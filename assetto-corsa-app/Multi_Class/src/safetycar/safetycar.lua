-- Safety car for the rolling-start formation lap.
--
-- The dashboard adds one extra AI car as the LAST entry of race.ini and records its index in
-- [HEADER] __MCR_SAFETY_CAR_INDEX. That car is not a competitor: Frame.Update shortens S.driverCount so
-- that every other module (classes, grid, leaderboard, export, yellow, blue, ...) never sees it, and
-- this module is the only one that controls it.
--
--   staged   -> the car is moved SAFETY_CAR_AHEAD_M in front of the first row of the first class and held
--   rolling  -> it leads the formation lap, never faster than the race cars' formation speed
--   double file (first class leader) / green / session end -> it is sent to the pits and held there
--              for the rest of the session
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")

local sFormat = string.format
local getCar = ac.getCar

local M = {}

local cachedIndex = nil       -- safety car index read from race.ini (nil = not found yet)
local lastIndexReadAt = -1000.0
local seenAttempt = nil       -- last S.rollingAttempt handled: a new staging means a new attempt
local rollingSince = nil
local pitStartAt = 0.0          -- when the safety car was told to drive to the pits
local pitInAt = nil           -- when it entered the pit lane
local lastPitRequestAt = -1000.0
local phase = "idle"          -- idle | waiting | leading | pitting | retiring | retired
local clock = 0.0
local leadingSince = 0.0
local lastRetryAt = -1000.0
local retireTries = 0
local dirSign = -1.0         -- flipped when the placed car turns out to face the wrong way
local placedAt = nil          -- clock time of the last placement, nil once its heading was verified
local placeTries = 0

local function readIndex()
  if cachedIndex then return cachedIndex end
  local now = os.clock()
  if now - lastIndexReadAt < 2.0 then return nil end
  lastIndexReadAt = now
  if ac.INIConfig and type(ac.INIConfig.raceConfig) == "function" then
    local ok, value = pcall(function()
      return ac.INIConfig.raceConfig():get("HEADER", "__MCR_SAFETY_CAR_INDEX", -1)
    end)
    value = ok and tonumber(value) or -1
    if value and value >= 1 then cachedIndex = math.floor(value) end
  end
  return cachedIndex
end

-- Number of cars that are competitors. Also records the safety car index in S.safetyCarIndex.
function M.CompetitorCount(sim)
  local total = sim.carsCount or 0
  S.carsTotal = total
  local idx = readIndex()
  if idx and idx == total - 1 then
    S.safetyCarIndex = idx
    return idx
  end
  S.safetyCarIndex = nil
  return total
end

local function inPit(car)
  return car and (car.isInPitlane == true or car.isInPit == true)
end

local function hold(i)
  pcall(physics.setAITopSpeed, i, 0.0)
  pcall(physics.setAIThrottleLimit, i, 0.0)
  pcall(physics.setAICaution, i, 1.0)
  pcall(physics.setAIStopCounter, i, C.ROLLING_START_STOP_COUNTER_HELD)
end

local function signedGap(pa, pb, trackLength)
  return ((pb - pa + 0.5) % 1.0 - 0.5) * trackLength
end

-- The car at the front of the first class: the first one that is still in the formation.
local function poleCarIndex()
  local group = S.rollingStartGroups and S.rollingStartGroups[1]
  if not group then return nil end
  for _, idx in ipairs(group.cars) do
    local st = S.rollingStartCars[idx]
    if st and not st.dropped and getCar(idx) then return idx, group, st end
  end
  return nil
end

-- Move the safety car to the AI line AHEAD_M in front of the first row of the first class.
local function place(i, trackLength)
  local poleIdx, group = poleCarIndex()
  local pole = poleIdx and getCar(poleIdx)
  local car = getCar(i)
  if not (pole and car and type(pole.splinePosition) == "number" and physics.setCarPosition) then return false end

  local rowCars = { pole }
  local second = group.cars[2] and getCar(group.cars[2])
  if second and second.position and pole.position
      and second.position:distance(pole.position) < 8.0 then
    rowCars[2] = second
  end

  -- The pole car's own heading is the only reliable "down the track" direction on the grid (the
  -- spline direction came out reversed on some tracks), so the safety car always faces the same way.
  local look = pole.look
  local llen = math.sqrt(look.x * look.x + look.z * look.z)
  if llen < 1e-3 then return false end
  local dir = vec3(look.x / llen, 0, look.z / llen)

  local p = U.NormalizeTrackProgress(pole.splinePosition)
  local target
  if ac.trackProgressToWorldCoordinate then
    local okA, here = pcall(ac.trackProgressToWorldCoordinate, p)
    local okB, ahead = pcall(ac.trackProgressToWorldCoordinate, (p + C.SAFETY_CAR_AHEAD_M / trackLength) % 1.0)
    if okA and okB and here and ahead then
      -- Only trust the spline point when it really is in front of the pole car.
      local fwd = (ahead.x - pole.position.x) * dir.x + (ahead.z - pole.position.z) * dir.z
      if fwd > C.SAFETY_CAR_AHEAD_M * 0.7 and fwd < C.SAFETY_CAR_AHEAD_M * 1.5 then
        target = vec3(ahead.x, ahead.y + (pole.position.y - here.y), ahead.z)
      end
    end
  end
  if not target then
    -- Straight ahead of the row along the pole car's heading.
    local cx, cz = 0, 0
    for _, c in ipairs(rowCars) do cx, cz = cx + c.position.x / #rowCars, cz + c.position.z / #rowCars end
    target = vec3(cx + dir.x * C.SAFETY_CAR_AHEAD_M, pole.position.y, cz + dir.z * C.SAFETY_CAR_AHEAD_M)
  end

  local facing = vec3(dir.x * dirSign, 0, dir.z * dirSign)
  pcall(physics.setCarPosition, i, target, facing)
  placedAt = clock
  placeTries = placeTries + 1
  if physics.setCarVelocity then pcall(physics.setCarVelocity, i, vec3(0, 0, 0)) end
  U.Log(sFormat("SAFETY: car %d placed %.0f m ahead of pole car %d, heading %.2f/%.2f", i,
      C.SAFETY_CAR_AHEAD_M, poleIdx, dir.x, dir.z))
  return true
end
-- True while any car of the field has not been released from the grid yet.
local function fieldStillLaunching()
  for _, carIndex in ipairs(S.rollingStartQueue) do
    local other = S.rollingStartCars[carIndex]
    if other and not other.released and not other.dropped then return true end
  end
  return false
end

-- Same speeds as the race cars' formation lap: the leader's rolling speed while the field is still
-- being released, formation speed afterwards, eased in with the launch ramp and slowed when the
-- leader falls far behind.
local function lead(i, car, trackLength)
  local speed = fieldStillLaunching() and C.ROLLING_START_SPEED_KMH or C.ROLLING_START_FORMATION_SPEED_KMH
  local throttle = 1.0

  local launch = math.min(1.0, (clock - leadingSince) / C.ROLLING_START_LAUNCH_RAMP_SECONDS)
  launch = launch * launch * (3 - 2 * launch)
  if launch < 1.0 then
    throttle = C.ROLLING_START_LAUNCH_THROTTLE_START + (1.0 - C.ROLLING_START_LAUNCH_THROTTLE_START) * launch
    speed = math.min(speed, C.ROLLING_START_LAUNCH_SPEED_START_KMH + (speed - C.ROLLING_START_LAUNCH_SPEED_START_KMH) * launch)
  end

  local poleIdx = poleCarIndex()
  local pole = poleIdx and getCar(poleIdx)
  if pole and type(pole.splinePosition) == "number" and type(car.splinePosition) == "number" then
    local gap = signedGap(U.NormalizeTrackProgress(pole.splinePosition), U.NormalizeTrackProgress(car.splinePosition), trackLength)
    if gap > C.SAFETY_CAR_WAIT_START_M then
      local t = math.min(1.0, (gap - C.SAFETY_CAR_WAIT_START_M) / (C.SAFETY_CAR_WAIT_FULL_M - C.SAFETY_CAR_WAIT_START_M))
      speed = math.min(speed, speed + (C.ROLLING_START_PHASE1_BEHIND_MIN_SPEED - speed) * t)
    end
  end

  pcall(physics.setAITopSpeed, i, speed)
  pcall(physics.setAIThrottleLimit, i, throttle)
  pcall(physics.setAICaution, i, 1.0)
  pcall(physics.setAIStopCounter, i, 0.0)
  if S.safetyCarHolding and ((car.speedKmh or 0) > C.SAFETY_CAR_RELEASE_SPEED_KMH
      or clock - leadingSince > C.SAFETY_CAR_RELEASE_MAX_SECONDS) then
    S.safetyCarHolding = false
    U.Log(sFormat("SAFETY: car %d is rolling; releasing the field", i))
  end
end

-- Ask the AI to drive into the pit lane. It keeps leading the formation until it gets there, so it never
-- stops in front of the field. Returns false when this CSP build has no such request.
local function requestPitDrive(i)
  if type(physics.setAIPitStopRequest) ~= "function" then return false end
  local ok = pcall(physics.setAIPitStopRequest, i, true)
  return ok
end

local function retire(i, car, reason)
  S.safetyCarHolding = false
  if phase == "retired" then return end
  if phase ~= "retiring" then
    phase = "retiring"
    retireTries = 0
    lastRetryAt = -1000.0
    U.Log(sFormat("SAFETY: car %d going to the pits (%s)", i, reason))
  end
  if inPit(car) then
    phase = "retired"
    hold(i)
    U.Log(sFormat("SAFETY: car %d is in the pits; retired for the session", i))
    return
  end
  hold(i)
  if clock - lastRetryAt >= 1.0 then
    lastRetryAt = clock
    retireTries = retireTries + 1
    if retireTries <= 3 and physics.teleportCarTo and ac.SpawnSet then
      pcall(physics.teleportCarTo, i, ac.SpawnSet.Pits)
    else
      -- AC moves an AI car that has been stopped for long enough to the pits.
      pcall(physics.setAIStopCounter, i, C.YELLOW_TOW_COUNTER)
    end
  end
end

local function progressOf(car)
  return U.NormalizeTrackProgress(car.splinePosition)
end

-- True once the safety car stands SAFETY_CAR_AHEAD_M in front of the pole car, facing the same way.
-- Placement only happens while the field is standing on the grid: right after a restart the cars are
-- still in the aborted attempt's formation, and a car placed then is carried back to the last grid
-- slot when AC respawns the field.
local function ensurePlaced(i, car, sim)
  local trackLength = sim.trackLengthM
  local poleIdx = poleCarIndex()
  local pole = poleIdx and getCar(poleIdx)
  if not (pole and U.IsFinite(trackLength) and type(pole.splinePosition) == "number"
      and type(car.splinePosition) == "number") then return false end
  if (pole.speedKmh or 0) > C.ROLLING_START_GRID_MAX_SPEED_KMH then return false end

  local gap = signedGap(progressOf(pole), progressOf(car), trackLength)
  local dot = (pole.look and car.look) and (pole.look.x * car.look.x + pole.look.z * car.look.z) or 1
  local inPlace = math.abs(gap - C.SAFETY_CAR_AHEAD_M) <= C.SAFETY_CAR_PLACE_TOLERANCE_M
  if inPlace and dot > 0 then
    if placedAt then
      placedAt = nil
      U.Log(sFormat("SAFETY: car %d in position (gap=%.0f m, heading dot=%.2f)", i, gap, dot))
    end
    return true
  end
  if placedAt and clock - placedAt < 0.4 then return false end   -- let a fresh placement settle
  if inPlace and placedAt and placeTries < 4 then
    dirSign = -dirSign
    U.Log(sFormat("SAFETY: car %d faces the wrong way (dot=%.2f); flipping its heading", i, dot))
  end
  place(i, trackLength)
  return false
end

function M.Reset()
  pitStartAt, pitInAt, lastPitRequestAt = 0.0, nil, -1000.0
  rollingSince = nil
  placedAt = nil
  placeTries = 0
  S.safetyCarHolding = false
  phase = "idle"
  clock = 0.0
  leadingSince = 0.0
  retireTries = 0
  lastRetryAt = -1000.0
end

function M.Update(sim, dt)
  local i = S.safetyCarIndex
  if not i or not Sess.IsRaceMode(sim) then
    if phase ~= "idle" then M.Reset() end
    return
  end
  local car = getCar(i)
  if not car then return end
  clock = clock + dt

  local state = S.rollingStartState

  -- Every staging (first start or a restart from the menu) is a new attempt, even when this module
  -- never saw the state go through "idle" in between.
  if S.rollingAttempt ~= seenAttempt then
    seenAttempt = S.rollingAttempt
    if phase ~= "idle" then M.Reset() end
  end

  if state == "idle" then
    -- A new attempt (or restart) begins from scratch.
    if phase ~= "idle" then M.Reset() end
    return
  end

  if phase == "idle" then
    if state == "staged" then
      phase = "waiting"
      S.safetyCarHolding = true   -- the field waits for the safety car to get going first
      hold(i)
      return
    end
    -- Rolling start unavailable / already over: the car has no job.
    if sim.isSessionStarted == true then retire(i, car, "no formation lap") end
    return
  end

  if phase == "waiting" then
    hold(i)
    local ready = ensurePlaced(i, car, sim)
    if state == "rolling" then
      rollingSince = rollingSince or clock
      if ready or clock - rollingSince > C.SAFETY_CAR_PLACE_MAX_SECONDS then
        phase = "leading"
        leadingSince = clock
        U.Log(sFormat("SAFETY: car %d leading the formation lap (placed=%s)", i, tostring(ready)))
      end
    elseif state ~= "staged" then
      retire(i, car, "formation did not start")
    end
    return
  end

  if phase == "leading" then
    local trackLength = sim.trackLengthM
    local _, _, poleState = poleCarIndex()
    if state ~= "rolling" then
      retire(i, car, "formation over")
    elseif poleState and poleState.enteredPhase2 then
      if requestPitDrive(i) then
        phase = "pitting"
        pitStartAt, pitInAt, lastPitRequestAt = clock, nil, clock
        U.Log(sFormat("SAFETY: car %d driving to the pits (field in double file)", i))
      else
        retire(i, car, "field in double file; no pit-drive support")
      end
    elseif U.IsFinite(trackLength) then
      lead(i, car, trackLength)
    end
    return
  end

  if phase == "pitting" then
    local trackLength = sim.trackLengthM
    if clock - lastPitRequestAt >= 1.0 then
      lastPitRequestAt = clock
      requestPitDrive(i)
    end
    if inPit(car) then
      -- In the pit lane: let the AI drive to its box on its own, then park it for good.
      pitInAt = pitInAt or clock
      pcall(physics.setAITopSpeed, i, C.SAFETY_CAR_PIT_SPEED_KMH)
      pcall(physics.setAIThrottleLimit, i, 1.0)
      if (clock - pitInAt > 2.0 and (car.speedKmh or 0) < 2.0) or clock - pitInAt > C.SAFETY_CAR_PARK_MAX_SECONDS then
        phase = "retired"
        hold(i)
        U.Log(sFormat("SAFETY: car %d parked in the pits; retired for the session", i))
      end
    elseif state ~= "rolling" or clock - pitStartAt > C.SAFETY_CAR_PIT_TIMEOUT_SECONDS then
      -- The formation is over (or the pit entry never came): do not leave it on the track.
      retire(i, car, "could not drive to the pits in time")
    elseif U.IsFinite(trackLength) then
      lead(i, car, trackLength)   -- keeps leading at formation speed until the pit entry
    end
    return
  end

  -- retiring / retired: never goes back to the race.
  retire(i, car, "retired")
end

return M
