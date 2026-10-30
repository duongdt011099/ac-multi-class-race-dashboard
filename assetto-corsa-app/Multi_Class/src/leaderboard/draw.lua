-- Leaderboard window drawing (lap / overall / class panels and the bottom status panel).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local Positions = require("src/leaderboard/positions")

local M = {}

local sFormat = string.format

local hasDWrite = (type(ui.pushDWriteFont) == "function" and type(ui.dwriteText) == "function")

function M.MeasureBoldText(text, fontSize)
  if hasDWrite then
    ui.pushDWriteFont("Segoe UI;Weight=Bold")
    local s = ui.measureDWriteText(text, fontSize)
    ui.popDWriteFont()
    return s
  end
  return ui.measureText(text)
end

function M.DrawBoldText(text, centerX, centerY, fontSize, color)
  if not hasDWrite then
    local size = ui.measureText(text)
    ui.setCursor(vec2(centerX - size.x / 2, centerY - size.y / 2))
    if color then
      ui.textColored(text, color)
    else
      ui.text(text)
    end
    return size.y
  end
  ui.pushDWriteFont("Segoe UI;Weight=Bold")
  local size = ui.measureDWriteText(text, fontSize)
  ui.setCursor(vec2(centerX - size.x / 2, centerY - size.y / 2))
  if color then
    ui.dwriteText(text, fontSize, color)
  else
    ui.dwriteText(text, fontSize)
  end
  ui.popDWriteFont()
  return size.y
end

local MeasureBoldText = M.MeasureBoldText
local DrawBoldText = M.DrawBoldText

-- The lane the human car should hold during the formation, from its starting grid slot parity and
-- the current pole-side setting. nil when the slot is unknown.
function M.GetPlayerLaneSide()
  local pos = S.rollingStartGridPositions[0]
  if type(pos) ~= "number" or pos <= 0 then return nil end
  local isOdd = (pos % 2 == 1)
  if S.rollingStartPoleOnRight then
    return isOdd and "RIGHT" or "LEFT"
  end
  return isOdd and "LEFT" or "RIGHT"
end

local function greenRecently(player, sim)
  return player.greenAt and type(sim.time) == "number" and sim.time / 1000 - player.greenAt <= 4
end

local function rollingFormationDisplay(sim)
  local player = S.rollingStartCars[0]
  if player and player.green then
    if greenRecently(player, sim) then
      return "GREEN — GO", rgbm(0.25, 1.0, 0.35, 1)
    end
    return nil, nil
  end
  local phaseText = player.enteredPhase2 and "DOUBLE FILE" or "SINGLE FILE"
  local lane = M.GetPlayerLaneSide()
  if lane then
    return sFormat("%s — YOUR LANE: %s", phaseText, lane), rgbm(1.0, 0.75, 0.25, 1)
  end
  return phaseText .. " — FORMATION", rgbm(1.0, 0.75, 0.25, 1)
end

function M.GetRollingStartDisplay(sim)
  local state = S.rollingStartState
  if state ~= "staged" and state ~= "rolling" then
    if S.yellowPlayerActive then
      return "YELLOW", rgbm(1.0, 0.9, 0.1, 1)
    elseif (S.yellowGreenUntil or 0) > (S.yellowClock or 0) then
      return "GREEN", rgbm(1, 1, 1, 1)
    end
  end
  if state == "staged" then
    return "ROLLING START READY", rgbm(1.0, 0.75, 0.25, 1)
  end

  if state == "complete" then
    local player = S.rollingStartCars[0]
    if player and greenRecently(player, sim) then
      return "GREEN — GO", rgbm(0.25, 1.0, 0.35, 1)
    end
    return nil, nil
  end

  if state == "unavailable" then
    return "ROLLING START UNAVAILABLE", rgbm(1.0, 0.55, 0.25, 1)
  end

  if state == "rolling" then
    return rollingFormationDisplay(sim)
  end

  return nil, nil
end

local function fitBigSize(layout, bigText)
  local panelW, panelH = layout.panelW, layout.panelH
  local bigSize = math.max(12, math.floor(panelH * 0.5))
  local bigW = MeasureBoldText(bigText, bigSize).x
  if bigW > panelW * 0.9 then
    bigSize = math.max(12, math.floor(bigSize * (panelW * 0.9) / bigW))
  end
  local bigH = MeasureBoldText(bigText, bigSize).y
  local bigMaxHalf = panelH * 0.22
  if bigH / 2 > bigMaxHalf then
    bigSize = math.max(12, math.floor(bigSize * bigMaxHalf / (bigH / 2)))
  end
  return bigSize
end

local function drawPanel(layout, x, title, bigText, totalText, color)
  local panelW, panelTop, panelH, columnBottom = layout.panelW, layout.panelTop, layout.panelH, layout.columnBottom
  local cx = x + panelW / 2

  ui.drawRectFilled(vec2(x, panelTop), vec2(x + panelW, columnBottom), rgbm(0.08, 0.10, 0.15, 0.6), 8)
  ui.drawRect(vec2(x, panelTop), vec2(x + panelW, columnBottom), rgbm(0.3, 0.3, 0.36, 0.7), 8, nil, 1.5)

  DrawBoldText(title, cx, panelTop + 8 + MeasureBoldText(title, 13).y / 2, 13, rgbm(0.7, 0.7, 0.8, 1))

  local bigCy = panelTop + panelH * 0.42
  local bigSize = fitBigSize(layout, bigText)
  local drawnBigH = DrawBoldText(bigText, cx, bigCy, bigSize, color)

  local sepW = panelW * 0.5
  local sepY = bigCy + drawnBigH / 2 + 10
  ui.drawRectFilled(vec2(cx - sepW / 2, sepY), vec2(cx + sepW / 2, sepY + 2), rgbm(0.5, 0.5, 0.6, 0.9))

  if totalText then
    local totalSize = MeasureBoldText(totalText, 16)
    local totalCy = math.min(sepY + 8 + totalSize.y / 2, columnBottom - totalSize.y / 2 - 8)
    DrawBoldText(totalText, cx, totalCy, 16, rgbm(0.7, 0.7, 0.8, 1))
  end
end

local function computeLayout()
  local w = ui.windowWidth()
  local h = ui.windowHeight()
  local layout = { w = w, h = h, padX = 12, gap = 16, panelTop = 8, gapPanelH = 46 }
  layout.panelW = (w - 2 * layout.padX - 2 * layout.gap) / 3
  layout.gapPanelTop = h - 8 - layout.gapPanelH
  layout.columnBottom = layout.gapPanelTop - 10
  layout.panelH = layout.columnBottom - layout.panelTop
  return layout
end

local function drawTopPanels(layout, data)
  local padX, panelW, gap = layout.padX, layout.panelW, layout.gap
  local lapTotalText = (data.totalLaps or 0) > 0 and sFormat("%d", data.totalLaps) or nil
  local overallText = data.overallPos and sFormat("%d", data.overallPos) or "-"
  local classText = data.classPos and sFormat("%d", data.classPos) or "-"
  -- The formation lap is not counted: the panel reads "-" until the player crosses the line for
  -- the first time, and the racing laps then start at 1.
  local lapText = data.formationLap and "-" or sFormat("%d", math.max(1, data.currentLap))
  drawPanel(layout, padX, "LAP", lapText, lapTotalText, rgbm(0.95, 0.95, 1, 1))
  drawPanel(layout, padX + panelW + gap, "OVERALL", overallText, sFormat("%d", data.totalCars), rgbm(0.95, 0.95, 1, 1))
  if data.classTotal > 0 then
    drawPanel(layout, padX + 2 * (panelW + gap), "CLASS", classText, sFormat("%d", data.classTotal), rgbm(0.35, 0.95, 0.45, 1))
  else
    drawPanel(layout, padX + 2 * (panelW + gap), "CLASS", "-", "add a class", rgbm(0.55, 0.55, 0.6, 1))
  end
end

local YELLOW_FLAG_FILL = rgbm(252 / 255, 221 / 255, 9 / 255, 1)
local YELLOW_FLAG_BORDER = rgbm(1.0, 0.93, 0.45, 1)
local YELLOW_FLAG_TEXT = rgbm(0, 0, 0, 1)
local YELLOW_BLINK_HALF_SECONDS = 0.5

-- The yellow panel blinks: solid yellow with black text, then dark with yellow text.
local function yellowBlinkOn()
  return math.floor((S.yellowClock or 0) / YELLOW_BLINK_HALF_SECONDS) % 2 == 0
end
local GREEN_FLAG_FILL =rgbm(7 / 255, 137 / 255, 48 / 255, 1)
local GREEN_FLAG_BORDER = rgbm(0.30, 0.72, 0.42, 1)
local GREEN_FLAG_TEXT = rgbm(1, 1, 1, 1)

local function drawBottomPanelBox(layout, flag)
  local gapPanelRight = layout.w - layout.padX
  local fill = rgbm(0.08, 0.10, 0.15, 0.6)
  local border = rgbm(0.3, 0.3, 0.36, 0.7)
  if flag == "yellow" then
    if yellowBlinkOn() then
      fill, border = YELLOW_FLAG_FILL, YELLOW_FLAG_BORDER
    else
      border = YELLOW_FLAG_FILL
    end
  elseif flag == "green" then
    fill, border = GREEN_FLAG_FILL, GREEN_FLAG_BORDER
  end
  ui.drawRectFilled(vec2(layout.padX, layout.gapPanelTop), vec2(gapPanelRight, layout.h - 8), fill, 8)
  ui.drawRect(vec2(layout.padX, layout.gapPanelTop), vec2(gapPanelRight, layout.h - 8), border, 8, nil, 1.5)
end

local function drawRollingText(layout, text, color, flag)
  local w, padX = layout.w, layout.padX
  drawBottomPanelBox(layout, flag)
  if flag == "yellow" then
    color = yellowBlinkOn() and YELLOW_FLAG_TEXT or YELLOW_FLAG_FILL
  elseif flag == "green" then color = GREEN_FLAG_TEXT end
  local size = MeasureBoldText(text, 17)
  local fontSize = size.x > (w - 2 * padX - 24)
      and math.max(12, math.floor(17 * (w - 2 * padX - 24) / size.x))
      or 17
  DrawBoldText(text, w / 2, layout.gapPanelTop + layout.gapPanelH / 2, fontSize, color)
end

local function drawQualiBest(layout, data)
  local w, padX = layout.w, layout.padX
  local posY = layout.gapPanelTop + layout.gapPanelH / 2
  drawBottomPanelBox(layout)

  local fastLap = data.classFastestMs and U.FormatLapMs(data.classFastestMs) or "--:--.---"
  local myLap = data.playerBestMs and U.FormatLapMs(data.playerBestMs) or "--:--.---"

  local maxW = w - 2 * padX - 24
  local gFs = 15
  local fastRow = "FASTEST  " .. fastLap
  local myRow = "YOU      " .. myLap
  local need = math.max(MeasureBoldText(fastRow, gFs).x, MeasureBoldText(myRow, gFs).x)
  if need > maxW then
    gFs = math.max(12, math.floor(gFs * maxW / need))
  end

  DrawBoldText(fastRow, w / 2, posY - (math.floor(gFs / 2) + 1), gFs, rgbm(0.35, 0.95, 0.45, 1))
  DrawBoldText(myRow, w / 2, posY + (math.floor(gFs / 2) + 1), gFs, rgbm(0.95, 0.95, 1, 1))
end

local function formatGapSeconds(seconds, bestLapMs)
  seconds = math.abs(seconds or 0)
  -- A gap bigger than the human car's best lap is no longer meaningful in seconds: show
  -- whole laps instead (1 LAP, 2 LAPS, ...). Floor, so the display never claims a lap the
  -- car has not completed yet.
  if bestLapMs and bestLapMs > 0 then
    local bestLapSec = bestLapMs / 1000.0
    if bestLapSec > 0 and seconds >= bestLapSec then
      local laps = math.max(1, math.floor(seconds / bestLapSec))
      if laps == 1 then
        return "1 LAP"
      else
        return sFormat("%d LAPS", laps)
      end
    end
  end
  if seconds >= 60 then
    local mins = math.floor(seconds / 60)
    local secs = math.floor(seconds % 60 + 0.5)
    if secs == 60 then
      mins = mins + 1
      secs = 0
    end
    return sFormat("%dm%02ds", mins, secs)
  end
  if seconds >= 10 then
    return sFormat("%.0fs", seconds)
  end
  return sFormat("%.1fs", seconds)
end

local function buildGapText(sim, data)
  local gapStr = "--"
  local gapColor = rgbm(0.55, 0.55, 0.6, 0.9)
  if not sim.isSessionStarted then return gapStr, gapColor end
  -- The labels read the other way round to the usual convention: "-" for the car ahead (time to
  -- make up) and "+" for the car behind. The position (ahead first) is unchanged. Cars sitting in
  -- the pits are skipped when the gaps are computed, so a pitted car never shows up here.
  local gapFront, gapBehind = data.gapFront, data.gapBehind
  if data.stopped then
    gapStr = "Car Stopped"
    gapColor = rgbm(1.0, 0.7, 0.35, 1)
  else
    local playerBest = Positions.GetCarBestLapMs(0)
    local frontStr = gapFront and ("-" .. formatGapSeconds(math.abs(gapFront), playerBest))
    local behindStr = gapBehind and ("+" .. formatGapSeconds(math.abs(gapBehind), playerBest))
    if frontStr and behindStr then
      gapStr = frontStr .. " / " .. behindStr
    else
      gapStr = frontStr or behindStr or gapStr
    end
  end
  return gapStr, gapColor
end

local function drawGapPanel(layout, sim, data)
  local w, padX = layout.w, layout.padX
  local gapStr, gapColor = buildGapText(sim, data)
  drawBottomPanelBox(layout)

  local gFs = 20
  local gSize = MeasureBoldText(gapStr, gFs)
  if gSize.x > (w - 2 * padX - 24) then
    gFs = math.max(12, math.floor(gFs * (w - 2 * padX - 24) / gSize.x))
  end
  DrawBoldText(gapStr, w / 2, layout.gapPanelTop + layout.gapPanelH / 2, gFs, gapColor)
end

local function collectData(qualiMode)
  local data = {}
  if qualiMode then
    data.overallPos, data.totalCars, data.classPos, data.classTotal, data.classFastestMs, data.playerBestMs = Positions.GetPlayerPositionsQuali()
    data.currentLap, data.totalLaps, data.stopped, data.formationLap = Positions.GetCurrentLapInfo()
  else
    data.overallPos, data.totalCars, data.classPos, data.classTotal, data.gapFront, data.gapBehind,
      data.currentLap, data.totalLaps, data.stopped, data.formationLap = Positions.GetPlayerPositions()
  end
  return data
end

function M.Leaderboard(dt)
  -- The whole app is off while "Enable Multiple Class Race" is unchecked.
  if not S.enduranceEnabled then return end
  local sim = ac.getSim()
  local layout = computeLayout()

  local qualiMode = Sess.IsQualiPracticeMode()
  local rollingText, rollingColor = M.GetRollingStartDisplay(sim)
  local data = collectData(qualiMode)

  -- During the rolling formation the engine classification is not meaningful yet, so hold the
  -- positions until every class has gone green (rollingStartState == "complete").
  local rollingActive = (S.rollingStartState == "staged" or S.rollingStartState == "rolling")
  if rollingActive then
    data.overallPos, data.classPos, data.gapFront, data.gapBehind = nil, nil, nil, nil
  end

  drawTopPanels(layout, data)

  if rollingText then
    local flag = nil
    if not rollingActive then
      if S.yellowPlayerActive == true then
        flag = "yellow"
      elseif (S.yellowGreenUntil or 0) > (S.yellowClock or 0) then
        flag = "green"
      end
    end
    drawRollingText(layout, rollingText, rollingColor, flag)
  elseif qualiMode then
    drawQualiBest(layout, data)
  elseif data.classTotal > 1 then
    drawGapPanel(layout, sim, data)
  end
end

return M
