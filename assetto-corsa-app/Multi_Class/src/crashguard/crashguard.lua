-- Crash guard: an AI car that crashes and is taken to the pits (CSP's "teleport to pit on damage")
-- gets its damage repaired and one more chance. If it crashes into the pits a second time it is locked
-- in the pit lane for the rest of the race, like the safety car.
--
-- A "crash" is a yellow-flag hazard (a car stopped or slow on track) that ends with the car in the
-- pit lane after at least CRASH_MIN_HAZARD_SECONDS; yellow.lua reports that in S.crashPitEvents.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")

local M = {}

local sFormat = string.format
local getCar = ac.getCar

local clock = 0.0
local nextScanAt = 0.0
local fullLife = {}       -- the engine life each car started with (the "fixed" value)
local lastTeleportAt = {} -- locked car -> clock of its last forced teleport

local function inPit(car)
  return car.isInPitlane == true or car.isInPit == true
end

local function hold(i)
  pcall(physics.setAITopSpeed, i, 0.0)
  pcall(physics.setAIThrottleLimit, i, 0.0)
  pcall(physics.setAICaution, i, 1.0)
  pcall(physics.setAIStopCounter, i, C.ROLLING_START_STOP_COUNTER_HELD)
end

-- Gives the car back full body and engine health. Which calls this CSP build honours is logged.
local function repair(i, car)
  local before = U.SafeGet(car, "engineLifeLeft")
  local okBody = type(physics.setCarBodyDamage) == "function"
      and pcall(physics.setCarBodyDamage, i, vec4(0, 0, 0, 0))
  local okEngine = false
  if fullLife[i] and type(physics.setCarEngineLife) == "function" then
    okEngine = pcall(physics.setCarEngineLife, i, fullLife[i])
  end
  pcall(physics.setAITopSpeed, i, 1000000.0)
  pcall(physics.setAIThrottleLimit, i, 1.0)
  U.Log(sFormat("CRASH: car %d repaired in the pits (body=%s engine=%s, engine life %s -> %s of %s)",
      i, tostring(okBody), tostring(okEngine), tostring(before),
      tostring(U.SafeGet(car, "engineLifeLeft")), tostring(fullLife[i])))
end

local function shouldRun(sim)
  if not C.CRASH_GUARD_ENABLED then return false end
  if sim.isSessionStarted ~= true or not Sess.IsRaceMode(sim) then return false end
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then return false end
  return not Sess.IsSessionFinished(sim)
end

function M.Reset()
  clock, nextScanAt = 0.0, 0.0
  fullLife, lastTeleportAt = {}, {}
  S.crashStrikes, S.crashLocked, S.crashPitEvents = {}, {}, {}
end

function M.Update(sim, dt)
  if not shouldRun(sim) then
    S.crashPitEvents = {}
    return
  end
  clock = clock + dt
  if clock < nextScanAt then return end
  nextScanAt = clock + C.CRASH_SCAN_SECONDS

  for i = 1, S.driverCount - 1 do
    local car = getCar(i)
    if car then
      local life = U.SafeGet(car, "engineLifeLeft")
      if type(life) == "number" and life > (fullLife[i] or 0) then fullLife[i] = life end

      local held = S.crashPitEvents[i]
      if held then
        S.crashPitEvents[i] = nil
        if held >= C.CRASH_MIN_HAZARD_SECONDS and not S.crashLocked[i] then
          local strikes = (S.crashStrikes[i] or 0) + 1
          S.crashStrikes[i] = strikes
          if strikes >= C.CRASH_MAX_PIT_TRIPS then
            S.crashLocked[i] = true
            U.Log(sFormat("CRASH: car %d crashed again (strike %d, stopped %.0f s); locked in the pits for good",
                i, strikes, held))
          else
            U.Log(sFormat("CRASH: car %d crashed and went to the pits (strike %d, stopped %.0f s)", i, strikes, held))
            repair(i, car)
          end
        end
      end

      if S.crashLocked[i] then
        hold(i)
        if not inPit(car) and physics.teleportCarTo and ac.SpawnSet
            and clock - (lastTeleportAt[i] or -1000.0) >= 2.0 then
          lastTeleportAt[i] = clock
          pcall(physics.teleportCarTo, i, ac.SpawnSet.Pits)
          U.Log(sFormat("CRASH: locked car %d was back on track; sent to the pits again", i))
        end
      end
    end
  end
end

return M
