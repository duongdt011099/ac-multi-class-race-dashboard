-- Blue flag yielding. AI cars do not respect blue flags, so a lapped AI car that has a lap-leader
-- closing from behind is slowed, moved off the racing line and held back until the faster car is
-- well clear. Only lapped cars are flagged (class is irrelevant); the player is never controlled.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local RCtl = require("src/rolling/control")

local sFormat = string.format
local getCar = ac.getCar
local setAITopSpeed = physics.setAITopSpeed
local setAIThrottleLimit = physics.setAIThrottleLimit

local M = {}

function M.BlueFlagCarUsable(car)
  if not car then return false end
  if car.isInPit == true or car.isInPitlane == true then return false end
  if type(car.splinePosition) ~= "number" then return false end
  return true
end

function M.BlueFlagRelease(carIndex)
  S.blueFlagCars[carIndex] = nil
  RCtl.RestoreAIControl(carIndex, true)
end

function M.ReleaseAllBlueFlag()
  for carIndex in pairs(S.blueFlagCars) do
    M.BlueFlagRelease(carIndex)
  end
end

local function CollectUsableCars()
  local cars, progress = {}, {}
  for i = 0, S.driverCount - 1 do
    local ok, car = pcall(getCar, i)
    if ok and M.BlueFlagCarUsable(car) then
      cars[i] = car
      progress[i] = (car.sessionLapCount or 0) + car.splinePosition
    end
  end
  return cars, progress
end

-- Gap of faster car F relative to B, in metres. lapsUp is about 1 while F is a lap up on B.
local function Relation(ctx, b, f)
  local cars, progress, trackLength = ctx.cars, ctx.progress, ctx.trackLength
  local behindM = (((cars[b].splinePosition - cars[f].splinePosition) % 1)) * trackLength
  local lapsUp = (progress[f] - progress[b]) + behindM / trackLength
  return behindM, trackLength - behindM, lapsUp
end

-- Keep the current target while it is still relevant, so the hold-back phase survives the pass.
local function KeepCurrentTarget(ctx, b, state)
  if not (state and state.target and ctx.cars[state.target]) then return nil, nil end
  local f = state.target
  local behindM, aheadM, lapsUp = Relation(ctx, b, f)
  if lapsUp > 0.5 and lapsUp < 1.5 and behindM <= C.BLUE_CLEAR_M then
    return f, "approach"
  elseif lapsUp >= 1.5 and aheadM <= C.BLUE_RELEASE_M then
    return f, "hold"
  end
  return nil, nil
end

local function DetectNewTarget(ctx, b)
  local target, phase = nil, nil
  local bestGap = C.BLUE_DETECT_M
  for f, fcar in pairs(ctx.cars) do
    if f ~= b then
      local behindM, _, lapsUp = Relation(ctx, b, f)
      if lapsUp > 0.5 and lapsUp < 1.5 and behindM < bestGap
          and (fcar.speedKmh or 0) > C.BLUE_STOPPED_KMH then
        bestGap = behindM
        target, phase = f, "approach"
      end
    end
  end
  return target, phase
end

local function ApplyYield(ctx, b, state, target, phase, dt)
  local behindM = Relation(ctx, b, target)
  local yield = phase == "hold" or behindM <= C.BLUE_YIELD_M
  local wantThrottle, wantOffset = 1.0, 0.0
  if yield then
    wantThrottle = C.BLUE_THROTTLE
    wantOffset = S.blueFlagYieldRight and C.BLUE_LANE_METERS or -C.BLUE_LANE_METERS
    local cap = math.max((ctx.cars[target].speedKmh or 0) * C.BLUE_SPEED_FACTOR, C.BLUE_MIN_SPEED_KMH)
    pcall(setAITopSpeed, b, cap)
  else
    pcall(setAITopSpeed, b, 999999.0)
  end

  local step = C.BLUE_THROTTLE_RAMP * dt
  state.throttle = state.throttle + math.max(-step, math.min(step, wantThrottle - state.throttle))
  local laneStep = C.BLUE_LANE_RAMP_MPS * dt
  state.offset = state.offset + math.max(-laneStep, math.min(laneStep, wantOffset - state.offset))
  pcall(setAIThrottleLimit, b, state.throttle)
  RCtl.SetRollingLaneOffset(b, state.offset)
  state.releasing = false
end

local function FlagCar(ctx, b, state, target, phase, dt)
  if not state then
    state = { throttle = 1.0, offset = 0.0 }
    S.blueFlagCars[b] = state
  end
  if state.target ~= target then
    U.Log(sFormat("BLUE: car %d flagged for car %d", b, target))
  end
  state.target = target
  state.phase = phase
  ApplyYield(ctx, b, state, target, phase, dt)
end

-- No faster car around any more: ease controls back before handing the AI over.
local function EaseBackControls(b, state, dt)
  if state.target then
    U.Log(sFormat("BLUE: car %d released", b))
    state.target = nil
  end
  local step = C.BLUE_THROTTLE_RAMP * dt
  local laneStep = C.BLUE_LANE_RAMP_MPS * dt
  state.throttle = math.min(1.0, state.throttle + step)
  state.offset = state.offset + math.max(-laneStep, math.min(laneStep, -state.offset))
  pcall(setAITopSpeed, b, 999999.0)
  if state.throttle >= 1.0 and math.abs(state.offset) < 0.01 then
    M.BlueFlagRelease(b)
  else
    pcall(setAIThrottleLimit, b, state.throttle)
    RCtl.SetRollingLaneOffset(b, state.offset)
  end
end

local function UpdateCar(ctx, b, dt)
  local car = ctx.cars[b]
  local state = S.blueFlagCars[b]
  local finished = car and S.liveTotalLaps > 0 and (car.lapCount or 0) >= S.liveTotalLaps + S.liveRollingStartLapOffset

  if car and not finished and (car.speedKmh or 0) > C.BLUE_STOPPED_KMH then
    local target, phase = KeepCurrentTarget(ctx, b, state)
    if not target then target, phase = DetectNewTarget(ctx, b) end

    if target then
      FlagCar(ctx, b, state, target, phase, dt)
    elseif state then
      EaseBackControls(b, state, dt)
    end
  elseif state then
    M.BlueFlagRelease(b)
  end
end

function M.UpdateBlueFlagYield(sim, dt)
  local rollingBusy = S.rollingStartState == "staged" or S.rollingStartState == "rolling"
  if not S.blueFlagEnabled or rollingBusy or sim.isSessionStarted ~= true
      or not Sess.IsRaceMode(sim) or Sess.IsSessionFinished(sim) then
    M.ReleaseAllBlueFlag()
    return
  end

  local trackLength = sim.trackLengthM
  if not U.IsFinite(trackLength) or trackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then return end
  if dt > 0.25 then dt = 0.25 end

  local cars, progress = CollectUsableCars()
  local ctx = { cars = cars, progress = progress, trackLength = trackLength }
  for b = 1, S.driverCount - 1 do
    UpdateCar(ctx, b, dt)
  end
end

return M
