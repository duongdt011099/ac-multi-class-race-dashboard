-- Rolling start: class grouping, lane offsets and release/grid checks.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")

local sFormat = string.format
local getCar = ac.getCar

local M = {}

function M.RollingClassLabel(classKey)
  if classKey == "__rolling_unclassified" then return "Unclassified" end

  for _, className in ipairs(S.releaseClasses) do
    if string.lower("class" .. className) == classKey then
      return className
    end
  end

  return tostring(classKey):gsub("^class", "")
end

local function getGridPosition(carIndex)
  local position = S.rollingStartGridPositions[carIndex]
  if type(position) == "number" and position > 0 then return position end

  local car = getCar(carIndex)
  position = car and car.racePosition or nil
  if type(position) == "number" and position > 0 then return position end
  return carIndex + 1
end

local function ensureGroup(groups, byClass, classKey)
  local group = byClass[classKey]
  if not group then
    group = { classKey = classKey, label = M.RollingClassLabel(classKey), cars = {} }
    byClass[classKey] = group
    groups[#groups + 1] = group
  end
  return group
end

local function sortGroupCars(group)
  table.sort(group.cars, function(a, b)
    local positionA = getGridPosition(a)
    local positionB = getGridPosition(b)

    if positionA == positionB then return a < b end
    return positionA < positionB
  end)
  group.gridPosition = #group.cars > 0 and getGridPosition(group.cars[1]) or math.huge
end

local function sortedNonEmpty(groups)
  local nonEmpty = {}
  for _, group in ipairs(groups) do
    if #group.cars > 0 then
      nonEmpty[#nonEmpty + 1] = group
    end
  end

  table.sort(nonEmpty, function(a, b)
    if a.gridPosition == b.gridPosition then
      return a.classKey < b.classKey
    end
    return a.gridPosition < b.gridPosition
  end)
  return nonEmpty
end

local function logGroupOrder(group)
  local positions = {}
  for _, carIndex in ipairs(group.cars) do
    positions[#positions + 1] = tostring(getGridPosition(carIndex))
  end
  U.Log(sFormat("ROLLING: grid order class '%s' start=%d positions={%s}",
      group.label, group.gridPosition, table.concat(positions, ",")))
end

function M.BuildRollingStartGroups()
  local groups = {}
  local byClass = {}

  -- Seed known classes, then derive their actual rolling order from AC's captured grid positions.
  -- The dashboard writes the qualifying order into the race grid; current best laps are not a
  -- reliable substitute for that order once the session has loaded.
  for _, classKey in ipairs(S.classOrder) do
    ensureGroup(groups, byClass, classKey)
  end

  for carIndex = 0, S.driverCount - 1 do
    local classKey = S.driverClass[carIndex] or "__rolling_unclassified"
    local group = ensureGroup(groups, byClass, classKey)
    group.cars[#group.cars + 1] = carIndex
  end

  for _, group in ipairs(groups) do
    sortGroupCars(group)
  end

  local nonEmpty = sortedNonEmpty(groups)
  for _, group in ipairs(nonEmpty) do
    logGroupOrder(group)
  end

  return nonEmpty
end

-- Lane follows the car's overall starting grid slot: odd and even slots take opposite lanes, and
-- rollingStartPoleOnRight decides which physical side the odd slots occupy (mirrors the reference
-- app's "Target Lane for Pole Position" setting). The value is auto-detected from the human car's
-- grid side each session; a manual toggle overrides it for the current session.
function M.GetRollingLaneOffset(gridPosition)
  local isOdd = ((gridPosition or 1) % 2 == 1)
  if S.rollingStartPoleOnRight then
    return isOdd and C.ROLLING_START_LANE_METERS or -C.ROLLING_START_LANE_METERS
  end
  return isOdd and -C.ROLLING_START_LANE_METERS or C.ROLLING_START_LANE_METERS
end

-- Gentle side-to-side wave once the race is under way, so the two columns are not perfectly rigid.
function M.GetOrganicLaneOffset(carIndex)
  if not S.rollingStartRaceHasStarted then return 0.0 end

  local time = S.rollingStartSessionTime
  local wave1 = math.sin(time * 0.25 + carIndex * 1.7) * 0.40
  local wave2 = math.cos(time * 0.40 - carIndex * 2.3) * 0.10
  return wave1 + wave2
end

-- Time-based release: a car goes when the shared release timer reaches its class start plus its
-- own slot within the class. The first car of the first class has threshold 0, so the overall
-- leader always gets the field moving (which then starts the timer via the "field is moving"
-- check). Cars leave one at a time in grid order, so the run-up is a true single file.
-- Nearest car ahead in release order that is an AI car and still running (not dropped).
local function releaseReferenceState(state)
  for queueIndex = (state.queueIndex or 1) - 1, 1, -1 do
    local carIndex = S.rollingStartQueue[queueIndex]
    local other = carIndex and S.rollingStartCars[carIndex]
    if other and carIndex ~= 0 and not other.dropped then return other, carIndex end
  end
  return nil
end

local function isNotStuck(other, otherIndex)
  if not other.released then return false end
  local car = getCar(otherIndex)
  if not car then return true end
  if S.rollingStartSessionTime - (other.releasedAt or 0) < C.ROLLING_START_RELEASE_GRACE_SECONDS then
    return true
  end
  if (car.speedKmh or 0) >= C.ROLLING_START_RELEASE_MOVED_KMH then return true end
  local sim = ac.getSim()
  local trackLength = sim and sim.trackLengthM
  if other.gridProgress and type(car.splinePosition) == "number" and U.IsFinite(trackLength) then
    local d = car.splinePosition - other.gridProgress
    if d > 0.5 then d = d - 1.0 elseif d < -0.5 then d = d + 1.0 end
    return d * trackLength >= C.ROLLING_START_RELEASE_MOVED_METERS
  end
  return false
end

-- Time-based release; a car also waits while the car ahead of it is still getting rolling
-- (released, but not yet up to MOVED_KMH / MOVED_METERS), so a late starter is not let out on
-- top of a car that has not launched yet. A healthy car ahead never delays the queue for long.
-- The wait is capped so one stuck car cannot freeze the field.
function M.IsRollingCarReleased(state)
  local group = S.rollingStartGroups[state.groupIndex]
  if not group then return true end
  local slot = (state.classPosition or 1) - 1
  local threshold = (group.classStartTime or 0.0) + slot * C.ROLLING_START_CAR_SECONDS
  if S.rollingStartReleaseTimer < threshold then return false end
  if S.rollingStartReleaseTimer >= C.ROLLING_START_RELEASE_MAX_SECONDS then return true end

  local other, otherIndex = releaseReferenceState(state)
  if not other or isNotStuck(other, otherIndex) then return true end

  if not state.gateWaitStart then
    state.gateWaitStart = S.rollingStartSessionTime
    U.Log(sFormat("ROLLING: car %d waiting for car %d to get rolling", state.carIndex, otherIndex))
  end
  local refReleasedAt = other.releasedAt or S.rollingStartSessionTime
  if S.rollingStartSessionTime - refReleasedAt >= C.ROLLING_START_RELEASE_WAIT_TIMEOUT then
    U.Log(sFormat("ROLLING: car %d release gate timed out waiting for car %d; releasing anyway",
        state.carIndex, otherIndex))
    return true
  end
  return false
end
local function carStillAtGrid(car)
  if not car or (car.speedKmh or 0) > 3.0 or (car.sessionLapCount or car.lapCount or 0) > 0 then
    return false
  end

  local progress = U.NormalizeTrackProgress(car.splinePosition or 0)
  return not (progress > 0.1 and progress < 0.9)
end

function M.IsFieldStillAtStartingGrid(sim)
  if S.driverCount <= 0 then return false end

  local session = ac.getSession and ac.getSession(sim.currentSessionIndex) or nil
  if session and type(session.leaderCompletedLaps) == "number" and session.leaderCompletedLaps > 0 then
    return false
  end

  for carIndex = 0, S.driverCount - 1 do
    if not carStillAtGrid(getCar(carIndex)) then return false end
  end

  return true
end

return M
