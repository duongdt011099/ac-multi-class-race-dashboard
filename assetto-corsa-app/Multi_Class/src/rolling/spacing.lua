-- Rolling start: phase-two (double file) speed/throttle spacing.
local C = require("src/constants")

local getCar = ac.getCar

local M = {}

local SPEED = C.ROLLING_START_SPEED_KMH

-- Linear catch-up multiplier from the reference app: 1.0 at minDist, up to maxMult at maxDist.
function M.GetLinearMultiplier(dist, minDist, maxDist, maxMult)
  maxMult = maxMult or C.ROLLING_START_CATCHUP_LIMIT
  if dist <= minDist then return 1.0 end
  if dist >= maxDist then return maxMult end
  return 1.0 + ((dist - minDist) / (maxDist - minDist)) * (maxMult - 1.0)
end

-- Speed/throttle when a partner is behind or level: ease off in proportion to the lead error.
local function easeOffForLead(leadError)
  return math.max(SPEED * 0.80, SPEED - leadError * 4.0), math.max(0.70, 1.0 - (leadError / 5.0))
end

local function leaderSpeed(car, map, trackLength)
  local targetSpeed = SPEED
  local p2Index = map[2]
  local p2Car = p2Index and getCar(p2Index)
  if p2Car and p2Car.splinePosition > car.splinePosition then
    local longDist = (p2Car.splinePosition - car.splinePosition) * trackLength
    targetSpeed = SPEED * M.GetLinearMultiplier(longDist, 0.0, 10.0)
  end
  return targetSpeed, 1.0
end

local function secondSpeed(car, map, trackLength)
  local targetSpeed, throttleLimit = SPEED, 1.0
  local p1Index = map[1]
  local p1Car = p1Index and getCar(p1Index)
  if not p1Car then return targetSpeed, throttleLimit end

  local longDist = (p1Car.splinePosition - car.splinePosition) * trackLength
  if p1Car.splinePosition <= car.splinePosition then
    return easeOffForLead(math.abs(longDist))
  end
  if longDist > 1.0 then
    return SPEED * M.GetLinearMultiplier(longDist, 1.0, 10.0), throttleLimit
  end
  local ratio = 0.85 + (longDist / 1.0) * 0.15
  return SPEED * ratio, ratio
end

-- Column target: hold the gap to the car two rows ahead. Also returns the gap in metres.
local function columnTarget(car, map, startPos, trackLength)
  local aheadIndex = map[startPos - 2]
  local aheadCar = aheadIndex and getCar(aheadIndex)
  if not aheadCar then return SPEED, 1.0, 0.0 end

  if aheadCar.splinePosition <= car.splinePosition then
    local leadError = (car.splinePosition - aheadCar.splinePosition) * trackLength
    local speed, throttle = easeOffForLead(leadError)
    return speed, throttle, 0.0
  end

  local splineDist = (aheadCar.splinePosition - car.splinePosition) * trackLength
  if splineDist > 8.0 then
    return SPEED * M.GetLinearMultiplier(splineDist - 8.0, 0.0, 14.0), 1.0, splineDist
  elseif splineDist > 1.5 then
    local ratio = 0.50 + ((splineDist - 1.5) / 6.5) * 0.50
    return SPEED * ratio, ratio, splineDist
  end
  return 0.0, 0.0, splineDist
end

-- Even slots follow their odd partner ahead.
local function evenRowTarget(car, partnerCar, trackLength)
  local longDistRow = (partnerCar.splinePosition - car.splinePosition) * trackLength
  if partnerCar.splinePosition <= car.splinePosition then
    return easeOffForLead(math.abs(longDistRow))
  end
  if longDistRow > 1.0 then
    return SPEED * M.GetLinearMultiplier(longDistRow - 1.0, 0.0, 10.0), 1.0
  end
  local ratio = 0.85 + (longDistRow / 1.0) * 0.15
  return SPEED * ratio, ratio
end

-- Odd slots wait for their even partner to catch up from behind.
local function oddRowTarget(car, partnerCar, trackLength)
  local longDistRow = (car.splinePosition - partnerCar.splinePosition) * trackLength
  if car.splinePosition > partnerCar.splinePosition then
    if longDistRow > 2.0 then
      return SPEED * 0.85, 0.70
    end
    return SPEED, 1.0
  end
  return SPEED * M.GetLinearMultiplier(math.abs(longDistRow), 0.0, 10.0), 1.0
end

local function rowTarget(car, map, startPos, trackLength)
  local isEven = (startPos % 2 == 0)
  local partnerIndex = map[isEven and (startPos - 1) or (startPos + 1)]
  local partnerCar = partnerIndex and getCar(partnerIndex)
  if not partnerCar then return SPEED, 1.0 end

  if isEven then return evenRowTarget(car, partnerCar, trackLength) end
  return oddRowTarget(car, partnerCar, trackLength)
end

local function blendedSpeed(car, map, startPos, trackLength)
  local speedCol, throttleCol, splineDist = columnTarget(car, map, startPos, trackLength)
  local speedRow, throttleRow = rowTarget(car, map, startPos, trackLength)

  local blendFactor = math.min(1.0, math.max(0.0, (splineDist - 8.0) / 6.0))
  local parallelSpeed = math.min(speedCol, speedRow)
  local parallelThrottle = math.min(throttleCol, throttleRow)

  return parallelSpeed + (speedCol - parallelSpeed) * blendFactor,
      parallelThrottle + (throttleCol - parallelThrottle) * blendFactor
end

-- Phase-two (double file) spacing, ported from the reference app but using class-relative
-- positions: the class leader waits for its P2, P2 tucks in behind its P1, and every other car
-- blends a "column" target (the car two rows ahead) with a "row partner" target. Returns the
-- commanded top speed and a throttle limit.
function M.ApplyRollingPhaseTwoSpacing(state, group, trackLength)
  local car = getCar(state.carIndex)
  local startPos = state.lockedPhase2Position or state.classPosition
  if not car or type(startPos) ~= "number" then return SPEED, 1.0 end

  local map = group and group.positionToCar or {}

  if startPos == 1 then return leaderSpeed(car, map, trackLength) end
  if startPos == 2 then return secondSpeed(car, map, trackLength) end
  return blendedSpeed(car, map, startPos, trackLength)
end

return M
