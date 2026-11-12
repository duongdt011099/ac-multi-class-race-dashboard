-- Leaderboard window drawing (lap / overall / class panels and the bottom status panel).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local Positions = require("src/leaderboard/positions")
local TimedRace = require("src/timed_race")

local M = {}

local sFormat = string.format

local hasDWrite = (type(ui.pushDWriteFont) == "function" and type(ui.dwriteText) == "function")

-- Bahnschrift (shipped with Windows 10+) gives the numbers a clean, modern DIN look; labels use
-- Segoe UI Semibold. DirectWrite falls back to its default face if a family is missing.
local FONTS = {
  num = "Bahnschrift;Weight=SemiBold",
  label = "Segoe UI;Weight=SemiBold",
}

local function fontFor(name)
  return FONTS[name or "num"] or FONTS.num
end

function M.MeasureBoldText(text, fontSize, fontName)
  if hasDWrite then
    ui.pushDWriteFont(fontFor(fontName))
    local s = ui.measureDWriteText(text, fontSize)
    ui.popDWriteFont()
    return s
  end
  return ui.measureText(text)
end

function M.DrawBoldText(text, centerX, centerY, fontSize, color, fontName)
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
  ui.pushDWriteFont(fontFor(fontName))
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
  return phaseText, rgbm(1.0, 0.75, 0.25, 1)
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

-- Shared flat-card look: solid dark body with small corner radius, no gradient, glow or border.
local CARD_FILL = rgbm(0.09, 0.10, 0.12, 0.92)
local CARD_RADIUS = 6
local DIM_TEXT = rgbm(0.58, 0.62, 0.70, 1)

-- An outline is drawn only when asked for (the blinking yellow flag).
local function drawCard(x1, y1, x2, y2, fill, border)
  ui.drawRectFilled(vec2(x1, y1), vec2(x2, y2), fill or CARD_FILL, CARD_RADIUS)
  if border then
    ui.drawRect(vec2(x1, y1), vec2(x2, y2), border, CARD_RADIUS, nil, 2)
  end
end

local function withAlpha(color, a)
  return rgbm(color.r, color.g, color.b, a)
end

-- Shared with the configuration window so both use the same card styling.
M.DrawCard = drawCard
M.WithAlpha = withAlpha

local function drawPanel(layout, x, title, bigText, totalText, color, accent)
  local panelW, panelTop, panelH, columnBottom = layout.panelW, layout.panelTop, layout.panelH, layout.columnBottom
  local cx = x + panelW / 2
  accent = accent or color

  drawCard(x, panelTop, x + panelW, columnBottom)
  -- Flat accent strip across the top edge of the card.
  local topFlag = ui.CornerFlags and ui.CornerFlags.Top
  ui.drawRectFilled(vec2(x, panelTop), vec2(x + panelW, panelTop + 3), accent, CARD_RADIUS, topFlag)

  DrawBoldText(title, cx, panelTop + 20 + MeasureBoldText(title, 12, "label").y / 2, 12, accent, "label")

  local bigCy = panelTop + panelH * 0.50
  local bigSize = fitBigSize(layout, bigText)
  local drawnBigH = DrawBoldText(bigText, cx, bigCy, bigSize, color)

  if totalText then
    local label = totalText
    local totalSize = MeasureBoldText(label, 15, "label")
    local totalCy = math.min(bigCy + drawnBigH / 2 + 10 + totalSize.y / 2, columnBottom - totalSize.y / 2 - 8)
    DrawBoldText(label, cx, totalCy, 15, DIM_TEXT, "label")
  end
end

local function computeLayout()
  local w = ui.windowWidth()
  local h = ui.windowHeight()
  local layout = { w = w, h = h, padX = 12, gap = 16, panelTop = 8, gapPanelH = 46 }
  -- Timed race: a clock panel sits above the position panels, which move down to make room.
  layout.timePanelH = 46
  -- The clock panel is always there (race countdown, race elapsed time, or the practice / qualifying
  -- time left), so the position panels keep the same height in every session.
  layout.panelTop = 8 + layout.timePanelH + 10
  layout.panelW = (w - 2 * layout.padX - 2 * layout.gap) / 3
  layout.gapPanelTop = h - 8 - layout.gapPanelH
  -- Without the position panels, the bottom panel sits right under the clock instead.
  if not S.showPositionLap then
    layout.gapPanelTop = layout.panelTop
  end
  layout.columnBottom = layout.gapPanelTop - 10
  layout.panelH = layout.columnBottom - layout.panelTop
  return layout
end

local function drawTopPanels(layout, data)
  local padX, panelW, gap = layout.padX, layout.panelW, layout.gap
  local lapTotalText = (data.totalLaps or 0) > 0 and sFormat("%d", data.totalLaps) or nil
  -- Timed race: until the final lap is known the panel counts the race clock down instead.
  local overallText = data.overallPos and sFormat("%d", data.overallPos) or "-"
  local classText = data.classPos and sFormat("%d", data.classPos) or "-"
  -- The formation lap is not counted: the panel reads "-" until the player crosses the line for
  -- the first time, and the racing laps then start at 1.
  local lapText = data.formationLap and "-" or sFormat("%d", math.max(1, data.currentLap))
  -- A single class on the grid makes the CLASS panel redundant: LAP and POSITION share the width.
  local singleClass = (data.classTotal or 0) <= 1
  if singleClass then
    panelW = (layout.w - 2 * padX - gap) / 2
    layout = setmetatable({ panelW = panelW }, { __index = layout })
  end
  drawPanel(layout, padX, "LAP", lapText, lapTotalText, rgbm(0.95, 0.96, 1, 1), rgbm(0.40, 0.68, 1.0, 1))
  drawPanel(layout, padX + panelW + gap, singleClass and "POSITION" or "OVERALL", overallText,
      sFormat("%d", data.totalCars), rgbm(0.95, 0.96, 1, 1), rgbm(1.0, 0.72, 0.28, 1))
  if singleClass then return end
  if data.classTotal > 0 then
    drawPanel(layout, padX + 2 * (panelW + gap), "CLASS", classText, sFormat("%d", data.classTotal),
        rgbm(0.45, 1.0, 0.55, 1), rgbm(0.30, 0.90, 0.45, 1))
  else
    drawPanel(layout, padX + 2 * (panelW + gap), "CLASS", "-", "add a class",
        rgbm(0.55, 0.55, 0.6, 1), rgbm(0.45, 0.47, 0.55, 1))
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
  local fill, border = nil, nil
  if flag == "yellow" then
    if yellowBlinkOn() then
      fill = YELLOW_FLAG_FILL
    else
      border = YELLOW_FLAG_FILL
    end
  elseif flag == "green" then
    fill = GREEN_FLAG_FILL
  end
  local top = layout.gapPanelTop
  local bottom = top + layout.gapPanelH
  drawCard(layout.padX, top, gapPanelRight, bottom, fill, border)
  if not flag then
    -- Flat accent strip on the left edge for the neutral state.
    ui.drawRectFilled(vec2(layout.padX, top), vec2(layout.padX + 4, bottom), rgbm(0.40, 0.68, 1.0, 1),
        CARD_RADIUS, ui.CornerFlags and ui.CornerFlags.Left)
  end
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

-- Full-width clock panel above the LAP / OVERALL / CLASS panels: time left in a timed race or in
-- practice / qualifying, elapsed time in a lap race.
local function timePanelText(sim)
  if Sess.IsRaceMode(sim) then
    if TimedRace.IsTimedRace() then
      local color = S.timedRaceTimeUp and rgbm(1.0, 0.85, 0.25, 1) or rgbm(0.95, 0.96, 1, 1)
      return TimedRace.FormatClock(TimedRace.SecondsLeft()), color
    end
    return TimedRace.FormatClock(S.timedRaceSeconds or 0), rgbm(0.95, 0.96, 1, 1)
  end
  local ok, left = pcall(function() return sim.sessionTimeLeft end)
  local seconds = (ok and type(left) == "number" and left > 0) and left / 1000 or 0
  return TimedRace.FormatClock(seconds), rgbm(0.95, 0.96, 1, 1)
end

local function drawTimePanel(layout, sim)
  local x1, x2 = layout.padX, layout.w - layout.padX
  local y1, y2 = 8, 8 + layout.timePanelH
  local accent = rgbm(0.95, 0.40, 0.40, 1)
  drawCard(x1, y1, x2, y2)
  ui.drawRectFilled(vec2(x1, y1), vec2(x2, y1 + 3), accent, CARD_RADIUS, ui.CornerFlags and ui.CornerFlags.Top)

  local text, color = timePanelText(sim)
  local cy = (y1 + y2) / 2 + 1
  local cx = layout.w / 2
  DrawBoldText(text, cx, cy, 28, color)
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

  drawTimePanel(layout, sim)
  if S.showPositionLap then
    drawTopPanels(layout, data)
  end

  if not S.showTimeGap then
    -- Bottom panel fully hidden: no gap, rolling-start / flag message or qualifying best.
    return
  end

  if rollingText then
    local flag = nil
    if rollingText == "GREEN — GO" then
      flag = "green"
    elseif not rollingActive then
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
