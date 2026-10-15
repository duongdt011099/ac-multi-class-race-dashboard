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
function M.IsRollingCarReleased(state)
  local group = S.rollingStartGroups[state.groupIndex]
  if not group then return true end
  local slot = (state.classPosition or 1) - 1
  local threshold = (group.classStartTime or 0.0) + slot * C.ROLLING_START_CAR_SECONDS
  return S.rollingStartReleaseTimer >= threshold
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
