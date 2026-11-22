-- Rolling start: per-frame formation maintenance (single file, double file, lane offsets, diagnostics).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local RCtl = require("src/rolling/control")
local PoleSide = require("src/rolling/pole_side")
local RGroups = require("src/rolling/groups")
local Spacing = require("src/rolling/spacing")
local Centre = require("src/rolling/centre")
local TrackShape = require("src/rolling/track_shape")
local Audio = require("src/audio")

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

local function LaneKeyGreater(a, b)
  return S.rollingStartCars[a].laneKey > S.rollingStartCars[b].laneKey
end

local function LinkLane(lane)
  table.sort(lane, LaneKeyGreater)
  for i = 2, #lane do
    S.rollingStartCars[lane[i]].laneAheadIndex = lane[i - 1]
  end
end

-- Running order of each class's double-file lanes, by actual track position rather than grid slot,
-- so the lane guard always knows the car physically ahead in the same lane.
local function BuildLaneOrder()
  for _, group in ipairs(S.rollingStartGroups or {}) do
    local left, right = group.laneLeft or {}, group.laneRight or {}
    group.laneLeft, group.laneRight = left, right
    for i = #left, 1, -1 do left[i] = nil end
    for i = #right, 1, -1 do right[i] = nil end
    local ref
    for _, carIndex in ipairs(group.cars) do
      local state = S.rollingStartCars[carIndex]
      if state then
        state.laneAheadIndex = nil
        local car = state.enteredPhase2 and not state.green and not state.dropped and getCar(carIndex)
        if car and type(car.splinePosition) == "number" then
          ref = ref or car.splinePosition
          local d = car.splinePosition - ref
          if d > 0.5 then d = d - 1.0 elseif d < -0.5 then d = d + 1.0 end
          state.laneKey = d
          local lane = (state.startSide or 1) < 0 and left or right
          lane[#lane + 1] = carIndex
        end
      end
    end
    LinkLane(left)
    LinkLane(right)
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
  if state.groupIndex == 1 then Audio.PlayRolling() end
  if state.carIndex == 0 then Audio.PlayDoubleFile() end
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

-- Spline position where the double file starts: a percentage of the lap, except on long tracks
-- where the double file is capped to a fixed length before the line.
local function FormationPoint(trackLength)
  local base = TrackShape.BasePoint(trackLength)
  -- Moved past a hairpin that would otherwise be taken two abreast (see rolling/track_shape.lua).
  return TrackShape.DoubleFileStart(base, trackLength)
end

-- Track the start-line crossing so a car only double-files on its flying lap.
local function TrackLineCrossing(state, car, group, trackLength)
  if not (car and type(car.splinePosition) == "number") then return end
  if not state.hasRolledOver and car.splinePosition < C.ROLLING_START_ROLLED_OVER_SPLINE then
    state.hasRolledOver = true
  end

  if state.hasRolledOver and car.splinePosition >= FormationPoint(trackLength)
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
local function FieldStillLaunching()
  for _, carIndex in ipairs(S.rollingStartQueue) do
    local other = S.rollingStartCars[carIndex]
    if other and not other.released and not other.dropped then return true end
  end
  return false
end

-- Lateral offset (m) that makes a single-file car swing gently left/right like a driver warming
-- the tyres: straight for 10 s, weave for 5 s, repeat. The sine is faded in and out by a half-sine
-- envelope so the weave starts and ends smoothly. Cars are staggered by index so they do not all
-- weave in unison.
local function TyreWarmWeaveOffset(carIndex)
  local straight = C.ROLLING_START_WEAVE_STRAIGHT_SECONDS
  local weave = C.ROLLING_START_WEAVE_SECONDS
  local t = ((S.rollingStartSessionTime or 0) + carIndex * 3.7) % (straight + weave)
  if t < straight then return 0.0 end
  local w = t - straight
  local envelope = math.sin(math.pi * w / weave)
  return C.ROLLING_START_WEAVE_AMPLITUDE_M * envelope * math.sin(2 * math.pi * w / C.ROLLING_START_WEAVE_PERIOD_SECONDS)
end

local function Lerp(a, b, t) return a + (b - a) * t end

-- 0..1: how far the class has got into its compaction run before the double-file point, measured
-- by the class front. The run is COMPACT_PER_CAR_M per class car long (clamped), so a big class
-- starts closing up earlier. 1 once the class front is at or past the point.
local function ClassCompaction(group, trackLength)
  if not (C.ROLLING_START_COMPACT_ENABLED and group and group.cars and U.IsFinite(trackLength) and trackLength > 0) then
    return 0.0
  end
  local frontState, frontIndex, count
  count = 0
  for _, memberIndex in ipairs(group.cars) do
    local member = S.rollingStartCars[memberIndex]
    if member and not member.dropped then
      count = count + 1
      if not frontState then frontState, frontIndex = member, memberIndex end
    end
  end
  if not frontState then return 0.0 end
  if frontState.enteredPhase2 then return 1.0 end
  if not frontState.hasRolledOver then return 0.0 end
  local frontCar = getCar(frontIndex)
  if not (frontCar and type(frontCar.splinePosition) == "number") then return 0.0 end
  local toPoint = FormationPoint(trackLength) - frontCar.splinePosition
  if toPoint <= 0 then return 1.0 end
  local zone = math.max(C.ROLLING_START_COMPACT_MIN_M,
      math.min(C.ROLLING_START_COMPACT_MAX_M, count * C.ROLLING_START_COMPACT_PER_CAR_M))
  local distM = toPoint * trackLength
  if distM >= zone then return 0.0 end
  return 1.0 - distM / zone
end

local function SingleFileTargets(carIndex, state, group, car, trackLength)
  local formationSpeed = C.ROLLING_START_FORMATION_SPEED_KMH
  -- Approaching the double-file point the class closes up: shorter follow gap, earlier and
  -- stronger catch-up, and the cars ahead wait for a car behind sooner.
  local k = ClassCompaction(group, trackLength)
  local minGap = Lerp(C.ROLLING_START_PHASE1_MIN_GAP, C.ROLLING_START_COMPACT_GAP_M, k)
  local fullGap = Lerp(C.ROLLING_START_PHASE1_FULL_GAP, C.ROLLING_START_COMPACT_FULL_GAP_M, k)
  local catchup = Lerp(C.ROLLING_START_PHASE1_CATCHUP, C.ROLLING_START_COMPACT_CATCHUP, k)
  local behindStart = Lerp(C.ROLLING_START_PHASE1_BEHIND_START_GAP, C.ROLLING_START_COMPACT_BEHIND_START_GAP, k)
  local behindFull = Lerp(C.ROLLING_START_PHASE1_BEHIND_FULL_GAP, C.ROLLING_START_COMPACT_BEHIND_FULL_GAP, k)
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
  local throttleLimit = 1.0
  -- The class leader follows the safety car like any other car ahead, so it never closes on it.
  local leaderHasNoCarAhead = not aheadCar
  local followingSafetyCar = false
  if not aheadCar and car and S.safetyCarIndex then
    local sc = getCar(S.safetyCarIndex)
    if sc and sc.isInPitlane ~= true and sc.isInPit ~= true and type(sc.splinePosition) == "number" then
      local scGap = Spacing.SignedGap(sc, car, trackLength)
      if scGap > -20 and scGap < 400 then
        aheadCar = sc
        followingSafetyCar = true
      end
    end
  end
  -- A leader with nobody ahead cruises at the rolling speed while the rest of the field is still
  -- being released, so it does not run off at race pace and leave the field far behind.
  if leaderHasNoCarAhead and FieldStillLaunching() then
    formationSpeed = C.ROLLING_START_SPEED_KMH
  end
  if aheadCar and car then
    local gap = Spacing.SignedGap(aheadCar, car, trackLength)
    if gap < minGap then
      -- Too close to the car ahead: drop below its speed in proportion to the shortfall so the
      -- gap opens back up. A follower that is alongside or already past backs off hard and hands
      -- the place back, so the class keeps its grid order into the double-file zone.
      local aheadSpeed = aheadCar.speedKmh or 0
      local backOff = math.min(C.ROLLING_START_PHASE1_YIELD_KMH,
          (minGap - gap) * C.ROLLING_START_PHASE1_GAP_GAIN_KMH)
      formationSpeed = math.min(formationSpeed, math.max(0.0, aheadSpeed - backOff))
      if gap < 0 then
        formationSpeed = math.max(formationSpeed, C.ROLLING_START_PHASE1_YIELD_FLOOR_KMH)
        throttleLimit = 0.5
      end
    else
      formationSpeed = C.ROLLING_START_FORMATION_SPEED_KMH *
          Spacing.GetLinearMultiplier(gap, minGap, fullGap, catchup)
    end
    if followingSafetyCar then
      -- Never close on the safety car faster than it can be matched at the follow gap: the cap is
      -- the safety car's own speed plus what braking at FOLLOW_DECEL can shed over the spare gap.
      local scSpeed = math.max(0.0, aheadCar.speedKmh or 0)
      local room = gap - C.SAFETY_CAR_FOLLOW_GAP_M
      local cap
      if room > 0 then
        local v = scSpeed / 3.6
        cap = math.sqrt(v * v + 2.0 * C.SAFETY_CAR_FOLLOW_DECEL * room) * 3.6
      else
        cap = math.max(0.0, scSpeed + room * C.ROLLING_START_PHASE1_GAP_GAIN_KMH)
        throttleLimit = math.min(throttleLimit, 0.5)
      end
      formationSpeed = math.min(formationSpeed, cap)
    end
  end
  -- The catch-up branch above replaces the speed; keep the leader at the rolling speed while the
  -- rest of the field is still being released.
  if leaderHasNoCarAhead and FieldStillLaunching() then
    formationSpeed = math.min(formationSpeed, C.ROLLING_START_SPEED_KMH)
  end
  -- Wait for the car behind: nearest car further back in the class that is released, driving and
  -- still in single file.
  if group and group.cars and car then
    for pos = (state.classPosition or 1) + 1, #group.cars do
      local behindIndex = group.cars[pos]
      local behindState = behindIndex and S.rollingStartCars[behindIndex]
      if behindState and behindState.released and not behindState.dropped and not behindState.enteredPhase2 then
        local behindCar = getCar(behindIndex)
        if behindCar and type(behindCar.splinePosition) == "number" and (behindCar.speedKmh or 0) > 5.0 then
          local behind = -Spacing.SignedGap(behindCar, car, trackLength)
          if behind > behindStart then
            local t = math.min(1.0, (behind - behindStart) / math.max(1.0, behindFull - behindStart))
            local waitSpeed = formationSpeed + (C.ROLLING_START_PHASE1_BEHIND_MIN_SPEED - formationSpeed) * t
            formationSpeed = math.min(formationSpeed, waitSpeed)
          end
          break
        end
      end
    end
  end
  -- Weave only once the car is actually rolling at speed.
  local offset = 0.0
  if car and (car.speedKmh or 0) > 30.0 then offset = TyreWarmWeaveOffset(carIndex) end
  return formationSpeed, offset, 1.0, throttleLimit
end

-- Never close on whatever is physically ahead in this car's own lane (see LANE_GUARD constants).
local function ApplyLaneGuard(state, car, trackLength, targetSpeed, throttleLimit)
  local aheadIndex = state.laneAheadIndex
  local aheadCar = aheadIndex and getCar(aheadIndex)
  if not (aheadCar and car) then return targetSpeed, throttleLimit end
  local gap = Spacing.SignedGap(aheadCar, car, trackLength)
  if gap >= C.ROLLING_START_LANE_GUARD_GAP then
    -- Braking-distance cap: arrive at HOLD_GAP no faster than the car ahead is going.
    if gap < C.ROLLING_START_LANE_GUARD_BRAKE_RANGE_M then
      local v = math.max(0.0, aheadCar.speedKmh or 0) / 3.6
      local room = gap - C.ROLLING_START_LANE_GUARD_HOLD_GAP
      local cap = math.sqrt(v * v + 2.0 * C.ROLLING_START_LANE_GUARD_BRAKE_DECEL * room) * 3.6
      if cap < targetSpeed then
        targetSpeed = cap
        -- The AI only eases off gently when its top speed drops; lift fully while well above the cap.
        if (car.speedKmh or 0) > cap + C.ROLLING_START_WAIT_LIFT_MARGIN_KMH then throttleLimit = 0.0 end
      end
    end
    return targetSpeed, throttleLimit
  end
  local cap = (aheadCar.speedKmh or 0) +
      (gap - C.ROLLING_START_LANE_GUARD_HOLD_GAP) * C.ROLLING_START_LANE_GUARD_GAIN_KMH
  if cap < targetSpeed then
    targetSpeed = math.max(0.0, cap)
    if gap < C.ROLLING_START_LANE_GUARD_HOLD_GAP then throttleLimit = math.min(throttleLimit, 0.5) end
  end
  return targetSpeed, throttleLimit
end

-- Keep a gap to the nearest car of the previous class that is physically ahead (see CLASS_GUARD constants).
local function ApplyClassGuard(state, car, trackLength, targetSpeed, throttleLimit)
  local prev = S.rollingStartGroups[(state.groupIndex or 1) - 1]
  if not (prev and car) then return targetSpeed, throttleLimit end
  local nearestGap, nearestSpeed
  for _, otherIndex in ipairs(prev.cars) do
    local other = S.rollingStartCars[otherIndex]
    if other and other.released and not other.dropped then
      local otherCar = getCar(otherIndex)
      if otherCar and type(otherCar.splinePosition) == "number" then
        local g = Spacing.SignedGap(otherCar, car, trackLength)
        if g > -4.0 and (not nearestGap or g < nearestGap) then
          nearestGap, nearestSpeed = g, otherCar.speedKmh or 0
        end
      end
    end
  end
  if not nearestGap or nearestGap >= C.ROLLING_START_CLASS_GUARD_GAP then return targetSpeed, throttleLimit end
  local cap = nearestSpeed + (nearestGap - C.ROLLING_START_CLASS_HOLD_GAP) * C.ROLLING_START_CLASS_GUARD_GAIN_KMH
  if cap < targetSpeed then
    targetSpeed = math.max(0.0, cap)
    if nearestGap < C.ROLLING_START_CLASS_HOLD_GAP then throttleLimit = math.min(throttleLimit, 0.5) end
  end
  return targetSpeed, throttleLimit
end

-- Double-file cars wait for the rest of their class: slow down in proportion to how far behind the
-- farthest released car is that has not yet entered double file (see WAIT constants).
local function WaitForStragglers(group, car, trackLength, targetSpeed, throttleLimit)
  if not (group and group.cars and car) then return targetSpeed, throttleLimit end
  -- How far each car is behind where its slot would be: single-file cars count in full, a car
  -- already in double file only by how far it trails its row (ROW_M per row). A big class enters
  -- the double file strung out, so its rear rows are still far from their slot after the point.
  local farthest = 0.0
  for _, memberIndex in ipairs(group.cars) do
    local member = S.rollingStartCars[memberIndex]
    if member and member.released and not member.dropped and not member.green then
      local other = getCar(memberIndex)
      if other and type(other.splinePosition) == "number" then
        local behind = -Spacing.SignedGap(other, car, trackLength)
        if behind > 0 and behind <= C.ROLLING_START_WAIT_MAX_RANGE_M then
          if member.enteredPhase2 then
            local slot = member.lockedPhase2Position or member.classPosition or 1
            behind = behind - (math.ceil(slot / 2) - 1) * C.ROLLING_START_ROW_M
          end
          if behind > farthest then farthest = behind end
        end
      end
    end
  end
  if farthest <= C.ROLLING_START_WAIT_START_GAP_M then return targetSpeed, throttleLimit end
  local t = math.min(1.0, (farthest - C.ROLLING_START_WAIT_START_GAP_M) /
      (C.ROLLING_START_WAIT_FULL_GAP_M - C.ROLLING_START_WAIT_START_GAP_M))
  local waitSpeed = targetSpeed + (C.ROLLING_START_WAIT_MIN_SPEED_KMH - targetSpeed) * t
  if waitSpeed < targetSpeed then
    targetSpeed = math.max(0.0, waitSpeed)
    throttleLimit = math.min(throttleLimit, 1.0 - 0.5 * t)
    -- The AI only eases off gently when its top speed drops (about 2 m/s^2), which on a 300 m
    -- double-file zone means the front row never really slows. Lift completely while well above
    -- the wait speed so it gets there quickly.
    if (car.speedKmh or 0) > targetSpeed + C.ROLLING_START_WAIT_LIFT_MARGIN_KMH then throttleLimit = 0.0 end
  end
  return targetSpeed, throttleLimit
end

-- True for the two cars at the front of the class (its first row). Cars that are dropped or retired
-- do not count, so the front row is always the first two cars actually driving.
local function IsFrontRow(carIndex, group)
  if not (group and group.cars) then return true end
  local ahead = 0
  for _, memberIndex in ipairs(group.cars) do
    if memberIndex == carIndex then return ahead < 2 end
    local member = S.rollingStartCars[memberIndex]
    if member and not member.dropped then ahead = ahead + 1 end
  end
  return true
end

-- Class gap in double file: the front row keeps DF_CLASS_GAP_M behind the previous class's last car,
-- so every class gets the same clear gap before its own green. Without it the only spacing was the
-- anti-collision class guard (30 m), so a class whose front row did not have to wait for its own
-- tail ran right up to the class ahead while the next class sat 140 m back.
local function KeepClassGap(state, car, trackLength, targetSpeed, throttleLimit)
  local prev = S.rollingStartGroups[(state.groupIndex or 1) - 1]
  if not (prev and prev.cars and car) or prev.green then return targetSpeed, throttleLimit end
  local nearestGap, nearestSpeed
  for _, otherIndex in ipairs(prev.cars) do
    local other = S.rollingStartCars[otherIndex]
    if other and other.released and not other.dropped then
      local otherCar = getCar(otherIndex)
      if otherCar and type(otherCar.splinePosition) == "number" then
        local g = Spacing.SignedGap(otherCar, car, trackLength)
        if g > -4.0 and (not nearestGap or g < nearestGap) then
          nearestGap, nearestSpeed = g, otherCar.speedKmh or 0
        end
      end
    end
  end
  if not nearestGap or nearestGap >= C.ROLLING_START_DF_CLASS_GAP_M + C.ROLLING_START_DF_CLASS_GAP_BAND_M then
    return targetSpeed, throttleLimit
  end
  local cap = nearestSpeed + (nearestGap - C.ROLLING_START_DF_CLASS_GAP_M) * C.ROLLING_START_DF_CLASS_GAP_GAIN_KMH
  cap = math.max(C.ROLLING_START_DF_CLASS_GAP_MIN_KMH, cap)
  if cap < targetSpeed then
    targetSpeed = cap
    if (car.speedKmh or 0) > cap + C.ROLLING_START_WAIT_LIFT_MARGIN_KMH then throttleLimit = math.min(throttleLimit, 0.0) end
  end
  return targetSpeed, throttleLimit
end

local function DoubleFileTargets(carIndex, state, group, car, trackLength)
  local cautionLevel = 0.7
  local targetOffset = Centre.LaneOffset(state, car, trackLength) + RGroups.GetOrganicLaneOffset(carIndex)
  local targetSpeed, throttleLimit = Spacing.ApplyRollingPhaseTwoSpacing(state, group, trackLength)
  targetSpeed, throttleLimit = ApplyLaneGuard(state, car, trackLength, targetSpeed, throttleLimit)
  -- Only the front row waits for the stragglers. Every other car follows the car ahead through the
  -- spacing above; slowing them by their own distance to the stragglers made the cars at the back
  -- brake less than the ones in front of them and run into them.
  if IsFrontRow(carIndex, group) then
    targetSpeed, throttleLimit = WaitForStragglers(group, car, trackLength, targetSpeed, throttleLimit)
    targetSpeed, throttleLimit = KeepClassGap(state, car, trackLength, targetSpeed, throttleLimit)
  end
  return targetSpeed, targetOffset, cautionLevel, throttleLimit
end

-- 0..1 progress through the post-release launch ramp (1 once finished).
local function LaunchProgress(state)
  if not state.releasedAt or state.skipLaunchRamp then return 1.0 end
  local t = (S.rollingStartSessionTime - state.releasedAt) / C.ROLLING_START_LAUNCH_RAMP_SECONDS
  if t < 0 then return 0.0 elseif t > 1 then return 1.0 end
  return t * t * (3 - 2 * t)
end

-- Hard no-overtake guard for the human car while the autopilot drives it. The soft spacing above
-- lets a car that has drawn alongside keep a floor speed (so a stalled car cannot hold the class
-- up), which the autopilot car turns into a real pass. Here the car directly in front of it (the
-- class car ahead in single file, the same-lane car ahead in double file) is never passed: inside
-- the hard gap it brakes below that car's speed, and once level or ahead it stops until re-passed.
local HARD_GAP_M = 8.0
local FOLLOW_RANGE_M = 70.0
-- Braking the closing profile plans with. 2.5 m/s^2 started braking ~30 m out, so the human car
-- could never close a 60 m gap to its slot on a short double-file zone (Long Beach).
local FOLLOW_DECEL = 4.5
local function ApplyNoOvertakeGuard(state, group, car, trackLength, targetSpeed, throttleLimit)
  local aheadIndex
  if state.enteredPhase2 then
    aheadIndex = state.laneAheadIndex
  elseif group and group.cars then
    for pos = (state.classPosition or 1) - 1, 1, -1 do
      local idx = group.cars[pos]
      local st = idx and S.rollingStartCars[idx]
      if idx and not (st and (st.dropped or st.retired)) then aheadIndex = idx break end
    end
  end
  local aheadCar = aheadIndex and getCar(aheadIndex)
  if car and aheadCar and type(aheadCar.splinePosition) == "number" then
    local gap = Spacing.SignedGap(aheadCar, car, trackLength)
    local aheadSpeed = aheadCar.speedKmh or 0
    if gap <= HARD_GAP_M and gap >= -40.0 then
      local cap = gap <= 0 and 0.0 or math.max(0.0, aheadSpeed - (HARD_GAP_M - gap) * 4.0)
      return math.min(targetSpeed, cap), 0.0
    elseif gap > HARD_GAP_M and gap <= FOLLOW_RANGE_M then
      -- Brake early enough to arrive at the hold gap at the other car's speed. A closing car makes
      -- the AI steer out to pass, so the closing must stop well before the hard gap.
      local v = aheadSpeed / 3.6
      local cap = math.sqrt(v * v + 2.0 * FOLLOW_DECEL * (gap - HARD_GAP_M)) * 3.6
      if cap < targetSpeed then targetSpeed = cap end
    end
  end
  -- Whatever is physically in front in the same lane (any class, any phase) is never closed on fast.
  local dist, speedAhead = Spacing.NearestAhead(0, car, S.carsTotal or S.driverCount)
  if dist and dist < HARD_GAP_M + 4.0 then
    targetSpeed = math.min(targetSpeed, math.max(0.0, (speedAhead or 0) - 5.0))
    throttleLimit = math.min(throttleLimit, 0.3)
  end
  return targetSpeed, throttleLimit
end

-- True while the car is still within the straight run after its release. The first call after the
-- release records where it started.
local function IsInStraightRun(state, car, trackLength)
  if state.straightDone then return false end
  if not (car and type(car.splinePosition) == "number" and U.IsFinite(trackLength) and trackLength > 0) then
    return false
  end
  if not state.straightOffset then
    -- A car already in double file holds the lane it is in (track centre), not the racing-line lane.
    state.straightOffset = state.enteredPhase2 and state.currentSplineOffset
      or (state.startSide or 1) * C.ROLLING_START_LANE_METERS
    state.straightStartSpline = car.splinePosition
    state.straightStartTime = S.rollingStartSessionTime or 0
  end
  local d = car.splinePosition - state.straightStartSpline
  if d > 0.5 then d = d - 1.0 elseif d < -0.5 then d = d + 1.0 end
  local travelled = math.max(0.0, d * trackLength)
  local elapsed = (S.rollingStartSessionTime or 0) - state.straightStartTime
  if travelled >= C.ROLLING_START_STRAIGHT_RUN_M or elapsed >= C.ROLLING_START_STRAIGHT_RUN_MAX_SECONDS then
    state.straightDone = true
    return false
  end
  return true
end

local function CommandFormationAI(carIndex, state, group, car, trackLength, maxLaneMove)
  local targetSpeed, targetOffset, cautionLevel, throttleLimit
  if state.enteredPhase2 then
    targetSpeed, targetOffset, cautionLevel, throttleLimit = DoubleFileTargets(carIndex, state, group, car, trackLength)
  else
    targetSpeed, targetOffset, cautionLevel, throttleLimit = SingleFileTargets(carIndex, state, group, car, trackLength)
  end

  targetSpeed, throttleLimit = ApplyClassGuard(state, car, trackLength, targetSpeed, throttleLimit)
  -- Final safety net, applied to every car: never run into whatever is physically ahead.
  targetSpeed, throttleLimit = Spacing.ProximityGuard(carIndex, car, S.carsTotal or S.driverCount, targetSpeed, throttleLimit)

  if carIndex == 0 and RCtl.DevAutopilotActive() then
    targetSpeed, throttleLimit = ApplyNoOvertakeGuard(state, group, car, trackLength, targetSpeed, throttleLimit)
  end

  local launch = LaunchProgress(state)
  if launch < 1.0 then
    local startThrottle = C.ROLLING_START_LAUNCH_THROTTLE_START
    throttleLimit = math.min(throttleLimit, startThrottle + (1.0 - startThrottle) * launch)
    local startSpeed = C.ROLLING_START_LAUNCH_SPEED_START_KMH
    targetSpeed = math.min(targetSpeed, startSpeed + (targetSpeed - startSpeed) * launch)
    maxLaneMove = maxLaneMove * (C.ROLLING_START_LAUNCH_LANE_SCALE + (1.0 - C.ROLLING_START_LAUNCH_LANE_SCALE) * launch)
  end

  if not state.enteredPhase2 then maxLaneMove = maxLaneMove * C.ROLLING_START_WEAVE_LANE_RATE_SCALE end

  local applied = state.currentSplineOffset or 0.0
  -- Straight run: right after release keep the lane the car physically sits in (its grid side)
  -- so it does not steer toward the racing line while it is still launching.
  if IsInStraightRun(state, car, trackLength) then
    targetOffset = state.straightOffset
    applied = state.straightOffset
  end
  local diff = targetOffset - applied
  if math.abs(diff) > maxLaneMove then
    applied = applied + (diff > 0 and maxLaneMove or -maxLaneMove)
  else
    applied = targetOffset
  end
  state.currentSplineOffset = applied

  state.lastTargetSpeed, state.lastThrottleLimit = targetSpeed, throttleLimit
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
  -- The human car is only watched while the autopilot drives it; it gets the same kick-start as
  -- the AI cars but is never dropped from the spacing.
  local isPlayer = carIndex == 0
  if not car or (isPlayer and S.playerAutopilotOn ~= true) then return end
  local inPit = not isPlayer and (car.isInPitlane == true or car.isInPit == true)
  local launched = state.releasedAt and
      (S.rollingStartSessionTime - state.releasedAt) > C.ROLLING_START_STALL_GRACE_SECONDS
  local still = launched and (car.speedKmh or 0) < C.ROLLING_START_STALL_SPEED_KMH and not state.enteredPhase2
  if still or inPit then
    state.stillTime = (state.stillTime or 0.0) + dt
  else
    state.stillTime = 0.0
    state.lastNudgeStill = 0.0
    state.recoveryTried = false
    state.recoveryStage = 0
    if state.dropped and (car.speedKmh or 0) > 10.0 then
      state.dropped = false
      U.Log(sFormat("ROLLING: car %d moving again; back in the formation", carIndex))
    end
    return
  end

  local stage = state.recoveryStage or 0
  if state.stillTime > C.ROLLING_START_STALL_STAGE1_SECONDS and stage < 1 then
    state.recoveryStage = 1
    state.recoveryTried = true
    state.skipLaunchRamp = true
    LogStalledCarState(carIndex, car, inPit)
    RCtl.RestoreAIControl(carIndex, false)
    AwakeCar(carIndex)
  elseif state.stillTime > C.ROLLING_START_STALL_STAGE2_SECONDS and stage < 2 then
    state.recoveryStage = 2
    NudgeCarForward(carIndex, car, C.ROLLING_START_STALL_NUDGE_MPS)
    U.Log(sFormat("ROLLING: car %d recovery stage 2: forward velocity nudge", carIndex))
  elseif state.stillTime - (state.lastNudgeStill or 0.0) > 2.0 then
    -- Keep nudging every 2 s. No teleport: moving a wedged car 12 m along its heading dropped it
    -- off the track / onto other cars and CSP then sent it to the pits.
    state.lastNudgeStill = state.stillTime
    NudgeCarForward(carIndex, car, C.ROLLING_START_STALL_NUDGE_MPS * 2)
  end
  if isPlayer then return end
  if (state.stillTime > 17.5 or inPit) and not state.dropped then
    state.dropped = true
    U.Log(sFormat("ROLLING: car %d dropped from formation spacing (stalled/pit)", carIndex))
  end
end

-- A car that crashes or stalls during the formation lap gets teleported to the pits by CSP. Its AI
-- would then drive out of the pit lane and rejoin mid-formation, so it is retired instead: held
-- where it is and out of the spacing chains until its class goes green.
local function WatchForPitTeleport(carIndex, state, car, trackLength)
  if state.retired or not car then return end
  local inPit = car.isInPitlane == true or car.isInPit == true
  if state.startedInPit == nil then state.startedInPit = inPit end
  local jumped = false
  local spline = car.splinePosition
  if type(spline) == "number" and type(state.lastSplineCheck) == "number" and type(trackLength) == "number" then
    local d = spline - state.lastSplineCheck
    if d > 0.5 then d = d - 1.0 elseif d < -0.5 then d = d + 1.0 end
    jumped = math.abs(d) * trackLength > C.ROLLING_START_TELEPORT_JUMP_METERS
  end
  state.lastSplineCheck = type(spline) == "number" and spline or state.lastSplineCheck
  if (inPit and not state.startedInPit) or (jumped and state.released) then
    state.retired = true
    state.dropped = true
    U.Log(sFormat("ROLLING: car %d teleported to the pits during formation (pit=%s jump=%s); retired until green",
        carIndex, tostring(inPit), tostring(jumped)))
  end
end

-- True when the car is in double file and no more than GREEN_MAX_SLOT_EXCESS_M behind its slot
-- (ROW_M per row behind its class leader).
local function InDoubleFileSlot(state, group, car, trackLength)
  if not state.enteredPhase2 then return false end
  local leaderIndex = group and group.positionToCar and group.positionToCar[1]
  if leaderIndex == nil or leaderIndex == state.carIndex then return true end
  local leaderCar = getCar(leaderIndex)
  if not leaderCar then return true end
  local behind = -Spacing.SignedGap(car, leaderCar, trackLength)
  local slot = state.lockedPhase2Position or state.classPosition or 1
  return behind - (math.ceil(slot / 2) - 1) * C.ROLLING_START_ROW_M <= C.ROLLING_START_GREEN_MAX_SLOT_EXCESS_M
end

local function MaintainCar(carIndex, state, trackLength, moves)
  local car = getCar(carIndex)
  local isAI = (carIndex ~= 0)
  local group = S.rollingStartGroups[state.groupIndex]

  if isAI and not state.green then
    WatchForPitTeleport(carIndex, state, car, trackLength)
    if state.retired then
      HoldUnreleasedAI(carIndex)
      RCtl.SetRollingLaneOffset(carIndex, 0.0)
      return
    end
  end

  if not state.released and RGroups.IsRollingCarReleased(state) then
    RCtl.ReleaseRollingCar(carIndex)
  end

  if not state.released then
    if isAI and car and (car.isInPitlane == true or car.isInPit == true) and not state.pitLogged then
      state.pitLogged = true
      U.Log(sFormat("ROLLING: car %d ended up in the pit lane while still held (t=%.1fs)", carIndex,
          S.rollingStartSessionTime))
    end
    if isAI or (carIndex == 0 and S.playerAutopilotOn == true) then HoldUnreleasedAI(carIndex) end
    return
  end

  TrackLineCrossing(state, car, group, trackLength)
  if not state.green then WatchForStalledCar(carIndex, state, car, moves.dt) end
  -- The autopilot drives the formation until the car is AUTOPILOT_HANDOVER_METERS short of the
  -- start line, then hands the car back to the human.
  local autopiloted = carIndex == 0 and S.playerAutopilotOn == true
  local handoverPoint = 1.0 - C.AUTOPILOT_HANDOVER_METERS / trackLength
  local atHandover = false
  if autopiloted and state.hasRolledOver and car and type(car.splinePosition) == "number" then
    atHandover = car.splinePosition >= handoverPoint
    -- On a short double-file zone the handover point can fall before the car has reached its
    -- slot; keep driving until it is in place, but never past the late handover point.
    if atHandover and car.splinePosition < 1.0 - C.AUTOPILOT_LATE_HANDOVER_METERS / trackLength
        and not InDoubleFileSlot(state, group, car, trackLength) then
      atHandover = false
    end
    if S.autopilotEnabled and not S.developerMode and not state.autopilotWarned and not atHandover then
      local speed = math.max((car.speedKmh or 0) / 3.6, 5.0)
      local secondsLeft = (handoverPoint - car.splinePosition) * trackLength / speed
      if secondsLeft <= C.AUTOPILOT_OFF_WARNING_SECONDS then
        state.autopilotWarned = true
        Audio.PlayAutopilotOff()
      end
    end
  end
  -- Developer mode never hands the car back.
  if autopiloted and not S.developerMode and (atHandover or state.green) then
    RCtl.SetPlayerAutopilot(false)
    S.manualBannerUntil = 0   -- the "Manual Control" notice gives way to the blinking "You have control" banner
    S.controlBannerUntil = os.clock() + C.CONTROL_BANNER_SECONDS
    autopiloted = false
  end
  if state.green then
    EaseOutLaneOffset(carIndex, state, isAI or (autopiloted and RCtl.DevAutopilotActive()), moves.greenLaneMove)
  elseif isAI or autopiloted then
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
        if memberIndex ~= 0 or RCtl.DevAutopilotActive() then
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
  local now = os.clock()
  if now - (S.rollingStartDiagAt or -1000) < 2.5 then return end
  S.rollingStartDiagAt = now
  for _, carIndex in ipairs(S.rollingStartQueue) do
    local state = S.rollingStartCars[carIndex]
    local car = getCar(carIndex)
    if state and car then
      local group = S.rollingStartGroups and S.rollingStartGroups[state.groupIndex] or nil
      U.Log(sFormat("ROLLING: car %d cls=%s cp=%d p=%.3f v=%.1f tgt=%.1f thr=%.2f phase=%d off=%.2f rel=%s green=%s wrap=%d trav=%.2f",
          carIndex, (group and group.label) or "?", state.classPosition or -1,
          U.NormalizeTrackProgress(car.splinePosition), car.speedKmh or -1,
          state.lastTargetSpeed or -1, state.lastThrottleLimit or -1,
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
  BuildLaneOrder()

  for _, carIndex in ipairs(S.rollingStartQueue) do
    local state = S.rollingStartCars[carIndex]
    if state then MaintainCar(carIndex, state, trackLength, moves) end
  end

  RampClassCaution(dt)
  Centre.Calibrate()
  ProbeLaneSide()
  LogFormationDiagnostics()
end

return M
