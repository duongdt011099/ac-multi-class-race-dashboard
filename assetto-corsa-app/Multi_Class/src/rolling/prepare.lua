-- Rolling start: staging (PrepareRollingStart) and formation kickoff (BeginRollingStart).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local RCtl = require("src/rolling/control")
local PoleSide = require("src/rolling/pole_side")
local RGroups = require("src/rolling/groups")

local sFormat = string.format
local getCar = ac.getCar

local M = {}

local function IsPreStart(sim)
  local session = ac.getSession and ac.getSession(sim.currentSessionIndex) or nil
  local preStart = sim.isSessionStarted ~= true
  if session and type(session.startTime) == "number" and type(sim.time) == "number" then
    preStart = preStart or sim.time < session.startTime
  end
  return preStart or RGroups.IsFieldStillAtStartingGrid(sim)
end

-- Returns an unavailability reason, or nil when staging can proceed.
local function CheckStagingPossible(sim)
  if S.driverCount <= 0 or
      not ac.hasTrackSpline or not ac.hasTrackSpline() then
    return "Track spline or car data is unavailable."
  end

  local trackLength = sim.trackLengthM
  if not U.IsFinite(trackLength) or trackLength <= 500 then
    return "Track length is unavailable or too short."
  end

  if not IsPreStart(sim) then
    return "The race was already moving when the Lua app loaded."
  end
  return nil
end

-- Flat front-to-back release order derived from the class ordering (grid position 1 first). The
-- field is NOT moved: cars launch from AC's real grid as the queue releases them.
local function BuildReleaseQueue(groups)
  local queue = {}
  for groupIndex, group in ipairs(groups) do
    group.index = groupIndex
    for _, carIndex in ipairs(group.cars) do
      queue[#queue + 1] = { carIndex = carIndex, groupIndex = groupIndex }
    end
  end
  return queue
end

local function BuildCarByGridPosition()
  local carByGridPosition = {}
  for carIndex = 0, S.driverCount - 1 do
    local grid = S.rollingStartGridPositions[carIndex]
    if type(grid) == "number" and grid > 0 then carByGridPosition[grid] = carIndex end
  end
  return carByGridPosition
end

local function NewCarState(group, groupIndex, classPosition, carIndex, carByGridPosition)
  local gridPosition = S.rollingStartGridPositions[carIndex] or (carIndex + 1)
  local partnerGrid = (gridPosition % 2 == 1) and (gridPosition + 1) or (gridPosition - 1)
  local partnerIndex = carByGridPosition[partnerGrid]
  local startSide = PoleSide.MeasureRollingStartSide(getCar(carIndex), partnerIndex and getCar(partnerIndex))
  if not startSide then
    startSide = RGroups.GetRollingLaneOffset(gridPosition) < 0 and -1 or 1
  end
  return {
    carIndex = carIndex,
    groupIndex = groupIndex,
    classKey = group.classKey,
    classPosition = classPosition,
    row = math.floor((classPosition - 1) / 2),
    startSide = startSide,
    gridPosition = gridPosition,
    released = false,
    hasRolledOver = false,
    enteredPhase2 = false,
    lockedPhase2Position = nil,
    currentSplineOffset = 0.0,
    green = false,
    wrapCount = 0,
    travelled = 0.0,
    lastProgress = nil,
  }
end

-- Per-car rolling state. Longitudinal positions are class-relative so each class forms its own
-- single-file then double-file grid, while the lane is measured from the car's own starting grid
-- side (so the columns reproduce the real grid). The player is included so the AI queue behind
-- them, but is never controlled.
local function BuildCarStates(groups, carByGridPosition)
  for groupIndex, group in ipairs(groups) do
    group.rows = math.max(1, math.ceil(#group.cars / 2))
    for classPosition, carIndex in ipairs(group.cars) do
      S.rollingStartCars[carIndex] =
          NewCarState(group, groupIndex, classPosition, carIndex, carByGridPosition)
    end
  end
end

local function AssignQueueIndices(queue)
  for queueIndex, entry in ipairs(queue) do
    S.rollingStartQueue[queueIndex] = entry.carIndex
    local state = S.rollingStartCars[entry.carIndex]
    local car = getCar(entry.carIndex)
    if state then
      state.queueIndex = queueIndex
      state.lastProgress = car and car.splinePosition or nil
    end
  end
end

-- Cumulative class release schedule: each class starts CLASS_GAP seconds after the previous
-- class's final car, so the gap between the tail of one and the head of the next is fixed.
local function ScheduleClassStarts(groups)
  local startTime = 0.0
  for _, group in ipairs(groups) do
    group.classStartTime = startTime
    startTime = startTime + #group.cars * C.ROLLING_START_CAR_SECONDS + C.ROLLING_START_CLASS_GAP_SECONDS
    group.green = false
    group.complete = false
    group.windowCloseTimer = 0
  end
end

local function StageCars(groups, queue)
  S.rollingStartGroups = groups
  S.rollingStartCars = {}
  S.rollingStartQueue = {}
  S.rollingStartLaneDiagDone = false

  -- Map of overall grid slot -> car so each car can find the partner sharing its two-wide row and
  -- measure which side it started on (used to keep the real grid sides in the double file).
  BuildCarStates(groups, BuildCarByGridPosition())
  AssignQueueIndices(queue)
  ScheduleClassStarts(groups)
end

function M.PrepareRollingStart(sim)
  if S.rollingStartState ~= "idle" or not Sess.IsRaceMode(sim) then return end

  local reason = CheckStagingPossible(sim)
  if reason then
    RCtl.MarkRollingStartUnavailable(reason)
    return
  end

  local groups = RGroups.BuildRollingStartGroups()
  if #groups == 0 then
    RCtl.MarkRollingStartUnavailable("No cars were available to stage.")
    return
  end

  local queue = BuildReleaseQueue(groups)
  StageCars(groups, queue)

  S.rollingStartState = "staged"
  S.rollingStartStatus = "ROLLING START READY"
  S.rollingStartSessionKey = Sess.GetCurrentSessionKey(sim)
  S.rollingStartLastFrame = nil
  S.rollingStartDiagFrame = 0
  U.Log(sFormat("ROLLING: prepared %d cars in %d classes (reference-style, no teleport); lapOffset=%d",
      #queue, #groups, S.liveRollingStartLapOffset))
end

-- Formation begins when AC green-lights the race. Every AI car is held on the grid; the row-0
-- leader of each class is released immediately (threshold 0) and the rest follow on the timer.
function M.BeginRollingStart(now)
  S.rollingStartRaceHasStarted = false
  S.rollingStartReleaseTimer = 0.0
  S.rollingStartSessionTime = 0.0
  S.rollingStartLastFrame = now

  -- One global block for the whole formation; released when the field is green / torn down.
  RCtl.AcquireRollingPitBlock()

  for _, carIndex in ipairs(S.rollingStartQueue) do
    local state = S.rollingStartCars[carIndex]
    if state and carIndex ~= 0 then
      local car = getCar(carIndex)
      state.lastProgress = car and car.splinePosition or state.lastProgress
      RCtl.HoldRollingCar(carIndex)
    end
  end

  U.Log(sFormat("ROLLING: formation beginning; %d cars in %d classes.", #S.rollingStartQueue, #S.rollingStartGroups))
end

return M
