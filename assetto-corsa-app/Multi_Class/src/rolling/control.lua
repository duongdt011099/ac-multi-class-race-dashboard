-- Low-level AI control for the rolling start: lane offsets, pit-teleport block, hold/release.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Audio = require("src/audio")

local sFormat = string.format
local setAITopSpeed = physics.setAITopSpeed
local setAICaution = physics.setAICaution
local setAIThrottleLimit = physics.setAIThrottleLimit

local M = {}

-- CSP keeps a car on a lateral lane offset from the AI racing line. The offset is in metres and is
-- applied through setAISplineAbsoluteOffset (the fraction-based setAISplineOffset is a different,
-- much smaller unit and does not hold a two-wide formation at speed).
function M.SetRollingLaneOffset(carIndex, offsetMeters)
  pcall(physics.setAISplineAbsoluteOffset, carIndex, offsetMeters, false)
end

-- Belt-and-suspenders guard against AC teleporting a long-held car to the pits while it is being
-- held on the grid. physics.blockTeleportingToPits() takes NO arguments, is GLOBAL (it is not a
-- per-car flag) and returns an ac.Disposable - a function you must call to release the block.
-- The old code called it as (carIndex, blocked) and discarded the returned disposable, so every
-- hold/release left a fresh permanent global block active. That is what stopped the PLAYER from
-- being moved to the pits at the end of the race (black screen / lost wheel / no pit teleport).
-- Acquire the block once for the formation and release it as soon as the formation is over.
function M.AcquireRollingPitBlock()
  if S.rollingPitBlock == nil and type(physics.blockTeleportingToPits) == "function" then
    local ok, disposable = pcall(physics.blockTeleportingToPits)
    if ok and disposable ~= nil then
      S.rollingPitBlock = disposable
    end
    U.Log(sFormat("ROLLING: pit teleport block acquired=%s", tostring(S.rollingPitBlock ~= nil)))
  end
end

function M.ReleaseRollingPitBlock()
  if S.rollingPitBlock ~= nil then
    pcall(S.rollingPitBlock)
    S.rollingPitBlock = nil
  end
end

function M.RestoreAIControl(carIndex, clearOffset)
  pcall(setAITopSpeed, carIndex, 999999.0)
  pcall(setAIThrottleLimit, carIndex, 1.0)
  pcall(setAICaution, carIndex, 1.0)
  pcall(physics.setAIStopCounter, carIndex, 0.0)
  if clearOffset ~= false then
    M.SetRollingLaneOffset(carIndex, 0.0)
  end
end

function M.RestoreRollingStartControls()
  Audio.Reset()
  -- The player car (index 0) is never held or autopiloted during the formation, so only the AI
  -- cars need their controls handed back.
  for carIndex in pairs(S.rollingStartCars) do
    if carIndex ~= 0 then
      M.RestoreAIControl(carIndex, true)
    end
  end

  M.ReleaseRollingPitBlock()

  S.rollingStartCars = {}
  S.rollingStartGroups = {}
  S.rollingStartQueue = {}
  S.rollingStartRaceHasStarted = false
  S.rollingStartReleaseTimer = 0.0
  S.rollingStartSessionTime = 0.0
  S.rollingStartLastFrame = nil
  S.rollingStartSessionKey = nil
end

function M.MarkRollingStartUnavailable(message)
  M.RestoreRollingStartControls()
  S.rollingStartState = "unavailable"
  S.rollingStartStatus = message or "Rolling start unavailable"
  U.Log("ROLLING: unavailable: " .. S.rollingStartStatus)
  pcall(function() ui.toast(ui.Icons.Warning, S.rollingStartStatus) end)
end

function M.HoldRollingCar(carIndex)
  if carIndex == 0 then return end

  setAITopSpeed(carIndex, 0.0)
  setAIThrottleLimit(carIndex, 0.0)
  setAICaution(carIndex, 1.0)
  physics.setAIStopCounter(carIndex, C.ROLLING_START_STOP_COUNTER_HELD)
  M.SetRollingLaneOffset(carIndex, 0.0)
end

function M.ReleaseRollingCar(carIndex)
  local state = S.rollingStartCars[carIndex]
  if not state or state.released then return end

  state.released = true
  state.releasedAt = S.rollingStartSessionTime
  if carIndex == 0 then return end

  physics.setAIStopCounter(carIndex, 0.0)
  U.Log(sFormat("ROLLING: released car %d (queue %d)", carIndex, state.queueIndex or -1))
end

return M
