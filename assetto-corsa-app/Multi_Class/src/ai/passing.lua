-- Class passing: a faster-class AI car that catches a slower-class car on the same line steers out
-- to the roomier side and goes round it, instead of sitting behind it waiting for the AI to find a
-- gap. The lateral offset (metres, through physics.setAISplineAbsoluteOffset) is eased out, held
-- until the slower car is behind, then eased back to the racing line. Step() returns true while the
-- car is attacking, so the modifiers can make it more committed (less caution, more aggression).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Field = require("src/ai/field")

local sFormat = string.format
local abs, min, max = math.abs, math.min, math.max

local M = {}

local state = {}      -- [i] = { off, side, target, since, lat0, checked }
local signFlip = 1    -- learned once: -1 when a positive offset moves the car toward negative lateral

local function classRank(i)
  local key = S.driverClass[i]
  if not key then return nil end
  for r, k in ipairs(S.classOrder) do
    if k == key then return r end   -- 1 = fastest class
  end
  return nil
end

-- Another car beside the one at i (within SIDE_CHECK_M) on the side we want to move to.
local function sideBlocked(i, d, side)
  for j = 0, S.driverCount - 1 do
    local dj = j ~= i and Field.Get(j)
    if dj then
      local dp = dj.prog - d.prog
      if dp > 0.5 then dp = dp - 1.0 elseif dp < -0.5 then dp = dp + 1.0 end
      if abs(dp) < 0.01 then
        local dx, dy, dz = d.px - dj.px, d.py - dj.py, d.pz - dj.pz
        if dx * dx + dy * dy + dz * dz < C.AI_PASS_SIDE_CHECK_M * C.AI_PASS_SIDE_CHECK_M then
          local dl = (dj.lat - d.lat) * side
          if dl > 0.02 and dl < 0.9 then return true end
        end
      end
    end
  end
  return false
end

local function setOffset(i, st, value)
  if abs(value - (st.sent or 0)) > 0.03 then
    st.sent = value
    pcall(physics.setAISplineAbsoluteOffset, i, value * signFlip, false)
  end
end

function M.Release(i)
  local st = state[i]
  if st then
    pcall(physics.setAISplineAbsoluteOffset, i, 0.0, false)
    state[i] = nil
  end
end

function M.Reset()
  for i in pairs(state) do M.Release(i) end
  state = {}
end

-- Is the slower car still worth passing: ahead, or just beside / only just behind?
local function targetStatus(d, td)
  local dp = td.prog - d.prog
  if dp > 0.5 then dp = dp - 1.0 elseif dp < -0.5 then dp = dp + 1.0 end
  local dx, dy, dz = d.px - td.px, d.py - td.py, d.pz - td.pz
  local dist = math.sqrt(dx * dx + dy * dy + dz * dz)
  return dp, dist
end

-- Called every apply tick for a free AI car. Returns true while the car is attacking.
function M.Step(i, car, d, dt)
  local st = state[i]
  local myRank = classRank(i)
  local want = 0

  if st and st.target then
    -- Latched: keep the side until the slower car is clearly behind, the turn is near, or time is up.
    local td = Field.Get(st.target)
    if td and st.age < C.AI_PASS_MAX_SECONDS then
      local dp, dist = targetStatus(d, td)
      local behindClear = dp < 0 and dist > C.AI_PASS_CLEAR_M
      local turnSoon = d.turnDist < 60 and abs(d.turnAngle) > 25
      if not behindClear and not turnSoon then want = st.side end
    end
  elseif myRank and d.carAhead >= 0 and d.gapAhead > 6.0 and d.gapAhead < C.AI_PASS_ATTACK_M then
    local ad = Field.Get(d.carAhead)
    local theirRank = classRank(d.carAhead)
    local turnSoon = d.turnDist < 80 and abs(d.turnAngle) > 25
    if ad and theirRank and theirRank > myRank and d.speed > ad.speed + 3.0 and not turnSoon then
      local spaceLeft, spaceRight = 1.0 + ad.lat, 1.0 - ad.lat
      local side = spaceLeft >= spaceRight and -1 or 1
      if sideBlocked(i, d, side) then side = -side end
      if not sideBlocked(i, d, side) then
        st = { off = 0.0, side = side, target = d.carAhead, age = 0.0 }
        state[i] = st
        want = side
        U.Log(sFormat("AI: car %d (%s) passes slower-class car %d: %.0f m ahead, closing %.0f km/h, side %d",
            i, tostring(S.driverClass[i]), d.carAhead, d.gapAhead, d.speed - ad.speed, side))
      end
    end
  end

  if not st then return false end
  st.age = st.age + dt

  local target = want * C.AI_PASS_OFFSET_M
  local rate = (want ~= 0) and C.AI_PASS_RATE_MPS or C.AI_PASS_RETURN_MPS
  st.off = st.off + max(-rate * dt, min(rate * dt, target - st.off))
  setOffset(i, st, st.off)

  -- Learn the offset direction once: after the car has moved out, its lateral position must have
  -- shifted toward the side it was sent to. If it went the other way, flip the sign for good.
  if want ~= 0 and not st.checked then
    if abs(st.off) > 1.0 and not st.lat0 then st.lat0, st.checkAt = d.lat, st.age + 1.2 end
    if st.lat0 and st.age >= st.checkAt then
      st.checked = true
      local moved = (d.lat - st.lat0) * want
      if moved < -0.03 then
        signFlip = -signFlip
        U.Log(sFormat("AI: pass offset moved car %d the wrong way (lat %.2f -> %.2f); flipping the offset sign",
            i, st.lat0, d.lat))
      end
    end
  end

  if want == 0 and abs(st.off) < 0.05 then
    M.Release(i)
    return false
  end
  return want ~= 0
end

return M
