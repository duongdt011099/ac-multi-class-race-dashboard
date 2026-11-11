-- Multi-class overtaking assist. AI cars treat every car ahead the same and, at the default caution,
-- sit behind a slower-class car they could easily pass. While a faster-class AI car is closing on a
-- slower-class car ahead and nothing else is beside it, its caution is lowered (and aggression
-- raised) so it commits to the pass; both go back to normal once the slower car is behind or the
-- way is blocked. Nothing else about the car is changed.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")

local getCar = ac.getCar

local M = {}

local clock = 0.0
local lastScanAt = -1000.0
local active = {}   -- [carIndex] = true while the assist has changed that car's caution

local function inPit(car)
  return car.isInPitlane == true or car.isInPit == true
end

local function classRank(i)
  local key = S.driverClass[i]
  if not key then return nil end
  for r, k in ipairs(S.classOrder) do
    if k == key then return r end   -- 1 = fastest class
  end
  return nil
end

local function release(i)
  if active[i] then
    pcall(physics.setAICaution, i, 1.0)
    if physics.setAIAggression then pcall(physics.setAIAggression, i, 0.0) end
    active[i] = nil
  end
end

-- Forward / sideways offset of `other` from `car` in the car's own frame (metres).
local function relative(car, other)
  local dx = other.position.x - car.position.x
  local dz = other.position.z - car.position.z
  local lx, lz = car.look.x, car.look.z
  local len = math.sqrt(lx * lx + lz * lz)
  if len < 1e-3 then return nil end
  lx, lz = lx / len, lz / len
  return dx * lx + dz * lz, dx * lz - dz * lx
end

local function eligible(i, car)
  if inPit(car) then return false end
  local b = S.blueCars[i]
  if b and b.passer then return false end
  local y = S.yellowCars[i]
  return not (y and (y.hazard or y.limited or y.releasing))
end

local function evaluate(i, car)
  local myRank = classRank(i)
  if not myRank or not car.position or not car.look then return false end

  local targetAhead = false
  for j = 0, S.driverCount - 1 do
    if j ~= i then
      local other = getCar(j)
      if other and other.position and not inPit(other) then
        local fwd, lat = relative(car, other)
        if fwd then
          -- Someone alongside (either side) means the way is not clear: stay cautious.
          if fwd > -6 and fwd < 8 and math.abs(lat) < 3.6 then return false end
          if fwd >= 8 and fwd <= C.OVERTAKE_RANGE_M and math.abs(lat) <= C.OVERTAKE_LATERAL_M then
            local theirRank = classRank(j)
            if theirRank and theirRank > myRank and (car.speedKmh or 0) > (other.speedKmh or 0) + 3 then
              targetAhead = true
            elseif not theirRank or theirRank <= myRank then
              -- Same or faster class directly ahead: normal behaviour.
              if fwd < C.OVERTAKE_RANGE_M * 0.5 then return false end
            end
          end
        end
      end
    end
  end
  return targetAhead
end

function M.Reset()
  for i in pairs(active) do release(i) end
  active = {}
  clock = 0.0
  lastScanAt = -1000.0
end

local function shouldRun(sim)
  if not S.overtakeAssistEnabled then return false end
  if sim.isSessionStarted ~= true or not Sess.IsRaceMode(sim) then return false end
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then return false end
  if S.fcy ~= "green" then return false end
  return not Sess.IsSessionFinished(sim)
end

function M.Update(sim, dt)
  if not shouldRun(sim) then
    if next(active) ~= nil then M.Reset() end
    return
  end
  clock = clock + dt
  if clock - lastScanAt < C.OVERTAKE_SCAN_SECONDS then return end
  lastScanAt = clock

  for i = 1, S.driverCount - 1 do
    local car = getCar(i)
    if car and eligible(i, car) and evaluate(i, car) then
      pcall(physics.setAICaution, i, C.OVERTAKE_CAUTION)
      if physics.setAIAggression then pcall(physics.setAIAggression, i, C.OVERTAKE_AGGRESSION) end
      active[i] = true
    else
      release(i)
    end
  end
end

return M
