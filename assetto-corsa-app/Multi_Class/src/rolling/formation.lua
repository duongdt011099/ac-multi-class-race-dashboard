-- Rolling start: per-frame formation maintenance (single file, double file, lane offsets, diagnostics).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local RCtl = require("src/rolling/control")
local PoleSide = require("src/rolling/pole_side")
local RGroups = require("src/rolling/groups")
local Spacing = require("src/rolling/spacing")

local sFormat = string.format
local getCar = ac.getCar
local setAITopSpeed = physics.setAITopSpeed
local setAICaution = physics.setAICaution
local setAIThrottleLimit = physics.setAIThrottleLimit
local setAISplineAbsoluteOffset = physics.setAISplineAbsoluteOffset

local M = {}

local function AdvanceReleaseClock(dt)
  S.rollingStartSessionTime = S.rollingStartSessionTime + dt

  -- The release clock only starts once the field is actually moving. Until then each class's row-0
  -- leader is already released (threshold 0), so the overall leader always gets things going.
  if S.rollingStartSessionTime > 0.5 and not S.rollingStartRaceHasStarted then
    for carIndex = 0, S.driverCount - 1 do
      local car = getCar(carIndex)
      if car and (car.speedKmh or 0) > 2.0 then
        S.rollingStartRaceHasStarted = true
        break
      end
    end
  end

  if S.rollingStartRaceHasStarted then
    S.rollingStartReleaseTimer = S.rollingStartReleaseTimer + dt
  end
end

-- Class-relative slot maps used by the phase-two (double file) spacing. Every car is placed by
-- its class position, not only those already double-filing, so a car that has not entered phase
-- two yet never leaves a hole in the column chain behind it.
local function BuildPositionMaps()
  for _, group in ipairs(S.rollingStartGroups or {}) do
    local map = {}
    for _, carIndex in ipairs(group.cars) do
      local state = S.rollingStartCars[carIndex]
      if state and not state.dropped then
        local pos = state.lockedPhase2Position or state.classPosition
        if pos and not map[pos] then map[pos] = carIndex end
      end
    end
    group.positionToCar = map
  end
end

local function HoldUnreleasedAI(carIndex)
  setAITopSpeed(carIndex, 0.0)
  setAIThrottleLimit(carIndex, 0.0)
  setAICaution(carIndex, 1.0)
  physics.setAIStopCounter(carIndex, C.ROLLING_START_STOP_COUNTER_HELD)
end

local function LockPhase2Position(state, group)
  state.enteredPhase2 = true
  state.phase2At = S.rollingStartSessionTime
  local pos = state.classPosition
  if group then
    for _, memberIndex in ipairs(group.cars) do
      local member = S.rollingStartCars[memberIndex]
      if member and member ~= state and member.enteredPhase2
          and member.lockedPhase2Position == pos then
        pos = state.classPosition + 0.5
        break
      end
    end
  end
  state.lockedPhase2Position = pos
end

-- Track the start-line crossing so a car only double-files on its flying lap.
local function TrackLineCrossing(state, car, group)
  if not (car and type(car.splinePosition) == "number") then return end
  if not state.hasRolledOver and car.splinePosition < C.ROLLING_START_ROLLED_OVER_SPLINE then
    state.hasRolledOver = true
  end

  if state.hasRolledOver and car.splinePosition >= C.ROLLING_START_FORMATION_POINT
      and not state.enteredPhase2 then
    LockPhase2Position(state, group)
  end
end

-- Green transition: ease the lane offset back to the racing line.
local function EaseOutLaneOffset(carIndex, state, isAI, greenLaneMove)
  local applied = state.currentSplineOffset or 0.0
  if math.abs(applied) > 0.001 then
    local diff = -applied
    if math.abs(diff) > greenLaneMove then
      applied = applied + (diff > 0 and greenLaneMove or -greenLaneMove)
    else
      applied = 0.0
    end
    state.currentSplineOffset = applied
    if isAI then RCtl.SetRollingLaneOffset(carIndex, applied) end
  end
end

-- Single-file run-up: cars released later start further back, so let the rear of the
-- pack run faster (up to PHASE1_CATCHUP) to close on the car ahead before the
-- formation point; otherwise the class reaches the double-file zone strung out.
local function SingleFileTargets(state, group, car, trackLength)
  local formationSpeed = C.ROLLING_START_FORMATION_SPEED_KMH
  local aheadCar
  if group and group.cars then
    -- Follow the nearest car ahead that is actually driving; a stalled/pitted car is skipped.
    for pos = (state.classPosition or 1) - 1, 1, -1 do
      local aheadIndex = group.cars[pos]
      local aheadState = aheadIndex and S.rollingStartCars[aheadIndex]
      if aheadIndex and not (aheadState and aheadState.dropped) then
        aheadCar = getCar(aheadIndex)
        break
      end
    end
  end
  if aheadCar and car then
    local gap = Spacing.SignedGap(aheadCar, car, trackLength)
    if gap < 0 then gap = 0 end
    formationSpeed = C.ROLLING_START_FORMATION_SPEED_KMH *
        Spacing.GetLinearMultiplier(gap, C.ROLLING_START_PHASE1_MIN_GAP, C.ROLLING_START_PHASE1_FULL_GAP,
            C.ROLLING_START_PHASE1_CATCHUP)
  end
  return formationSpeed, 0.0, 1.0, 1.0
end

local function DoubleFileTargets(carIndex, state, group, trackLength)
  local cautionLevel = 0.7
  local targetOffset = (state.startSide or 1) * C.ROLLING_START_LANE_METERS + RGroups.GetOrganicLaneOffset(carIndex)
  local targetSpeed, throttleLimit = Spacing.ApplyRollingPhaseTwoSpacing(state, group, trackLength)
  return targetSpeed, targetOffset, cautionLevel, throttleLimit
end

-- 0..1 progress through the post-release launch ramp (1 once finished).
local function LaunchProgress(state)
  if not state.releasedAt or state.skipLaunchRamp then return 1.0 end
  local t = (S.rollingStartSessionTime - state.releasedAt) / C.ROLLING_START_LAUNCH_RAMP_SECONDS
  if t < 0 then return 0.0 elseif t > 1 then return 1.0 end
  return t * t * (3 - 2 * t)
end

local function CommandFormationAI(carIndex, state, group, car, trackLength, maxLaneMove)
  local targetSpeed, targetOffset, cautionLevel, throttleLimit
  if state.enteredPhase2 then
    targetSpeed, targetOffset, cautionLevel, throttleLimit = DoubleFileTargets(carIndex, state, group, trackLength)
  else
    targetSpeed, targetOffset, cautionLevel, throttleLimit = SingleFileTargets(state, group, car, trackLength)
  end

  local launch = LaunchProgress(state)
  if launch < 1.0 then
    local startThrottle = C.ROLLING_START_LAUNCH_THROTTLE_START
    throttleLimit = math.min(throttleLimit, startThrottle + (1.0 - startThrottle) * launch)
    local startSpeed = C.ROLLING_START_LAUNCH_SPEED_START_KMH
    targetSpeed = math.min(targetSpeed, startSpeed + (targetSpeed - startSpeed) * launch)
    maxLaneMove = maxLaneMove * (C.ROLLING_START_LAUNCH_LANE_SCALE + (1.0 - C.ROLLING_START_LAUNCH_LANE_SCALE) * launch)
  end

  local applied = state.currentSplineOffset or 0.0
  local diff = targetOffset - applied
  if math.abs(diff) > maxLaneMove then
    applied = applied + (diff > 0 and maxLaneMove or -maxLaneMove)
  else
    applied = targetOffset
  end
  state.currentSplineOffset = applied

  setAITopSpeed(carIndex, targetSpeed)
  setAICaution(carIndex, cautionLevel)
  setAIThrottleLimit(carIndex, throttleLimit)
  setAISplineAbsoluteOffset(carIndex, applied, false)
  physics.setAIStopCounter(carIndex, 0.0)
end

local function Field(car, name)
  local ok, v = pcall(function() return car[name] end)
  if not ok or v == nil then return "n/a" end
  if type(v) == "number" then return sFormat("%.2f", v) end
  return tostring(v)
end

-- One-shot snapshot of a stalled car so the log shows why it is not moving.
local function LogStalledCarState(carIndex, car, inPit)
  U.Log(sFormat("ROLLING: car %d stalled diag v=%.1f pit=%s gas=%s brake=%s clutch=%s gear=%s rpm=%s " ..
      "handbrake=%s engineStalled=%s collided=%s damage=%s",
      carIndex, car.speedKmh or 0, tostring(inPit), Field(car, "gas"), Field(car, "brake"),
      Field(car, "clutch"), Field(car, "gear"), Field(car, "rpm"), Field(car, "handbrake"),
      Field(car, "isEngineStalled"), Field(car, "collidedWith"), Field(car, "engineLifeLeft")))
  local nearest, nearestDist = nil, math.huge
  local pos = car.position
  if pos then
    for other = 0, S.driverCount - 1 do
      local oc = other ~= carIndex and getCar(other) or nil
      if oc and oc.position then
        local d = pos:distance(oc.position)
        if d < nearestDist then nearest, nearestDist = other, d end
      end
    end
  end
  if nearest then
    U.Log(sFormat("ROLLING: car %d nearest car %d at %.2f m", carIndex, nearest, nearestDist))
  end
end

local function AwakeCar(carIndex)
  if physics.awakeCar then pcall(physics.awakeCar, carIndex) end
end

local function NudgeCarForward(carIndex, car, speedMps)
  AwakeCar(carIndex)
  local look = car.look
  if look and physics.setCarVelocity then
    pcall(physics.setCarVelocity, carIndex, vec3(look.x * speedMps, 0, look.z * speedMps))
  end
end

-- Last resort: move the car a little further along its own heading and give it forward speed.
local function TeleportCarForward(carIndex, car, meters)
  local look, pos = car.look, car.position
  if not (look and pos and physics.setCarPosition) then return end
  local target = vec3(pos.x + look.x * meters, pos.y, pos.z + look.z * meters)
  pcall(physics.setCarPosition, carIndex, target, look)
  NudgeCarForward(carIndex, car, 8.0)
  U.Log(sFormat("ROLLING: car %d recovery stage 3: moved %.0f m forward", carIndex, meters))
end

-- A released AI car that sits still (or ends up in the pit lane) must not hold the rest of its
-- class up: nudge it first, then drop it from the spacing chains until it moves again.
local function WatchForStalledCar(carIndex, state, car, dt)
  if carIndex == 0 or not car then return end
  local inPit = car.isInPitlane == true or car.isInPit == true
  local launched = state.releasedAt and (S.rollingStartSessionTime - state.releasedAt) > 2.5
  local still = launched and (car.speedKmh or 0) < 3.0 and not state.enteredPhase2
  if still or inPit then
    state.stillTime = (state.stillTime or 0.0) + dt
  else
    state.stillTime = 0.0
    state.recoveryTried = false
    state.recoveryStage = 0
    if state.dropped and (car.speedKmh or 0) > 10.0 then
      state.dropped = false
      U.Log(sFormat("ROLLING: car %d moving again; back in the formation", carIndex))
    end
    return
  end

  local stage = state.recoveryStage or 0
  if state.stillTime > 0.5 and stage < 1 then
    state.recoveryStage = 1
    state.recoveryTried = true
    state.skipLaunchRamp = true
    LogStalledCarState(carIndex, car, inPit)
    RCtl.RestoreAIControl(carIndex, false)
    AwakeCar(carIndex)
  elseif state.stillTime > 2.5 and stage < 2 then
    state.recoveryStage = 2
    NudgeCarForward(carIndex, car, 5.0)
    U.Log(sFormat("ROLLING: car %d recovery stage 2: forward velocity nudge", carIndex))
  elseif state.stillTime > 7.5 and stage < 3 then
    state.recoveryStage = 3
    TeleportCarForward(carIndex, car, 12.0)
  end
  if (state.stillTime > 17.5 or inPit) and not state.dropped then
    state.dropped = true
    U.Log(sFormat("ROLLING: car %d dropped from formation spacing (stalled/pit)", carIndex))
  end
end

local function MaintainCar(carIndex, state, trackLength, moves)
  local car = getCar(carIndex)
  local isAI = (carIndex ~= 0)
  local group = S.rollingStartGroups[state.groupIndex]

  if not state.released and RGroups.IsRollingCarReleased(state) then
    RCtl.ReleaseRollingCar(carIndex)
  end

  if not state.released then
    if isAI and car and (car.isInPitlane == true or car.isInPit == true) and not state.pitLogged then
      state.pitLogged = true
      U.Log(sFormat("ROLLING: car %d ended up in the pit lane while still held (t=%.1fs)", carIndex,
          S.rollingStartSessionTime))
    end
    if isAI then HoldUnreleasedAI(carIndex) end
    return
  end

  TrackLineCrossing(state, car, group)
  if not state.green then WatchForStalledCar(carIndex, state, car, moves.dt) end
  if state.green then
    EaseOutLaneOffset(carIndex, state, isAI, moves.greenLaneMove)
  elseif isAI then
    CommandFormationAI(carIndex, state, group, car, trackLength, moves.maxLaneMove)
  end
end

-- Per-class green transition: ramp caution from 0 to 1 over the reference window.
local function RampClassCaution(dt)
  for _, group in ipairs(S.rollingStartGroups or {}) do
    if group.green and (group.windowCloseTimer or 0) <= C.ROLLING_START_GREEN_WINDOW_SECONDS + 0.1 then
      group.windowCloseTimer = (group.windowCloseTimer or 0) + dt
      local progress = math.min(1.0, group.windowCloseTimer / C.ROLLING_START_GREEN_WINDOW_SECONDS)
      for _, memberIndex in ipairs(group.cars) do
        if memberIndex ~= 0 then
          setAICaution(memberIndex, progress)
        end
      end
    end
  end
end

local function FindOppositeParityPair()
  local oddState, evenState
  for _, carIndex in ipairs(S.rollingStartQueue) do
    local state = S.rollingStartCars[carIndex]
    if state and state.enteredPhase2 and state.gridPosition then
      if state.gridPosition % 2 == 1 and not oddState then
        oddState = state
      elseif state.gridPosition % 2 == 0 and not evenState then
        evenState = state
      end
    end
  end
  return oddState, evenState
end

local function LateralVsPlayer(state, player, psx, psy, psz)
  local car = getCar(state.carIndex)
  local pp = car and car.position
  if not pp then return 0 end
  local dx = pp.x - player.position.x
  local dy = pp.y - player.position.y
  local dz = pp.z - player.position.z
  return dx * psx + dy * psy + dz * psz
end

-- One-shot probe: confirm which world side a commanded offset actually places a car on. Once two
-- phase-2 cars of opposite grid parity have settled lanes, log their offsets next to their real
-- lateral position relative to the player (positive = player's left side).
local function ProbeLaneSide()
  if S.rollingStartLaneDiagDone then return end
  local player = getCar(0)
  local psx, psy, psz = PoleSide.GetCarLeftAxis(player)
  if not (player and psx) then return end

  local oddState, evenState = FindOppositeParityPair()
  if oddState and evenState
      and math.abs(oddState.currentSplineOffset or 0) > 1.0
      and math.abs(evenState.currentSplineOffset or 0) > 1.0 then
    U.Log(sFormat("ROLLING: lane diag oddGrid=%d oddOff=%.2f oddLat=%.2f | evenGrid=%d evenOff=%.2f evenLat=%.2f",
        oddState.gridPosition, oddState.currentSplineOffset or 0,
        LateralVsPlayer(oddState, player, psx, psy, psz),
        evenState.gridPosition, evenState.currentSplineOffset or 0,
        LateralVsPlayer(evenState, player, psx, psy, psz)))
    S.rollingStartLaneDiagDone = true
  end
end

-- Periodic diagnostic so the formation can be verified from the log.
local function LogFormationDiagnostics()
  S.rollingStartDiagFrame = (S.rollingStartDiagFrame or 0) + 1
  if S.rollingStartDiagFrame % 30 ~= 0 then return end
  for _, carIndex in ipairs(S.rollingStartQueue) do
    local state = S.rollingStartCars[carIndex]
    local car = getCar(carIndex)
    if state and car then
      local group = S.rollingStartGroups and S.rollingStartGroups[state.groupIndex] or nil
      U.Log(sFormat("ROLLING: car %d cls=%s cp=%d p=%.3f v=%.1f phase=%d off=%.2f rel=%s green=%s wrap=%d trav=%.2f",
          carIndex, (group and group.label) or "?", state.classPosition or -1,
          U.NormalizeTrackProgress(car.splinePosition), car.speedKmh or -1,
          state.enteredPhase2 and 2 or 1, state.currentSplineOffset or 0,
          tostring(state.released), tostring(state.green), state.wrapCount or 0,
          state.travelled or 0.0))
    end
  end
end

function M.MaintainRollingFormation(dt, trackLength)
  AdvanceReleaseClock(dt)

  local moves = {
    dt = dt,
    maxLaneMove = C.ROLLING_START_LANE_RAMP_MPS * dt,
    greenLaneMove = C.ROLLING_START_GREEN_OFFSET_RAMP_MPS * dt,
  }

  BuildPositionMaps()

  for _, carIndex in ipairs(S.rollingStartQueue) do
    local state = S.rollingStartCars[carIndex]
    if state then MaintainCar(carIndex, state, trackLength, moves) end
  end

  RampClassCaution(dt)
  ProbeLaneSide()
  LogFormationDiagnostics()
end

return M
