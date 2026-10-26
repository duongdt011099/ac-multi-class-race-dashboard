-- Rolling start: per-frame state machine (staged -> rolling -> complete).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local RCtl = require("src/rolling/control")
local Prepare = require("src/rolling/prepare")
local Formation = require("src/rolling/formation")
local Green = require("src/rolling/green")
local Audio = require("src/audio")

local getCar = ac.getCar

local M = {}

local function ResetToIdle()
  RCtl.RestoreRollingStartControls()
  S.rollingStartState = "idle"
  S.rollingStartStatus = ""
end

-- Returns true when the session/mode changed and the procedure was torn down.
local function TearDownIfSessionChanged(sim)
  if not Sess.IsRaceMode(sim) then
    ResetToIdle()
    return true
  end
  if S.rollingStartSessionKey and Sess.GetCurrentSessionKey(sim) ~= S.rollingStartSessionKey then
    ResetToIdle()
    return true
  end
  return false
end

-- Returns true when the frame must end here.
local function HandleStagedOrEnded(sim, now)
  if S.rollingStartState == "staged" then
    -- Cars stay on AC's real grid until the race actually starts. Nothing is teleported.
    if sim.isSessionStarted ~= true then return true end

    local startTrackLength = sim.trackLengthM
    if not U.IsFinite(startTrackLength) or startTrackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then
      RCtl.MarkRollingStartUnavailable("Track length became unavailable at race start.")
      return true
    end

    S.rollingStartState = "rolling"
    S.rollingStartStatus = "SINGLE FILE â€” FORMATION"
    Prepare.BeginRollingStart(now)
    U.Log("ROLLING: race start detected; formation beginning.")
  elseif S.rollingStartState == "rolling" and sim.isSessionStarted ~= true then
    RCtl.RestoreRollingStartControls()
    S.rollingStartState = "finished"
    S.rollingStartStatus = ""
    U.Log("ROLLING: session ended before every car received green; controls restored.")
    return true
  end
  return false
end

local function GreenWholeClass(group, now)
  group.green = true
  group.greenAt = now
  group.windowCloseTimer = 0
  for _, memberIndex in ipairs(group.cars) do
    local memberState = S.rollingStartCars[memberIndex]
    if memberState and not memberState.green then
      Green.GiveRollingGreen(memberIndex, memberState, now)
    end
  end
end

-- Fold the line wrap into the per-frame delta so travel stays continuous across laps.
local function AccumulateTravel(state, progress)
  local rawDelta = progress - state.lastProgress
  local delta = rawDelta
  if delta < -0.5 then
    delta = delta + 1.0
  elseif delta > 0.5 then
    delta = delta - 1.0
  end
  state.travelled = (state.travelled or 0.0) + delta
  if math.abs(rawDelta) > 0.5 then
    state.wrapCount = (state.wrapCount or 0) + 1
  end
  return rawDelta < -0.5
end

local function CheckCarForGreen(carIndex, state, now)
  local car = getCar(carIndex)
  if not (car and type(car.splinePosition) == "number") then
    return
  end
  if type(state.lastProgress) ~= "number" then
    state.lastProgress = U.NormalizeTrackProgress(car.splinePosition)
    state.travelled = state.travelled or 0.0
    state.wrapCount = state.wrapCount or 0
    return
  end
  local progress = U.NormalizeTrackProgress(car.splinePosition)
  local crossedForward = AccumulateTravel(state, progress)

  -- Travel is counted from the grid through the single-file run-up too (not only once double file
  -- starts), otherwise a car's first phase-2 frame measures from its stale grid progress and the
  -- class needs roughly an extra half lap before it can go green.
  if state.enteredPhase2 == true and crossedForward and (state.travelled or 0.0) >= C.ROLLING_START_GREEN_MIN_TRAVEL_LAP then
    local group = S.rollingStartGroups[state.groupIndex]
    if group and not group.green then
      GreenWholeClass(group, now)
    elseif not group then
      Green.GiveRollingGreen(carIndex, state, now)
    end
  end
  state.lastProgress = progress
end

-- Detect each car's line crossings. A class goes green the moment its first car crosses the
-- start line having already covered at least GREEN_MIN_TRAVEL_LAP of a lap since leaving the
-- grid. Measuring travel (rather than counting crossings) keeps this correct whether the grid
-- sits just before the line (first crossing immediate, green after a full lap) or just after it
-- (first crossing already ~a lap in, green right there). The old fixed "two crossings" rule
-- made grid-after-line tracks such as Spa force a second, ~10-minute formation lap.
local function DetectGreenCrossings(now)
  for _, carIndex in ipairs(S.rollingStartQueue) do
    local state = S.rollingStartCars[carIndex]
    if state and not state.green then CheckCarForGreen(carIndex, state, now) end
  end
end

local function AllClassesGreen()
  for _, group in ipairs(S.rollingStartGroups or {}) do
    if not group.green then return false end
  end
  return true
end

local function CompleteRollingStart()
  -- Make sure no car is left holding a lane offset now that the whole field is racing.
  for carIndex, state in pairs(S.rollingStartCars) do
    if carIndex ~= 0 then
      RCtl.SetRollingLaneOffset(carIndex, 0.0)
    end
    state.currentSplineOffset = 0.0
  end
  RCtl.ReleaseRollingPitBlock()
  Audio.FadeOut()
  S.rollingStartState = "complete"
  S.rollingStartStatus = "GREEN â€” GO"
  S.raceDisplayPending = nil
  S.raceDisplayStable = nil
  U.Log("ROLLING: every class received green.")
end

local function ClampedFrameDelta(now)
  local dt = now - (S.rollingStartLastFrame or now)
  if dt < 0 then dt = 0 end
  if dt > 0.25 then dt = 0.25 end
  S.rollingStartLastFrame = now
  return dt
end

function M.UpdateRollingStart(sim)
  if S.rollingStartState == "idle" then return end
  if TearDownIfSessionChanged(sim) then return end
  if S.rollingStartState == "unavailable" or S.rollingStartState == "complete" or S.rollingStartState == "finished" then return end

  local now = type(sim.time) == "number" and sim.time / 1000 or 0
  if HandleStagedOrEnded(sim, now) then return end
  if S.rollingStartState ~= "rolling" then return end

  local trackLength = sim.trackLengthM
  if not U.IsFinite(trackLength) or trackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then
    RCtl.MarkRollingStartUnavailable("Track length became unavailable during formation.")
    return
  end

  Formation.MaintainRollingFormation(ClampedFrameDelta(now), trackLength)
  DetectGreenCrossings(now)

  -- The procedure is complete once every class has gone green.
  if AllClassesGreen() then
    CompleteRollingStart()
    return
  end

  local playerState = S.rollingStartCars[0]
  if playerState and not playerState.green then
    S.rollingStartStatus = playerState.enteredPhase2 and "DOUBLE FILE â€” FORMATION" or "SINGLE FILE â€” FORMATION"
  end
end

return M
