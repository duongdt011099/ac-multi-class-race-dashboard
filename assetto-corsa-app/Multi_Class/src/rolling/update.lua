-- Rolling start: per-frame state machine (staged -> rolling -> complete).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local RCtl = require("src/rolling/control")
local Prepare = require("src/rolling/prepare")
local Formation = require("src/rolling/formation")
local Green = require("src/rolling/green")
local TrackShape = require("src/rolling/track_shape")
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

-- Gap in metres from a double-file car to the car physically ahead of it in its own lane (the lane
-- running order rebuilt every frame by formation.lua's BuildLaneOrder), nil for a lane leader.
-- Measured by actual order, not grid slot: when two cars of a lane swap, the slot-based gap ran
-- through the car in between and stayed ~27 m forever, holding the class until the 60 s timeout.
local function RowGap(st, car, trackLength)
  local other = st.laneAheadIndex and getCar(st.laneAheadIndex)
  if not (other and type(other.splinePosition) == "number") then return nil end
  local d = other.splinePosition - car.splinePosition
  if d > 0.5 then d = d - 1.0 elseif d < -0.5 then d = d + 1.0 end
  return d * trackLength
end

-- True once every active car of the class is in double file and the tail is within
-- GREEN_MAX_TAIL_GAP_M of the class leader.
local function ClassClosedUp(group, trackLength)
  local lead, tail
  for _, memberIndex in ipairs(group.cars) do
    local st = S.rollingStartCars[memberIndex]
    local car = st and not st.dropped and getCar(memberIndex)
    if car and type(car.splinePosition) == "number" then
      if not st.enteredPhase2 then
        group.greenBlocker = string.format("car %d not in double file yet", memberIndex)
        return false
      end
      lead = lead or car
      tail = car
      local ahead = lead.splinePosition - car.splinePosition
      if ahead > 0.5 then ahead = ahead - 1.0 elseif ahead < -0.5 then ahead = ahead + 1.0 end
      if ahead * trackLength > C.ROLLING_START_GREEN_MAX_TAIL_GAP_M then
        group.greenBlocker = string.format("car %d is %.0f m behind the class leader", memberIndex, ahead * trackLength)
        return false
      end
      -- Every car must also be tucked up to the row ahead of it, so the class does not take green
      -- with a row still trailing. This is checked row by row, not as a cumulative distance from the
      -- leader at ROW_M per row: the proximity guard holds same-lane cars ~10-12 m apart, so the
      -- cumulative check failed on a big class (16 cars: +22 m at the tail) and green never came.
      local rowGap = RowGap(st, car, trackLength)
      if rowGap and rowGap > C.ROLLING_START_GREEN_MAX_ROW_GAP_M then
        group.greenBlocker = string.format("car %d is %.0f m behind the row ahead", memberIndex, rowGap)
        return false
      end
    end
  end
  return true
end

local function CheckCarForGreen(carIndex, state, now, trackLength)
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
  -- A car teleported to the pits mid-formation jumps along the spline (e.g. 0.72 -> 0.03), which
  -- AccumulateTravel would read as a line crossing and arm the green for its whole class far from
  -- the line. Retired cars, and any frame that jumps further than a teleport, never arm green.
  local jump = progress - state.lastProgress
  if jump > 0.5 then jump = jump - 1.0 elseif jump < -0.5 then jump = jump + 1.0 end
  if state.retired or math.abs(jump) * (trackLength or 0) > C.ROLLING_START_TELEPORT_JUMP_METERS then
    state.lastProgress = progress
    return
  end
  local crossedForward = AccumulateTravel(state, progress)

  -- Travel is counted from the grid through the single-file run-up too (not only once double file
  -- starts), otherwise a car's first phase-2 frame measures from its stale grid progress and the
  -- class needs roughly an extra half lap before it can go green.
  if state.enteredPhase2 == true and crossedForward and (state.travelled or 0.0) >= C.ROLLING_START_GREEN_MIN_TRAVEL_LAP then
    state.greenArmed = true
  end
  -- Green is taken at the green point: the line itself, or a little past it when a late hairpin
  -- leaves too little double file before the line (TrackShape.GreenPoint).
  local greenPoint = TrackShape.GreenPoint(trackLength)
  if state.greenArmed and (greenPoint <= 0 or (progress >= greenPoint and progress < 0.5)) then
    local group = S.rollingStartGroups[state.groupIndex]
    if group and not group.green then
      -- Don't go green yet: wait for the rest of the class to close up into double file.
      group.greenPendingAt = group.greenPendingAt or now
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
local function DetectGreenCrossings(now, trackLength)
  for _, carIndex in ipairs(S.rollingStartQueue) do
    local state = S.rollingStartCars[carIndex]
    if state and not state.green then CheckCarForGreen(carIndex, state, now, trackLength) end
  end

  for _, group in ipairs(S.rollingStartGroups or {}) do
    if group.greenPendingAt and not group.green then
      local timedOut = now - group.greenPendingAt >= C.ROLLING_START_GREEN_HOLD_MAX_SECONDS
      local closedUp = ClassClosedUp(group, trackLength)
      if not closedUp and now - (group.greenWaitLogAt or -1000) >= 5.0 then
        group.greenWaitLogAt = now
        U.Log(string.format("ROLLING: class %s green held %.0f/%.0f s: %s", tostring(group.label),
            now - group.greenPendingAt, C.ROLLING_START_GREEN_HOLD_MAX_SECONDS, tostring(group.greenBlocker)))
      end
      if timedOut or closedUp then
        if timedOut then U.Log("ROLLING: class green forced after hold timeout.") end
        GreenWholeClass(group, now)
      end
    end
  end
end

local function AllClassesGreen()
  for _, group in ipairs(S.rollingStartGroups or {}) do
    if not group.green then return false end
  end
  return true
end

local function CompleteRollingStart()
  -- No car is left holding a lane offset now that the whole field is racing. A class that went
  -- green only just now (held for its tail, or a centred double file well off the racing line)
  -- can still be metres off the line: snapping that to 0 made the whole class swerve at once, so
  -- those cars keep easing back after completion (EasePostGreenOffsets).
  S.rollingPostGreenOffsets = {}
  for carIndex, state in pairs(S.rollingStartCars) do
    if carIndex ~= 0 or RCtl.DevAutopilotActive() then
      local offset = state.currentSplineOffset or 0.0
      if math.abs(offset) > 0.05 then
        S.rollingPostGreenOffsets[carIndex] = offset
      else
        RCtl.SetRollingLaneOffset(carIndex, 0.0)
      end
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

-- After completion: ease the leftover lane offsets back to the racing line at the green rate.
local function EasePostGreenOffsets(dt)
  local pending = S.rollingPostGreenOffsets
  if not pending or next(pending) == nil then return end
  local step = C.ROLLING_START_GREEN_OFFSET_RAMP_MPS * dt
  for carIndex, offset in pairs(pending) do
    if math.abs(offset) <= step then
      RCtl.SetRollingLaneOffset(carIndex, 0.0)
      pending[carIndex] = nil
    else
      offset = offset - (offset > 0 and step or -step)
      RCtl.SetRollingLaneOffset(carIndex, offset)
      pending[carIndex] = offset
    end
  end
end

function M.UpdateRollingStart(sim)
  if S.rollingStartState == "idle" then return end
  if TearDownIfSessionChanged(sim) then return end
  local now = type(sim.time) == "number" and sim.time / 1000 or 0
  if S.rollingStartState == "complete" then
    EasePostGreenOffsets(ClampedFrameDelta(now))
    return
  end
  if S.rollingStartState == "unavailable" or S.rollingStartState == "finished" then return end

  if HandleStagedOrEnded(sim, now) then return end
  if S.rollingStartState ~= "rolling" then return end

  local trackLength = sim.trackLengthM
  if not U.IsFinite(trackLength) or trackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then
    RCtl.MarkRollingStartUnavailable("Track length became unavailable during formation.")
    return
  end

  Formation.MaintainRollingFormation(ClampedFrameDelta(now), trackLength)
  DetectGreenCrossings(now, trackLength)

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
