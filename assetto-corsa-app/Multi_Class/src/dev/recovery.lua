-- Developer mode only: gets the autopilot-driven human car back into the race after it was sent to
-- the pits (CSP's damage teleport). AI cars are repaired and released by crashguard.lua and
-- ai/tuning.lua, but both skip car 0, and in normal mode the human simply drives out themselves.
-- Called only while RCtl.DevAutopilotActive().
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local RCtl = require("src/rolling/control")

local M = {}

local PIT_WAIT_SECONDS = 4.0     -- time in the pits before the first recovery attempt
local RETRY_SECONDS = 12.0       -- between attempts while the car is still in the pits
local clock = 0.0
local pitSince = nil
local lastAttempt = -1000.0
local fullLife = nil

local function inPit(car)
  return car.isInPitlane == true or car.isInPit == true
end

local function repair(car)
  pcall(function()
    if type(physics.setCarBodyDamage) == "function" then physics.setCarBodyDamage(0, vec4(0, 0, 0, 0)) end
    if fullLife and type(physics.setCarEngineLife) == "function" then physics.setCarEngineLife(0, fullLife) end
  end)
end

function M.Reset()
  clock, pitSince, lastAttempt, fullLife = 0.0, nil, -1000.0, nil
end

function M.Update(sim, dt)
  local car = ac.getCar(0)
  if not car then return end
  if sim.isSessionStarted ~= true or not Sess.IsRaceMode(sim) then pitSince = nil return end
  -- Race not started / still in the formation: the rolling start owns the car. After the leader has
  -- finished the pits are closed and the car stays where it is.
  if S.rollingStartState ~= "complete" or S.raceLeaderFinished or S.playerRetired then pitSince = nil return end
  clock = clock + dt

  local life = U.SafeGet(car, "engineLifeLeft")
  if type(life) == "number" and life > (fullLife or 0) then fullLife = life end

  if not inPit(car) then pitSince = nil return end
  pitSince = pitSince or clock
  if clock - pitSince < PIT_WAIT_SECONDS or clock - lastAttempt < RETRY_SECONDS then return end
  lastAttempt = clock

  U.Log(U.CarDiag and ("DEV: human car is in the pits; repairing and re-engaging the autopilot | " .. U.CarDiag(car))
      or "DEV: human car is in the pits; re-engaging the autopilot")
  repair(car)
  -- Hand the car back to the AI cleanly: clear any stale limits/hold, then restart the autopilot.
  RCtl.RestoreAIControl(0, true)
  RCtl.SetPlayerAutopilot(false)
  RCtl.SetPlayerAutopilot(true)
  pcall(physics.setAIStopCounter, 0, 0.0)
  pcall(physics.awakeCar, 0)
end

return M
