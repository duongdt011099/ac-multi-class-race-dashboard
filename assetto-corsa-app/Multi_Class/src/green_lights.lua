-- "Five green lights" banner shown at the top of the screen whenever the player is given green
-- (rolling start green only, not the green after a yellow flag).
local S = require("src/state")
local C = require("src/constants")
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
local CONTROL_IMAGES = { "you_have_control_1.png", "you_have_control_2.png" }   -- "You have control", blinking

local SAVED_IMAGE = "result_saved.png"         -- "Session Result Saved" (practice / qualifying / race)

local WAIT_IMAGE = "wait_for_others.png"       -- "Wait for other cars" (finished, back in the pits)

local LANE_LEFT_IMAGE = "lane_left.png"        -- "Your Lane: LEFT" (autopilot off, double file)
local LANE_RIGHT_IMAGE = "lane_right.png"      -- "Your Lane: RIGHT"

local LAST_LAP_IMAGE = "last_lap.png"          -- "Last Lap" (human car on the final lap of a race)

local alpha = 0.0
local lastLapAlpha = 0.0
local lastLapLogged = nil
local lastLeaderScanAt = 0
local leaderLapCount = 0
local leaderFinalLc = nil
local leaderDoneLc = nil
local laneAlpha = 0.0
local laneShown = nil
local waitAlpha = 0.0
local wasWaiting = false
local savedAlpha = 0.0
local finishedAlpha = 0.0
local autoAlpha = 0.0
local manualAlpha = 0.0
local controlAlpha = 0.0
local yellowAlpha = 0.0
local yellowLeft = 5

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
  -- The "You have control" banner outranks the manual notice: the autopilot-off clip can still be
  -- playing when the car is handed back, and must not keep the new banner from showing.
  local control = (S.controlBannerUntil or 0) > os.clock() and not showing
  local auto = S.playerAutopilotOn == true and not manual and not showing
  manual = manual and not showing and not control
  -- "You have control" after the autopilot hands the car back: two images swapped every few tenths of
  -- a second make it blink. Like the manual notice it hides the lower-priority banners.
  local covered = manual or control

  -- Lane notice for a human driver who steers the formation lap themselves (autopilot off):
  -- shown from the double-file phase until the player's class goes green.
  local laneImage = nil
  if not S.autopilotEnabled and startLive and playerState and playerState.enteredPhase2
      and not playerState.green and not showing and not covered then
    local side = Draw.GetPlayerLaneSide()
    if side == "LEFT" then laneImage = LANE_LEFT_IMAGE elseif side == "RIGHT" then laneImage = LANE_RIGHT_IMAGE end
  end
  -- Last-lap notice: stays up from the moment the human car starts its final lap of a race until
  -- it takes the flag. A car on the lead lap starts its final lap with lap count totalLaps-1. A
  -- lapped car never gets there, so for it the final lap starts at its first line crossing after
  -- the overall leader began the leader's own final lap, and ends at its first crossing after the
  -- leader has finished.
  local lastLap = false
  local totalLaps = S.liveTotalLaps or 0
  if S.liveSessionType == "race" and totalLaps > 0 and not startLive then
    local okCar, car = pcall(ac.getCar, 0)
    local lc = okCar and car and car.lapCount or nil
    if type(lc) == "number" then
      local nowClock = os.clock()
      if nowClock - lastLeaderScanAt > 0.5 then
        lastLeaderScanAt = nowClock
        local best = 0
        for i = 0, (S.driverCount or 1) - 1 do
          local okOther, other = pcall(ac.getCar, i)
          local olc = okOther and other and other.lapCount or nil
          if type(olc) == "number" and olc > best then best = olc end
        end
        leaderLapCount = best
      end
      if lc < (lastLapLogged or 0) then leaderFinalLc, leaderDoneLc = nil, nil end   -- new session
      if leaderLapCount < totalLaps - 1 then leaderFinalLc, leaderDoneLc = nil, nil end
      if leaderLapCount >= totalLaps - 1 and leaderFinalLc == nil then leaderFinalLc = lc end
      if leaderLapCount >= totalLaps and leaderDoneLc == nil then leaderDoneLc = lc end
      if lc ~= lastLapLogged then
        lastLapLogged = lc
        pcall(U.Log, string.format("LASTLAP: player lapCount=%d totalLaps=%d leaderLapCount=%d", lc, totalLaps, leaderLapCount))
      end
      local onFinalLap = lc >= totalLaps - 1 or (leaderFinalLc ~= nil and lc > leaderFinalLc)
      local finished = lc >= totalLaps or (leaderDoneLc ~= nil and lc > leaderDoneLc)
      lastLap = onFinalLap and not finished
    end
  end
  lastLap = lastLap and not showing and not covered
  lastLapAlpha = fade(lastLapAlpha, lastLap, dt)

  laneAlpha = fade(laneAlpha, laneImage ~= nil, dt)
  if laneImage then laneShown = laneImage end

  local okWait, waiting = pcall(Exporter.IsPlayerWaitingForOthers, sim)
  waiting = okWait and waiting == true and not showing and not covered
  wasWaiting = waiting
  waitAlpha = fade(waitAlpha, waiting, dt)

  local saved = (S.resultSavedBannerUntil or 0) > os.clock() and not showing and not covered
  savedAlpha = fade(savedAlpha, saved, dt)
  finishedAlpha = fade(finishedAlpha, S.raceFinishedBanner == true and not showing and not covered, dt)

  alpha = fade(alpha, showing, dt)
  autoAlpha = fade(autoAlpha, auto, dt)
  manualAlpha = fade(manualAlpha, manual, dt)
  controlAlpha = fade(controlAlpha, control, dt)

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
  if controlAlpha > 0.0 then
    local blink = math.floor(os.clock() / C.CONTROL_BANNER_BLINK_SECONDS) % 2 + 1
    ui.drawImage(CONTROL_IMAGES[blink], vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, controlAlpha))
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
  if finishedAlpha > 0.0 then
    -- Drawn in code: a card with a chequered strip, the headline and a hint to leave.
    local x1, y1, x2, y2 = 0, 0, width, height
    local fill = rgbm(0.06, 0.08, 0.07, 0.94 * finishedAlpha)
    Draw.DrawCard(x1, y1, x2, y2, fill)
    local cell, cols = (y2 - y1) / 6, math.floor((x2 - x1) / ((y2 - y1) / 6))
    for c = 0, cols - 1 do
      for r = 0, 1 do
        local light = (c + r) % 2 == 0
        local cx = x1 + c * cell
        ui.drawRectFilled(vec2(cx, y1 + r * cell * 0.5), vec2(math.min(cx + cell, x2), y1 + (r + 1) * cell * 0.5),
          rgbm(light and 1 or 0.1, light and 1 or 0.1, light and 1 or 0.1, 0.9 * finishedAlpha))
      end
    end
    local mid = (x1 + x2) / 2
    Draw.DrawBoldText(S.finishedBannerTitle or "SESSION FINISHED", mid, y1 + (y2 - y1) * 0.48, math.floor((y2 - y1) * 0.34),
      rgbm(0.45, 1.0, 0.55, finishedAlpha))
    local hint = "Result saved  -  you can exit the game"
    if S.raceExitAt then
      hint = string.format("Result saved  -  exiting the game in %d seconds",
        math.max(0, math.ceil(S.raceExitAt - os.clock())))
    end
    Draw.DrawBoldText(hint, mid, y1 + (y2 - y1) * 0.80,
      math.floor((y2 - y1) * 0.16), rgbm(0.85, 0.88, 0.95, finishedAlpha), "label")
  end
  -- Yellow flag warning when the flag sounds are off: drawn in code, counting down to the limit.
  local yellowSoon = S.fcy == "yellow_pending" and S.fcyNoAudio == true and not showing and not covered
  yellowAlpha = fade(yellowAlpha, yellowSoon, dt)
  if yellowAlpha > 0.0 then
    if yellowSoon then
      yellowLeft = math.max(1, math.ceil(C.YELLOW_NO_AUDIO_COUNTDOWN_SECONDS - ((S.yellowClock or 0) - (S.fcyAt or 0))))
    end
    ui.drawImage(string.format("yellow_in_%d.png", math.max(1, math.min(5, yellowLeft))),
      vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, yellowAlpha))
  end
  if savedAlpha > 0.0 then
    ui.drawImage(SAVED_IMAGE, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, savedAlpha))
  end
end

return M
