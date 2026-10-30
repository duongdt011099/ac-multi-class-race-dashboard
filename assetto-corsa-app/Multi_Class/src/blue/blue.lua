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
local function findPasser(i, car, p, trackLength)
  local best, bestGap
  for j = 0, S.driverCount - 1 do
    local other = j ~= i and getCar(j) or nil
    local op = other and not inPit(other) and progress(other)
    if op then
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
  st.cap = nil
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
  else
    local cap = math.max(C.BLUE_MIN_SPEED_KMH, (passer.speedKmh or 0) * C.BLUE_HOLD_FACTOR)
    st.cap = cap
    pcall(physics.setAITopSpeed, i, cap)
  end
end

local playerLapped = false

local function scan(trackLength)
  local me = getCar(0)
  local mp = me and not inPit(me) and progress(me)
  playerLapped = mp and findPasser(0, me, mp, trackLength) ~= nil or false
  for i = 1, S.driverCount - 1 do
    local car = getCar(i)
    local p = car and progress(car)
    local st = p and S.blueCars[i]
    if p and not st then st = {}; S.blueCars[i] = st end
    if st and not st.passer and not skipCar(i, car) then
      local passerIndex = findPasser(i, car, p, trackLength)
      if passerIndex then
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
    if st.passer and i ~= 0 then RCtl.RestoreAIControl(i, false) end
  end
  S.blueCars = {}
  S.blueActive = false
  S.blueLapping = false
  playerLapped = false
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

  local lapping = false
  for i, st in pairs(S.blueCars) do
    local car = getCar(i)
    local p = car and progress(car)
    if not p then
      S.blueCars[i] = nil
    elseif st.passer then
      if skipCar(i, car) then
        -- Pits or yellow take over the car's controls; just forget the hold.
        st.passer, st.cap = nil, nil
      else
        holdCar(i, car, p, st, trackLength)
      end
    end
    if st.passer == 0 then lapping = true end
  end
  S.blueActive = playerLapped
  S.blueLapping = lapping
end

return M
