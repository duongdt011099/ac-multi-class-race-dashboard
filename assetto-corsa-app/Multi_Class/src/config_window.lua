-- Configuration window (script.clConfig body).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local Classes = require("src/classes/manager")
local Writer = require("src/export/writer")
local Exporter = require("src/export/exporter")
local Audio = require("src/audio")
local RCtl = require("src/rolling/control")

local Config = {}

local sFormat = string.format
local colX = 24
local MUTED = rgbm(0.6, 0.6, 0.7, 1)

local Draw = require("src/leaderboard/draw")

local CARD_MARGIN = 24      -- gap between the window edge and the cards (the panel padding)
local CARD_PAD_TOP = 34     -- room for the section title inside a card
-- Dashboard palette: near-black surfaces, hairline borders and a single red accent.
local ACCENT_BLUE = rgbm(1.0, 0.18, 0.14, 1)   -- the dashboard red (name kept; it is the main accent)
local SURFACE = rgbm(0.067, 0.067, 0.082, 1)
local SURFACE_BORDER = rgbm(0.165, 0.165, 0.196, 1)
local TITLE_TEXT = rgbm(0.78, 0.78, 0.81, 1)

-- Card heights measured on the previous frame (a card is drawn before its content, so it needs
-- last frame's height; the one-frame lag is not visible).
local sectionHeights = {}

local function drawBackgroundAndTitle(w, h)
  -- Main panel: square, inset from the window edge, with a hairline border.
  ui.drawRectFilled(vec2(6, 6), vec2(w - 6, h - 6), rgbm(0.027, 0.027, 0.031, 0.97), 0)
  ui.drawRect(vec2(6, 6), vec2(w - 6, h - 6), SURFACE_BORDER, 0, nil, 1)

  ui.pushFont(ui.Font.Title)
  local titleText = "Multiple Class Race"
  local titleSize = ui.measureText(titleText)
  local titleX = (w - titleSize.x) / 2
  ui.setCursor(vec2(titleX, 26))
  ui.text(titleText)
  ui.popFont()

  local subY = ui.getCursor().y + 2
  ui.pushFont(ui.Font.Small)
  local subText = "CONFIGURATION"
  local subSize = ui.measureText(subText)
  ui.setCursor(vec2((w - subSize.x) / 2, subY))
  ui.textColored(subText, Draw.WithAlpha(ACCENT_BLUE, 0.9))
  ui.popFont()

  local barY = ui.getCursor().y + 8
  ui.drawRectFilled(vec2(w / 2 - 24, barY), vec2(w / 2 + 24, barY + 3), ACCENT_BLUE, 2)
  return barY + 20
end

-- Grid layout for the current frame: the cards are laid out in rows of `cols` cards, and every
-- card of a row is as tall as the tallest card in that row.
local layout = { cols = 1, cardW = 0, baseX = 0 }
local MIN_CARD_W = 300
local COL_GAP = 14
local ROW_GAP = 14

local function beginColumns(w)
  local cols = 1
  layout.cols = cols
  layout.cardW = (w - 2 * CARD_MARGIN - (layout.cols - 1) * COL_GAP) / layout.cols
end

-- Draws one titled card (x1..x2, y0..y0+height) around the content produced by fn, and returns the
-- height its content actually needed.
local function drawSection(sec, x1, y0, height)
  local x2 = x1 + layout.cardW
  Draw.DrawCard(x1, y0, x2, y0 + height, SURFACE)
  ui.drawRect(vec2(x1, y0), vec2(x2, y0 + height), SURFACE_BORDER, 6, nil, 1)
  -- Accent strip across the top edge, like the dashboard cards.
  ui.drawRectFilled(vec2(x1, y0), vec2(x2, y0 + 3), ACCENT_BLUE, 6, ui.CornerFlags and ui.CornerFlags.Top)

  colX = x1 + 12
  ui.pushFont(ui.Font.Small)
  ui.setCursor(vec2(colX + 2, y0 + 12))
  ui.textColored(string.upper(sec.title), TITLE_TEXT)
  ui.popFont()
  ui.drawLine(vec2(x1 + 12, y0 + 31), vec2(x2 - 12, y0 + 31), SURFACE_BORDER, 1)

  -- Line breaks inside the card return to the indent, so move it to this column.
  local indent = colX - layout.baseX
  if indent > 0 then ui.indent(indent) else indent = 0 end
  ui.setCursor(vec2(colX, y0 + CARD_PAD_TOP + 4))
  local wrapped = type(ui.pushTextWrapPos) == "function" and type(ui.popTextWrapPos) == "function"
  if wrapped then ui.pushTextWrapPos(x2 - 12) end
  layout.innerW = layout.cardW - 24
  sec.fn(layout.cardW + 24)   -- the section functions size full-width controls as (w - 48)
  if wrapped then ui.popTextWrapPos() end
  if indent > 0 then ui.unindent(indent) end

  local used = ui.getCursor().y - y0
  sectionHeights[sec.key] = used
  return used
end

-- Lays the sections out in rows. A card is drawn before its content, so the shared row height comes
-- from the heights measured on the previous frame. Returns the y below the last row.
local function drawSections(sections, top)
  local y = top
  local i = 1
  while i <= #sections do
    local last = math.min(#sections, i + layout.cols - 1)
    local rowHeight = 80
    for j = i, last do
      rowHeight = math.max(rowHeight, sectionHeights[sections[j].key] or 0)
    end
    local measured = 0
    for j = i, last do
      local x1 = CARD_MARGIN + (j - i) * (layout.cardW + COL_GAP)
      measured = math.max(measured, drawSection(sections[j], x1, y, rowHeight))
    end
    y = y + math.max(rowHeight, measured) + ROW_GAP
    i = last + 1
  end
  return y
end

-- Splits text into lines no wider than maxW, breaking at spaces and, for a word that is too long on
-- its own (such as a path), inside the word. Measured with the current font.
local function wrapLines(text, maxW)
  local lines, line = {}, ""
  local function flush() if line ~= "" then lines[#lines + 1] = line; line = "" end end
  for word in tostring(text):gmatch("%S+") do
    local candidate = (line == "") and word or (line .. " " .. word)
    if ui.measureText(candidate).x <= maxW then
      line = candidate
    else
      flush()
      while ui.measureText(word).x > maxW and #word > 1 do
        local cut = #word - 1
        while cut > 1 and ui.measureText(word:sub(1, cut)).x > maxW do cut = cut - 1 end
        lines[#lines + 1] = word:sub(1, cut)
        word = word:sub(cut + 1)
      end
      line = word
    end
  end
  flush()
  if #lines == 0 then lines[1] = "" end
  return lines
end

-- Checkbox whose label wraps inside the card instead of running past its edge. Returns true when
-- the box (or its label) was clicked, like ui.checkbox. The label is wrapped by hand: the later
-- lines line up under the first one, next to the box.
local function checkRow(label, value)
  local clicked = ui.checkbox("##" .. label, value)
  ui.sameLine()
  local textX = ui.getCursor().x
  local maxW = math.max(60, (layout.innerW or 240) - (textX - colX))
  local lines = wrapLines(label, maxW)
  ui.text(lines[1])
  if type(ui.itemClicked) == "function" and ui.itemClicked() then clicked = true end
  for i = 2, #lines do
    ui.setCursor(vec2(textX, ui.getCursor().y))
    ui.text(lines[i])
  end
  return clicked
end

-- Rounded, softly coloured widgets. Returns how many colours / vars were pushed.
local function pushModernStyle()
  local colours = {
    { "FrameBg", rgbm(1, 1, 1, 0.06) }, { "FrameBgHovered", rgbm(1, 1, 1, 0.12) },
    { "FrameBgActive", rgbm(1, 1, 1, 0.16) },
    { "Button", rgbm(1.0, 0.18, 0.14, 0.30) }, { "ButtonHovered", rgbm(1.0, 0.18, 0.14, 0.55) },
    { "ButtonActive", rgbm(1.0, 0.18, 0.14, 0.80) },
    { "CheckMark", ACCENT_BLUE },
    { "Header", rgbm(1.0, 0.18, 0.14, 0.25) }, { "HeaderHovered", rgbm(1.0, 0.18, 0.14, 0.40) },
  }
  local pushed = 0
  for _, entry in ipairs(colours) do
    local id = ui.StyleColor and ui.StyleColor[entry[1]]
    if id ~= nil then
      ui.pushStyleColor(id, entry[2])
      pushed = pushed + 1
    end
  end
  ui.pushStyleVar(ui.StyleVar.FrameRounding, 8)
  return pushed
end

-- Draws text wrapped to the card's inner width. Breaks at spaces, and inside a word that is too
-- long on its own (such as a folder path), so nothing runs into the next column.
local function drawWrapped(text, color)
  for _, l in ipairs(wrapLines(text, layout.innerW or 240)) do
    if color then ui.textColored(l, color) else ui.text(l) end
  end
end

-- Shortens the middle of a path ("D:\Steam\...\Multi_Class") so the drive and the last folders
-- both stay visible.
local function middleEllipsize(text, maxWidth)
  if ui.measureText(text).x <= maxWidth then return text end
  local function build(n)
    local head = math.ceil(n * 0.4)
    return text:sub(1, head) .. "..." .. text:sub(#text - (n - head) + 1)
  end
  local lo, hi = 1, #text - 1
  while lo < hi do
    local mid = math.ceil((lo + hi) / 2)
    if ui.measureText(build(mid)).x <= maxWidth then lo = mid else hi = mid - 1 end
  end
  return build(lo)
end

-- One-line path field: subtle rounded box, middle-ellipsised, full path in the tooltip.
local function drawPathField(path, color, tooltip)
  local innerW = layout.innerW or 240
  local shown = middleEllipsize(path, innerW - 16)
  local pos = ui.getCursor()
  local th = ui.measureText(shown).y
  ui.drawRectFilled(vec2(colX, pos.y), vec2(colX + innerW, pos.y + th + 10), rgbm(1, 1, 1, 0.06), 6)
  ui.setCursor(vec2(colX + 8, pos.y + 5))
  ui.textColored(shown, color or rgbm(0.92, 0.94, 1, 1))
  if ui.itemHovered() then ui.setTooltip(tooltip and (path .. "\n\n" .. tooltip) or path) end
  ui.setCursor(vec2(colX, pos.y + th + 14))
end

local function drawMusicSection(w)
  if checkRow("Play music when you enter double file (rolling start)", S.rollingMusicEnabled) then
    S.rollingMusicEnabled = not S.rollingMusicEnabled
    S.storedSettings.rollingMusicEnabled = S.rollingMusicEnabled
    if not S.rollingMusicEnabled then Audio.Reset() end
  end
  S.rollingMusicVolume = C.AUDIO_VOLUME
  S.storedSettings.rollingMusicVolume = C.AUDIO_VOLUME
  ui.dummy(vec2(0, 8))
end

local function drawAutopilotSection(w)
  if checkRow("Autopilot for my car during the formation lap", S.autopilotEnabled) then
    S.autopilotEnabled = not S.autopilotEnabled
    S.storedSettings.autopilotEnabled = S.autopilotEnabled
    if not S.autopilotEnabled then RCtl.SetPlayerAutopilot(false) end
  end
  if ui.itemHovered() then
    ui.setTooltip("Autopilot drives your car through the formation lap until 300 m before the start line. You take over manually from there.")
  end
  ui.dummy(vec2(0, 8))
end

local mapPickerColor = rgbm(1, 1, 1, 1)

local function drawGeneralSection(w)
  if checkRow("Close the game 5 seconds after a session result is saved", S.autoExitAfterRace) then
    S.autoExitAfterRace = not S.autoExitAfterRace
    S.storedSettings.autoExitAfterRace = S.autoExitAfterRace
    if not S.autoExitAfterRace then S.raceExitAt = nil end
  end
  if ui.itemHovered() then
    ui.setTooltip("After a race, qualifying or practice session finishes and its result is saved, a banner counts down and Assetto Corsa closes.")
  end
  if checkRow("Show leaderboard", S.showLeaderboard) then
    S.showLeaderboard = not S.showLeaderboard
    S.storedSettings.showLeaderboard = S.showLeaderboard
    ac.setWindowOpen("cl_leaderboard", S.showLeaderboard and not ac.getSim().isInMainMenu)
  end
  if S.showLeaderboard then
    ui.indent(18)
    if ui.radioButton("Extended mode", S.leaderboardExtended) then
      S.leaderboardExtended = true
      S.storedSettings.leaderboardExtended = true
    end
    if ui.itemHovered() then
      ui.setTooltip("Your class only: the leader and the cars around you, with time gap and fuel.")
    end
    if ui.radioButton("Simple mode", not S.leaderboardExtended) then
      S.leaderboardExtended = false
      S.storedSettings.leaderboardExtended = false
    end
    if ui.itemHovered() then
      ui.setTooltip("Lap, overall and class position panels with the gap to the cars next to you.")
    end
    if not S.leaderboardExtended then
      if checkRow("Show position / lap on the leaderboard", S.showPositionLap) then
        S.showPositionLap = not S.showPositionLap
        S.storedSettings.showPositionLap = S.showPositionLap
      end
      if checkRow("Show time gap on the leaderboard", S.showTimeGap) then
        S.showTimeGap = not S.showTimeGap
        S.storedSettings.showTimeGap = S.showTimeGap
      end
      if ui.itemHovered() then
        ui.setTooltip("When both are unchecked the leaderboard only shows the race time.")
      end
    end
    ui.unindent(18)
  end
  if checkRow("Display track map", S.showMap) then
    S.showMap = not S.showMap
    S.storedSettings.showMap = S.showMap
    ac.setWindowOpen("cl_trackmap", S.showMap and not ac.getSim().isInMainMenu)
  end
  if ui.itemHovered() then
    ui.setTooltip("Shows a real-time track map with every car in its class color and class position.")
  end
  if S.showMap then
    ui.indent(18)
    ui.dummy(vec2(0, 10))
    ui.text("My car color")
    ui.sameLine()
    local shown = S.humanCarColor
    if not shown then
      shown = require("src/map/roster").PlayerColor(os.clock()) or rgbm(1, 1, 1, 1)
    end
    -- Clicking the swatch opens the colour picker as a popup.
    ui.colorButton("##humancarcolor", rgbm(shown.r, shown.g, shown.b, 1), ui.ColorPickerFlags.NoPicker)
    if ui.itemClicked() then
      mapPickerColor = rgbm(shown.r, shown.g, shown.b, 1)
      -- Popup above the config window; it closes when you click outside of it.
      ui.popup(function()
        local flags = ui.ColorPickerFlags.NoAlpha + ui.ColorPickerFlags.NoSidePreview
            + ui.ColorPickerFlags.PickerHueBar + ui.ColorPickerFlags.DisplayHex
        if ui.colorPicker("##humancarpicker", mapPickerColor, flags) then
          S.humanCarColor = rgbm(mapPickerColor.r, mapPickerColor.g, mapPickerColor.b, 1)
          S.storedSettings.humanCarColor = string.format("#%02x%02x%02x",
            math.floor(mapPickerColor.r * 255 + 0.5), math.floor(mapPickerColor.g * 255 + 0.5),
            math.floor(mapPickerColor.b * 255 + 0.5))
        end
        -- Also here: a click outside the popup only closes it, so the button below it needs two clicks.
        if S.humanCarColor and ui.button("Use class color") then
          S.humanCarColor = nil
          S.storedSettings.humanCarColor = ""
          ui.closePopup()
        end
      end, { padding = vec2(10, 10) })
    end
    if ui.itemHovered() then ui.setTooltip("Click to choose the color of your own car on the track map.") end
    if S.humanCarColor then
      ui.sameLine()
      if ui.button("Use class color") then
        S.humanCarColor = nil
        S.storedSettings.humanCarColor = ""
      end
    end
    ui.unindent(18)
  end
  ui.dummy(vec2(0, 8))
end

local function drawYellowFlagSection(w)
  if checkRow("Full course yellow when a car stops on track", S.yellowFlagEnabled) then
    S.yellowFlagEnabled = not S.yellowFlagEnabled
    S.storedSettings.yellowFlagEnabled = S.yellowFlagEnabled
  end
  if checkRow("Blue flag lights when you are lapping a car", S.blueFlagEnabled) then
    S.blueFlagEnabled = not S.blueFlagEnabled
    S.storedSettings.blueFlagEnabled = S.blueFlagEnabled
  end
  if checkRow("Play yellow / green flag sounds", S.flagSoundEnabled) then
    S.flagSoundEnabled = not S.flagSoundEnabled
    S.storedSettings.flagSoundEnabled = S.flagSoundEnabled
    if not S.flagSoundEnabled then Audio.ResetFlags() end
  end
  S.flagSoundVolume = C.AUDIO_VOLUME
  S.storedSettings.flagSoundVolume = C.AUDIO_VOLUME
  ui.dummy(vec2(0, 8))
end

-- The classes come from the dashboard's session file, so this is a read-only summary.
local function drawClassRow(ci)
  local clsName = S.releaseClasses[ci]
  local cars = {}
  for i = 0, S.driverCount - 1 do
    if S.manualDriverClass[i] == clsName then
      local car = ac.getCarName and ac.getCarName(i) or sFormat("Car %d", i + 1)
      local okDriver, driver = pcall(function() return ac.getDriverName and ac.getDriverName(i) end)
      cars[#cars + 1] = (okDriver and type(driver) == "string" and driver ~= "") and (driver .. " - " .. car) or car
    end
  end
  ui.setCursor(vec2(colX, ui.getCursor().y + 6))
  drawWrapped(sFormat("%s (%d car%s)", clsName, #cars, #cars == 1 and "" or "s"))
  if #cars > 0 and ui.itemHovered() then
    ui.setTooltip(table.concat(cars, "\n"))
  end
end

local function drawClassList()
  if #S.releaseClasses > 0 then
    ui.setCursor(vec2(colX, ui.getCursor().y + 4))

    for ci = 1, #S.releaseClasses do
      drawClassRow(ci)
    end
  else
    ui.setCursor(vec2(colX, ui.getCursor().y))
    ui.pushFont(ui.Font.Small)
    drawWrapped("No classes loaded. Start the session from the dashboard so it can send the cars and classes.", rgbm(0.9, 0.7, 0.3, 1))
    ui.popFont()
  end
end

local function drawEnduranceSection(w)
  do
    if S.driverCount > 0 then
      drawClassList()
    else
      ui.setCursor(vec2(colX, ui.getCursor().y))
      ui.pushFont(ui.Font.Small)
      ui.textColored("Waiting for car data to load...", rgbm(0.6, 0.7, 0.9, 1))
      ui.popFont()
    end

    ui.dummy(vec2(0, 8))
  end
end

local function drawUnsupportedMessage(w, err)
  ui.setCursor(vec2(colX, ui.getCursor().y))
  ui.pushItemWidth(w - 48)
  drawWrapped(err or "Class-based mode cannot be enabled under current conditions!")
  ui.popItemWidth()
  ui.dummy(vec2(0, 8))
end

-- Outside the dashboard the app has nothing to configure: no controls, only a large centered notice.
local function drawNotFromDashboard(w, h)
  ui.drawRectFilled(vec2(6, 6), vec2(w - 6, h - 6), rgbm(0.027, 0.027, 0.031, 0.97), 0)
  ui.drawRect(vec2(6, 6), vec2(w - 6, h - 6), SURFACE_BORDER, 0, nil, 1)

  ui.pushFont(ui.Font.Title)
  local lines = wrapLines(Sess.NOT_FROM_DASHBOARD_MESSAGE, w - 64)
  local lineH = ui.measureText("Ag").y + 4
  local y = math.max(24, (h - #lines * lineH) / 2)
  for _, line in ipairs(lines) do
    ui.setCursor(vec2((w - ui.measureText(line).x) / 2, y))
    ui.textColored(line, rgbm(0.95, 0.55, 0.3, 1))
    y = y + lineH
  end
  ui.popFont()

  ui.setCursor(vec2(layout.baseX, y))
  ui.dummy(vec2(1, math.max(0, 200 - y)))
end

function Config.Draw()
  local valid, err = Sess.CheckSessionValidity()
  local sim = ac.getSim()
  local w = ui.windowWidth()
  local h = ui.windowHeight()

  layout.baseX = ui.getCursor().x   -- the window's default line-start x

  if not Sess.IsDashboardLaunch() then
    drawNotFromDashboard(w, h)
    return
  end

  ui.pushStyleVar(ui.StyleVar.FramePadding, vec2(6, 6))
  local pushedColours = pushModernStyle()

  local top = drawBackgroundAndTitle(w, h)
  beginColumns(w)

  local sections = { { key = "race", title = "Classes and cars", fn = drawEnduranceSection } }
  if S.enduranceEnabled then
    sections[#sections + 1] = { key = "general", title = "General", fn = drawGeneralSection }
    sections[#sections + 1] = { key = "music", title = "Music", fn = drawMusicSection }
    sections[#sections + 1] = { key = "autopilot", title = "Autopilot", fn = drawAutopilotSection }
    sections[#sections + 1] = { key = "flags", title = "Flags", fn = drawYellowFlagSection }
    if not valid then
      sections[#sections + 1] = { key = "notice", title = "Notice",
        fn = function(width) drawUnsupportedMessage(width, err) end }
    end
  end

  -- Reserve the room below the last row so the auto-sized window includes it.
  local bottom = drawSections(sections, top)
  ui.setCursor(vec2(layout.baseX, bottom))
  ui.dummy(vec2(1, 14))

  ui.popStyleVar(2)   -- FrameRounding + FramePadding
  if pushedColours > 0 then ui.popStyleColor(pushedColours) end
end

return Config
