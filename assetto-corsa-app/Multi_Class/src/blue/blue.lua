-- Lapped-car rule (blue flag), kept simple: once a faster car has passed an AI car that it is lapping,
-- the lapped car must not pass it back. While the passer is ahead and within RELEASE_GAP_M, the lapped
-- car's top speed is capped just under the passer's speed; beyond that gap it races freely again.
-- Lines, lanes and every other control are left alone.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local RCtl = require("src/rolling/control")

local sFormat = string.format
local getCar = ac.getCar

local M = {}

local clock = 0.0
local lastScanAt = -1000.0

local function inPit(car)
  return car.isInPitlane == true or car.isInPit == true
end

local function progress(car)
  if type(car.splinePosition) ~= "number" then return nil end
  return U.NormalizeTrackProgress(car.splinePosition)
end

-- Signed distance in metres from progress pa to pb along the track: positive when pb is ahead.
local function signedGap(pa, pb, trackLength)
  return ((pb - pa + 0.5) % 1.0 - 0.5) * trackLength
end

local function skipCar(i, car)
  if inPit(car) then return true end
  local y = S.yellowCars[i]
  return S.fcy ~= "green" or (y and (y.hazard or y.limited or y.releasing)) or false
end

-- Nearest car just ahead of car i that is at least a lap up on it: the car that has lapped it.
local function nearLine(p, trackLength)
  local m = C.BLUE_LINE_MARGIN_M / trackLength
  return p < m or p > 1.0 - m
end

local function findPasser(i, car, p, trackLength)
  local best, bestGap
  -- The lap counter and the spline wrap do not flip on the same frame, so right at the start/finish
  -- line the lap difference is unreliable (it briefly reads a whole lap off). Never judge there.
  if nearLine(p, trackLength) then return nil end
  for j = 0, S.driverCount - 1 do
    local other = j ~= i and getCar(j) or nil
    local op = other and not inPit(other) and progress(other)
    if op and not nearLine(op, trackLength) and (other.speedKmh or 0) >= C.BLUE_MIN_PASSER_KMH then
      local ahead = signedGap(p, op, trackLength)
      if ahead > C.BLUE_PASSED_M and ahead <= C.BLUE_ENGAGE_GAP_M and (not bestGap or ahead < bestGap) then
        -- Lap counters tick at the line while the spline wraps there: remove the distance between
        -- the cars and round to whole laps.
        local diff = (other.lapCount or 0) + op - ((car.lapCount or 0) + p) - ahead / trackLength
        if math.floor(diff + 0.5) >= 1 then best, bestGap = j, ahead end
      end
    end
  end
  return best
end

local function drop(i, st, reason)
  if st.passer then U.Log(sFormat("BLUE: t=%.1f car %d free again (%s)", clock, i, reason)) end
  RCtl.RestoreAIControl(i, false)
  st.passer = nil
  st.cap, st.capAt = nil, nil
end

-- A clearly faster car close behind the lapped car: it must not be slowed in front of it.
local function fasterCarBehind(i, car, p, trackLength)
  local mine = car.speedKmh or 0
  for j = 0, S.driverCount - 1 do
    local other = j ~= i and getCar(j) or nil
    local op = other and not inPit(other) and progress(other)
    if op and (other.speedKmh or 0) >= mine + C.BLUE_BEHIND_FASTER_KMH then
      local gap = signedGap(p, op, trackLength)
      if gap < 0 and gap > -C.BLUE_BEHIND_M then return j end
    end
  end
  return nil
end

local function holdCar(i, car, p, st, trackLength)
  local passer = getCar(st.passer)
  local pp = passer and progress(passer)
  local ahead = pp and signedGap(p, pp, trackLength)
  if not ahead then
    drop(i, st, "passer gone")
  elseif ahead >= C.BLUE_RELEASE_GAP_M then
    drop(i, st, sFormat("passer %.0f m ahead", ahead))
  elseif ahead < 0 then
    drop(i, st, "passer is behind again")
  elseif fasterCarBehind(i, car, p, trackLength) then
    st.noHoldUntil = clock + C.BLUE_BEHIND_COOLDOWN_S
    drop(i, st, "faster car closing from behind")
  elseif (passer.speedKmh or 0) < C.BLUE_MIN_PASSER_KMH or inPit(passer) then
    -- A slow or stopped passer must not drag the lapped car down with it (a held car crawling at the
    -- floor speed was being called a yellow hazard).
    drop(i, st, "passer is slow")
  else
    -- The closer the passer, the further under its speed the lapped car is held, so it can neither
    -- close up nor punch the faster car when that one brakes.
    local factor = C.BLUE_HOLD_FACTOR
    if ahead < C.BLUE_CLOSE_GAP_M then
      factor = C.BLUE_CLOSE_FACTOR + (C.BLUE_HOLD_FACTOR - C.BLUE_CLOSE_FACTOR) * (ahead / C.BLUE_CLOSE_GAP_M)
    end
    local cap = math.max(C.BLUE_MIN_SPEED_KMH, (passer.speedKmh or 0) * factor)
    -- The cap only falls gradually: the passer braking for a corner must not make this car brake
    -- on the straight with the field arriving behind it.
    local prev = st.cap or math.max(car.speedKmh or 0, cap)
    local dtCap = st.cap and clock - (st.capAt or clock) or 0
    st.capAt = clock
    if cap < prev then cap = st.cap and math.max(cap, prev - C.BLUE_CAP_RAMP_KMH_S * dtCap) or prev end
    st.cap = cap
    pcall(physics.setAITopSpeed, i, cap)
    if ahead < C.BLUE_CLOSE_GAP_M and (car.speedKmh or 0) > cap then
      pcall(physics.setAICaution, i, C.BLUE_CLOSE_CAUTION)
    else
      pcall(physics.setAICaution, i, 1.0)
    end
  end
end

-- A faster car that has lapped car i and is right beside it (not yet clear in front, not still behind).
local function lappingCarAlongside(i, car, p, trackLength)
  if nearLine(p, trackLength) or not car.position then return nil end
  local mine = car.speedKmh or 0
  for j = 0, S.driverCount - 1 do
    local other = j ~= i and getCar(j) or nil
    local op = other and other.position and not inPit(other) and progress(other)
    if op and not nearLine(op, trackLength) and (other.speedKmh or 0) >= mine + 10
        and (other.speedKmh or 0) >= C.BLUE_MIN_PASSER_KMH then
      local dx, dz = other.position.x - car.position.x, other.position.z - car.position.z
      if dx * dx + dz * dz <= C.BLUE_ALONGSIDE_M * C.BLUE_ALONGSIDE_M then
        local gap = signedGap(p, op, trackLength)
        local diff = (other.lapCount or 0) + op - ((car.lapCount or 0) + p) - gap / trackLength
        if math.floor(diff + 0.5) >= 1 then return j end
      end
    end
  end
  return nil
end

local function scan(trackLength)
  for i = 1, S.driverCount - 1 do
    local car = getCar(i)
    local p = car and progress(car)
    local st = p and S.blueCars[i]
    if p and not st then st = {}; S.blueCars[i] = st end
    if st and not skipCar(i, car) then
      local side = lappingCarAlongside(i, car, p, trackLength)
      if side then
        if not st.yieldCap then
          st.yieldCap = math.max(C.BLUE_YIELD_MIN_KMH, (car.speedKmh or 0) - C.BLUE_YIELD_SLOW_KMH)
          U.Log(sFormat("BLUE: t=%.1f car %d (%.0f km/h) yields to lapping car %d alongside; cap %.0f km/h",
              clock, i, car.speedKmh or 0, side, st.yieldCap))
        end
        st.yieldUntil = clock + C.BLUE_YIELD_HOLD_S
      end
    end
    if st and not st.passer and not (st.noHoldUntil and clock < st.noHoldUntil) and not skipCar(i, car) then
      local passerIndex = findPasser(i, car, p, trackLength)
      -- The same passer must be seen for BLUE_CONFIRM_SECONDS so a one-frame lap-counter glitch
      -- cannot start a hold.
      if passerIndex and st.cand == passerIndex then
        if clock - st.candSince < C.BLUE_CONFIRM_SECONDS then passerIndex = nil end
      else
        st.cand, st.candSince = passerIndex, clock
        passerIndex = nil
      end
      if passerIndex then
        st.cand = nil
        st.passer = passerIndex
        U.Log(sFormat("BLUE: t=%.1f car %d (%.0f km/h) was passed by lapping car %d; not passing back",
            clock, i, car.speedKmh or 0, passerIndex))
      end
    end
  end
end

-- Hand every car back and forget all state.
function M.Reset()
  for i, st in pairs(S.blueCars) do
    if (st.passer or st.yieldCap) and i ~= 0 then RCtl.RestoreAIControl(i, false) end
  end
  S.blueCars = {}
  clock = 0.0
  lastScanAt = -1000.0
end

local function shouldRun(sim)
  if not S.blueFlagEnabled then return false end
  if sim.isSessionStarted ~= true or not Sess.IsRaceMode(sim) then return false end
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then return false end
  return not Sess.IsSessionFinished(sim)
end

function M.Update(sim, dt)
  if not shouldRun(sim) then
    if next(S.blueCars) ~= nil then M.Reset() end
    return
  end
  local trackLength = sim.trackLengthM
  if not U.IsFinite(trackLength) or trackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then return end

  clock = clock + dt
  if clock - lastScanAt >= C.BLUE_SCAN_SECONDS then
    lastScanAt = clock
    scan(trackLength)
  end

  for i, st in pairs(S.blueCars) do
    local car = getCar(i)
    local p = car and progress(car)
    if not p then
      S.blueCars[i] = nil
    elseif st.yieldCap and st.yieldUntil and clock < st.yieldUntil and not skipCar(i, car) then
      -- A faster car is beside this one: slow down and let it go by.
      pcall(physics.setAITopSpeed, i, st.yieldCap)
      pcall(physics.setAICaution, i, 1.0)
    elseif st.yieldCap then
      st.yieldCap, st.yieldUntil = nil, nil
      if not st.passer then RCtl.RestoreAIControl(i, false) end
    elseif st.passer then
      if skipCar(i, car) then
        -- Pits or yellow take over the car's controls; forget the hold. A car in the pits must not
        -- keep the speed cap into its next stint.
        if inPit(car) then RCtl.RestoreAIControl(i, false) end
        st.passer, st.cap = nil, nil
      else
        holdCar(i, car, p, st, trackLength)
      end
    end
  end
end

return M
