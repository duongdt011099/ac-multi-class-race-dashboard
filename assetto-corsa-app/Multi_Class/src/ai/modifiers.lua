-- Situation modifiers for one AI car. Each situation adds
-- small offsets to the car's AI settings (caution, grip, aggression, brake hint, lookahead, tyre
-- hint); tuning.lua adds them to the car's base values and applies the result.
--   safe start     the first seconds of the race: a little more caution, no tyre abuse
--   spike          emergency: closing fast on a car ahead on the same line while braking hard
--   side by side   inside / outside, defending / attacking roles with the turn direction
--   clear road     nobody within 50 m: a little more attack
--   elasticaution  caution follows the car ahead: lift off when it brakes for a corner, commit when
--                  both accelerate away from one; slipstream trims caution
--   victim defense pressure right behind and room ahead: brake later, keep the line
--   class pass     a faster-class car closing on a slower-class car with a clear side commits to it
local C = require("src/constants")
local S = require("src/state")
local Field = require("src/ai/field")

local getCar = ac.getCar
local abs, min, max = math.abs, math.min, math.max

local M = {}

local SBS = {
  right = {
    inside_car_defense  = { lookaheadBase = -1, aggression = 0.11, aiGrip = 0.0355, brakehint = 0.007 },
    inside_car_attack   = { lookaheadBase = -1, aggression = 0.15, aiGrip = 0.0355, brakehint = 0.01 },
    outside_car_defense = { lookaheadBase = 1, aggression = 0.2, aiGrip = 0.0285, brakehint = -0.005, tyreHint = -0.015 },
    outside_car_attack  = { lookaheadBase = 1, aggression = 0.08, aiGrip = 0.032, brakehint = -0.01, tyreHint = -0.01 },
  },
  left = {
    inside_car_defense  = { lookaheadBase = -1, aggression = 0.11, aiGrip = 0.0355, brakehint = 0.007 },
    inside_car_attack   = { lookaheadBase = -1, aggression = 0.11, aiGrip = 0.0355, brakehint = 0.01 },
    outside_car_defense = { lookaheadBase = 1, aggression = 0.08, aiGrip = 0.0285, brakehint = -0.005, tyreHint = -0.015 },
    outside_car_attack  = { lookaheadBase = 1, aggression = 0.08, aiGrip = 0.032, brakehint = -0.01, tyreHint = -0.01 },
  },
}

-- A slower-class car with a faster-class car beside or close behind it gives way: less aggression
-- (so it stops defending its line) and a little more caution.
local YIELD = { aggression = C.AI_YIELD_AGGRESSION, aiCaution = C.AI_YIELD_CAUTION }

local function add(m, t)
  for k, v in pairs(t) do m[k] = (m[k] or 0) + v end
end

local function classRank(i)
  local key = S.driverClass[i]
  if not key then return nil end
  for r, k in ipairs(S.classOrder) do
    if k == key then return r end   -- 1 = fastest class
  end
  return nil
end

-- True when a faster-class car is within YIELD_RANGE_M behind car i, on any line.
local function fasterClassBehind(i, d)
  local myRank = classRank(i)
  if not myRank then return false end
  for j = 0, S.driverCount - 1 do
    local dj = j ~= i and Field.Get(j)
    if dj then
      local dp = d.prog - dj.prog
      if dp > 0.5 then dp = dp - 1.0 elseif dp < -0.5 then dp = dp + 1.0 end
      if dp > 0 and dp < 0.05 then
        local theirRank = classRank(j)
        if theirRank and theirRank < myRank then
          local dx, dy, dz = d.px - dj.px, d.py - dj.py, d.pz - dj.pz
          if math.sqrt(dx * dx + dy * dy + dz * dz) < C.AI_YIELD_RANGE_M then return true end
        end
      end
    end
  end
  return false
end

-- ---------------------------------------------------------------------------------------------
-- Spike: closing on the car ahead on the same line while braking hard. Returns the modifier table
-- or nil.
local function spike(car, d)
  if d.carAhead < 0 then return nil end
  local ahead, ad = getCar(d.carAhead), Field.Get(d.carAhead)
  if not (ahead and ad) then return nil end

  local speed = d.speed
  local threshold = 8.0
  if speed > 150 then threshold = 15.0 + (speed - 150) * 0.3
  elseif speed > 100 then threshold = 8.0 + (speed - 100) * 0.14 end
  threshold = min(threshold, 25.0)
  if d.gapAhead >= threshold then return nil end

  local latDiff = abs(d.lat - ad.lat)
  local converging = latDiff < 0.3 or (latDiff < 0.6 and d.gapAhead < threshold * 0.5)
  if not converging then return nil end

  local speedDiff = abs(d.speed - ad.speed)
  local brakingThreshold = speedDiff > 30 and 0.5 or speedDiff > 15 and 0.75 or 1.0
  if not (abs(d.accel) > brakingThreshold and speedDiff > 10) then return nil end

  local intensity = speed < 60 and 0.4 or speed < 80 and 0.6 or speed < 100 and 0.7 or speed < 120 and 0.8
      or speed < 160 and 1.0 or speed < 200 and 1.1 or 1.2
  local look = (d.turnDist < 200 and abs(d.turnAngle) > 15) and 1 or 0
  return {
    aiCaution = intensity, aiGrip = intensity * 0.6, aggression = -0.05,
    lookaheadBase = look * intensity, brakehint = -0.1,
  }
end

-- ---------------------------------------------------------------------------------------------
-- Side by side: role depends on which car is on the inside of the coming turn.
local function sideBySide(i, d)
  local partner = d.sbs and Field.Get(d.sbs)
  if not partner then return nil end
  -- A slower-class car beside a faster-class one does not defend: it gives way.
  local myRank, theirRank = classRank(i), classRank(d.sbs)
  if myRank and theirRank and theirRank < myRank then return YIELD end
  local role = "inside_car_defense"
  local kind = "right"
  local avgDist = (d.turnDist + partner.turnDist) / 2
  local avgAngle = (d.turnAngle + partner.turnAngle) / 2
  if avgDist > 0 and avgDist < 150 and abs(avgAngle) > 5 then
    local inside
    if avgAngle > 0 then
      kind = "right"
      inside = d.lat > partner.lat
    else
      kind = "left"
      inside = d.lat < partner.lat
    end
    role = (inside and "inside_car_" or "outside_car_") .. d.sbsRole
  else
    -- No turn near: the car on the inside-by-default defends, the other one too.
    role = "inside_car_defense"
  end
  return SBS[kind][role]
end

-- ---------------------------------------------------------------------------------------------
-- Elasticaution
local function approachingTurn(d)
  if d.turnDist > 0 and d.turnDist <= 50 then return true, abs(d.turnAngle) end
  return false, 0
end

local function decelElasticaution(d, ad, ahead)
  local decel = abs(d.accel)
  local aheadDecel = abs(ahead.acceleration and ahead.acceleration.z or 0)
  local sameLine = abs(d.lat - ad.lat) < 0.3
  if decel > 1.2 then
    if sameLine and aheadDecel > 1.0 then return 0.0
    elseif sameLine and aheadDecel < 0.5 then return 0.05 end
    return -0.05
  end
  if sameLine and aheadDecel > 1.5 and decel < 0.5 then return 0.2 end

  local near, angle = approachingTurn(d)
  if not near then return 0.1 end
  local severity = min(angle / 90.0, 1.0)
  local proximity = max(0, (150 - d.turnDist) / 100.0)
  local braking = min(decel / 3.0, 1.0)
  local angleMul = 1.0
  if angle > 30 then angleMul = min(1.0 + (angle - 30) / 60.0, 2.0) end
  local speedMod = 1.0
  if d.speed > 150 and d.turnDist < 80 then speedMod = min(1.0 + (d.speed - 100) / 200.0, 1.5) end
  return max(-0.25, min(severity * proximity * braking * speedMod * angleMul, 1.4))
end

local function accelElasticaution(d)
  local near, angle = approachingTurn(d)
  if not near and d.accel > 0 then return -0.05 end
  local severity = 1.0 - min(angle / 90.0, 1.0)
  local proximity = max(0, d.turnDist / 100.0)
  local intensity = min(max(d.accel, 0) / 3.0, 1.0)
  local angleMul = angle < 21 and 1.5 or 1.0
  local speedMod = (d.speed < 100 and d.turnDist > 100) and 1.3 or 1.0
  return max(-0.5, min(-(severity * proximity * intensity * speedMod * angleMul), 0.1))
end

-- Same speed and the right distance behind the car ahead: a slipstream, trim the caution a little.
local function slipstream(d, ahead)
  if abs(d.turnAngle) > 60 and d.turnDist <= 80 then return 0.0 end
  if abs(d.speed - (ahead.speedKmh or 0)) > 25.0 then return 0.0 end
  if d.gapAhead > 55.0 or d.gapAhead < 19.0 then return 0.0 end
  return -0.015
end

local function elasticaution(d)
  if d.carAhead < 0 or d.gapAhead > C.AI_ELASTICAUTION_RANGE_M then return 0.0 end
  local ahead, ad = getCar(d.carAhead), Field.Get(d.carAhead)
  if not (ahead and ad) then return 0.0 end
  if d.accel > 0.1 then return accelElasticaution(d) end
  if d.accel < -0.1 then return decelElasticaution(d, ad, ahead) end
  return slipstream(d, ahead)
end

-- ---------------------------------------------------------------------------------------------
-- Class pass (the old overtake assist): a faster-class car closing on a slower-class car ahead with
-- nothing beside it commits to the pass.
local function relative(car, other)
  local dx = other.position.x - car.position.x
  local dz = other.position.z - car.position.z
  local lx, lz = car.look.x, car.look.z
  local len = math.sqrt(lx * lx + lz * lz)
  if len < 1e-3 then return nil end
  lx, lz = lx / len, lz / len
  return dx * lx + dz * lz, dx * lz - dz * lx
end

local function classPass(i, car)
  local myRank = classRank(i)
  if not myRank or not car.position or not car.look then return false end
  local bestFwd, bestClosing = nil, 0
  for j = 0, S.driverCount - 1 do
    if j ~= i then
      local other = getCar(j)
      if other and other.position and other.isInPitlane ~= true and other.isInPit ~= true then
        local fwd, lat = relative(car, other)
        if fwd then
          if fwd > -6 and fwd < 8 and abs(lat) < 3.6 then return false end
          if fwd >= 8 and fwd <= C.OVERTAKE_RANGE_M and abs(lat) <= C.OVERTAKE_LATERAL_M then
            local theirRank = classRank(j)
            if theirRank and theirRank > myRank and (car.speedKmh or 0) > (other.speedKmh or 0) + 3 then
              if not bestFwd or fwd < bestFwd then bestFwd, bestClosing = fwd, (car.speedKmh or 0) - (other.speedKmh or 0) end
            elseif not theirRank or theirRank <= myRank then
              if fwd < C.OVERTAKE_RANGE_M * 0.5 then return false end
            end
          end
        end
      end
    end
  end
  return bestFwd, bestClosing
end

-- Extra caution while closing on the car ahead (same line): zero beyond CLOSE_TTC_S seconds to
-- contact, rising to CLOSE_CAUTION at contact. Any class pair; the faster car does the braking.
local function closingCaution(d)
  if d.carAhead < 0 or d.gapAhead > C.AI_CLOSE_RANGE_M then return 0.0 end
  local ad = Field.Get(d.carAhead)
  if not ad then return 0.0 end
  local closing = (d.speed - ad.speed) / 3.6
  if closing < 1.5 then return 0.0 end
  local ttc = max(d.gapAhead - 5.0, 0.0) / closing
  if ttc >= C.AI_CLOSE_TTC_S then return 0.0 end
  return C.AI_CLOSE_CAUTION * (1.0 - ttc / C.AI_CLOSE_TTC_S)
end

-- ---------------------------------------------------------------------------------------------
-- Returns the summed modifiers for car i (a table with caution / grip / aggression / brake /
-- lookahead / tyre / spike fields), or nil when no situation applies. `raceTime` is the seconds
-- since the race went green.
function M.Compute(i, car, raceTime, attack)
  local d = Field.Get(i)
  if not d then return nil end
  local m = {}
  local any = false

  if raceTime < C.AI_SAFE_START_SECONDS then
    add(m, { aiGrip = 0.005, aiCaution = 0.15, tyreHint = -0.02 })
    any = true
  end

  local sp = spike(car, d)
  local hairpin = abs(d.turnAngle) > 60 and d.turnDist < 120
  if sp then
    add(m, sp)
    m.spike = true
    return m
  end

  local sbs = sideBySide(i, d)
  if sbs then add(m, sbs); any = true end

  local yielding = fasterClassBehind(i, d)
  if yielding and sbs ~= YIELD then
    add(m, YIELD)
    any = true
  end

  if d.nearest > 50.0 and not yielding then
    add(m, { aggression = 0.1, brakehint = 0.01, lookaheadBase = -0.5 })
    any = true
  end

  if C.AI_ELASTICAUTION_ENABLED and not hairpin then
    local adj = elasticaution(d)
    if abs(adj) > 0.01 then
      m.aiCaution = (m.aiCaution or 0) + adj
      any = true
    end
  end

  if not yielding and d.carBehind >= 0 and d.gapBehind < 15 and d.gapBehind > 8 and d.gapAhead > 8 then
    add(m, { brakehint = 0.01, tyreHint = -0.02, lookaheadBase = 0.5 })
    any = true
  end

  -- Closing on a car ahead too fast: build caution before the braking zone, not after (the spike
  -- rule above only reacts once the gap is a few metres).
  -- Class pass: commit only while there is time and room. The discount fades with the gap and is off
  -- once the time to contact drops under PASS_MIN_TTC_S (a hard-closing hypercar must brake).
  local passing = false
  if S.overtakeAssistEnabled and not hairpin then
    local fwd, closing = classPass(i, car)
    if fwd then
      local ttc = (fwd - 5.0) / max(closing / 3.6, 0.5)
      if ttc >= C.AI_PASS_MIN_TTC_S then
        local scale = max(0.2, min(1.0, (fwd - 20.0) / (C.OVERTAKE_RANGE_M - 20.0)))
        add(m, { aiCaution = (C.OVERTAKE_CAUTION - 1.0) * scale, aggression = (C.OVERTAKE_AGGRESSION - 0.3) * scale })
        any = true
        passing = true
      end
    end
  end

  -- Attacking (ai/passing.lua has steered the car out to go round a slower class): commit to it.
  if attack then
    add(m, { aggression = C.AI_ATTACK_AGGRESSION, aiCaution = -C.AI_ATTACK_CAUTION, aiGrip = C.AI_ATTACK_GRIP })
    any = true
    passing = true
  end

  -- Closing on a car ahead too fast: build caution before the braking zone. A car that is passing a
  -- slower class keeps only a fraction of it (it is going round, not into the back).
  local closeCaution = closingCaution(d)
  if closeCaution > 0 then
    m.aiCaution = (m.aiCaution or 0) + closeCaution * (passing and C.AI_CLOSE_WHILE_PASSING or 1.0)
    any = true
  end

  return any and m or nil
end

return M
