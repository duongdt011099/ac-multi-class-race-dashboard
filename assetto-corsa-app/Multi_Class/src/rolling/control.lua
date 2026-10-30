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

-- Hands the human car to (or takes it back from) CSP's AI driver. Only used for the optional
-- single-file autopilot; S.playerAutopilotOn tracks it so it is released exactly once.
function M.SetPlayerAutopilot(enabled)
  if enabled == (S.playerAutopilotOn == true) then return end
  if type(physics.setCarAutopilot) ~= "function" then
    if enabled then U.Log("ROLLING: physics.setCarAutopilot is unavailable; autopilot skipped") end
    return
  end
  -- Signature is setCarAutopilot(active, chooseTyres): it always acts on the user car, there is no
  -- car index argument (passing 0 first would read as active=true).
  local ok, err = pcall(physics.setCarAutopilot, enabled)
  if not ok then
    U.Log("ROLLING: setCarAutopilot failed: " .. tostring(err))
    return
  end
  S.playerAutopilotOn = enabled
  if not enabled then M.RestoreAIControl(0, true) end
  U.Log("ROLLING: player autopilot " .. (enabled and "ON" or "OFF (manual)"))
end

function M.RestoreRollingStartControls()
  Audio.Reset()
  M.SetPlayerAutopilot(false)
  -- The player car (index 0) is never held, so only the AI cars need their controls handed back.
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

-- Short description of the car ahead in the class order: its speed and how far in front it is.
-- Added to the release log so the run-up spacing can be checked in the log.
local function AheadInfo(state)
  local group = S.rollingStartGroups and S.rollingStartGroups[state.groupIndex]
  if not (group and group.cars and state.classPosition) then return "" end
  local aheadIndex = group.cars[state.classPosition - 1]
  local aheadCar = aheadIndex and ac.getCar(aheadIndex)
  if not aheadCar then return "" end

  local gapText = ""
  local sim = ac.getSim()
  local trackLength = sim and sim.trackLengthM
  local car = ac.getCar(state.carIndex)
  if type(trackLength) == "number" and trackLength > 0
      and type(aheadCar.splinePosition) == "number" and car
      and type(car.splinePosition) == "number" then
    local d = aheadCar.splinePosition - car.splinePosition
    if d > 0.5 then d = d - 1.0 elseif d < -0.5 then d = d + 1.0 end
    gapText = sFormat(", %.0f m ahead", d * trackLength)
  end
  return sFormat(" | ahead car %d at %.0f km/h%s", aheadIndex, aheadCar.speedKmh or 0, gapText)
end

function M.ReleaseRollingCar(carIndex)
  local state = S.rollingStartCars[carIndex]
  if not state or state.released then return end

  state.released = true
  state.releasedAt = S.rollingStartSessionTime
  if carIndex == 0 then
    if S.autopilotEnabled then M.SetPlayerAutopilot(true) end
    return
  end

  physics.setAIStopCounter(carIndex, 0.0)
  U.Log(sFormat("ROLLING: released car %d (queue %d)%s", carIndex, state.queueIndex or -1, AheadInfo(state)))
end

return M
