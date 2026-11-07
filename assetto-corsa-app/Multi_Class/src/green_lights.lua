-- "Five green lights" banner shown at the top of the screen whenever the player is given green
-- (rolling start green only, not the green after a yellow flag).
local S = require("src/state")
local Draw = require("src/leaderboard/draw")
local Audio = require("src/audio")
local Exporter = require("src/export/exporter")
local U = require("src/util")

local M = {}

local IMAGE = "green_lights.png"   -- relative to the app folder
local IMAGE_ASPECT = 120 / 500     -- height / width of the PNG
local FADE_SECONDS = 0.4

local AUTOPILOT_IMAGE = "auto_pilot.png"       -- "Auto Pilot Mode"
local MANUAL_IMAGE = "manual_control.png"     -- "Manual Control, Hold your steering wheel"

local SAVED_IMAGE = "result_saved.png"         -- "Session Result Saved" (practice / qualifying / race)

local WAIT_IMAGE = "wait_for_others.png"       -- "Wait for other cars" (finished, back in the pits)

local LANE_LEFT_IMAGE = "lane_left.png"        -- "Your Lane: LEFT" (autopilot off, double file)
local LANE_RIGHT_IMAGE = "lane_right.png"      -- "Your Lane: RIGHT"

local LAST_LAP_IMAGE = "last_lap.png"          -- "Last Lap" (human car on the final lap of a race)

local alpha = 0.0
local lastLapAlpha = 0.0
local lastLapLogged = nil
local laneAlpha = 0.0
local laneShown = nil
local waitAlpha = 0.0
local wasWaiting = false
local savedAlpha = 0.0
local autoAlpha = 0.0
local manualAlpha = 0.0

local function fade(current, showing, dt)
  local step = (dt or 0.016) / FADE_SECONDS
  if showing then return math.min(1.0, current + step) end
  return math.max(0.0, current - step)
end

function M.Draw(dt)
  local sim = ac.getSim()
  local text = Draw.GetRollingStartDisplay(sim)
  -- Race-start green only: the green shown after a yellow flag is excluded.
  local yellowGreen = (S.yellowGreenUntil or 0) > (S.yellowClock or 0)
  local showing = type(text) == "string" and text:sub(1, 5) == "GREEN" and not yellowGreen

  -- Manual-control notice: while the "autopilot off" clip plays, and briefly after the hand-over.
  local manual = Audio.IsAutopilotOffPlaying() or (S.manualBannerUntil or 0) > os.clock()
  -- Once the player's class has gone green (or the start is over) the notice is no longer relevant.
  local playerState = S.rollingStartCars and S.rollingStartCars[0]
  local startLive = S.rollingStartState == "staged" or S.rollingStartState == "rolling"
  if (playerState and playerState.green) or not startLive then
    manual = false
    S.manualBannerUntil = 0
  end
  local auto = S.playerAutopilotOn == true and not manual and not showing
  manual = manual and not showing

  -- Lane notice for a human driver who steers the formation lap themselves (autopilot off):
  -- shown from the double-file phase until the player's class goes green.
  local laneImage = nil
  if not S.autopilotEnabled and startLive and playerState and playerState.enteredPhase2
      and not playerState.green and not showing and not manual then
    local side = Draw.GetPlayerLaneSide()
    if side == "LEFT" then laneImage = LANE_LEFT_IMAGE elseif side == "RIGHT" then laneImage = LANE_RIGHT_IMAGE end
  end
  -- Last-lap notice: stays up from the moment the human car starts the final lap of a race until
  -- it takes the flag (lap count reaches the race length).
  local lastLap = false
  local totalLaps = S.liveTotalLaps or 0
  if S.liveSessionType == "race" and totalLaps > 0 and not startLive then
    local okCar, car = pcall(ac.getCar, 0)
    local lc = okCar and car and car.lapCount or nil
    if type(lc) == "number" then
      if lc ~= lastLapLogged then
        lastLapLogged = lc
        pcall(U.Log, string.format("LASTLAP: player lapCount=%d totalLaps=%d", lc, totalLaps))
      end
      lastLap = lc >= totalLaps - 1 and lc < totalLaps
    end
  end
  lastLap = lastLap and not showing and not manual
  lastLapAlpha = fade(lastLapAlpha, lastLap, dt)

  laneAlpha = fade(laneAlpha, laneImage ~= nil, dt)
  if laneImage then laneShown = laneImage end

  local okWait, waiting = pcall(Exporter.IsPlayerWaitingForOthers, sim)
  waiting = okWait and waiting == true and not showing and not manual
  if waiting and not wasWaiting then
    pcall(function() ui.toast(ui.Icons.Warning, "Wait for other cars to finish") end)
  end
  wasWaiting = waiting
  waitAlpha = fade(waitAlpha, waiting, dt)

  local saved = (S.resultSavedBannerUntil or 0) > os.clock() and not showing and not manual
  savedAlpha = fade(savedAlpha, saved, dt)

  alpha = fade(alpha, showing, dt)
  autoAlpha = fade(autoAlpha, auto, dt)
  manualAlpha = fade(manualAlpha, manual, dt)

  local size = ui.windowSize()
  local width = size.x
  local height = width * IMAGE_ASPECT
  if alpha > 0.0 then
    ui.drawImage(IMAGE, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, alpha))
  end
  if autoAlpha > 0.0 then
    ui.drawImage(AUTOPILOT_IMAGE, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, autoAlpha))
  end
  if manualAlpha > 0.0 then
    ui.drawImage(MANUAL_IMAGE, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, manualAlpha))
  end
  if lastLapAlpha > 0.0 then
    ui.drawImage(LAST_LAP_IMAGE, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, lastLapAlpha))
  end
  if laneAlpha > 0.0 and laneShown then
    ui.drawImage(laneShown, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, laneAlpha))
  end
  if waitAlpha > 0.0 then
    ui.drawImage(WAIT_IMAGE, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, waitAlpha))
  end
  if savedAlpha > 0.0 then
    ui.drawImage(SAVED_IMAGE, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, savedAlpha))
  end
end

return M
