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
local Detect = require("src/classes/detection")

local Config = {}

local sFormat = string.format
local colX = 24
local MUTED = rgbm(0.6, 0.6, 0.7, 1)

local Draw = require("src/leaderboard/draw")

local CARD_MARGIN = 24      -- gap between the window edge and the cards (the panel padding)
local CARD_PAD_TOP = 34     -- room for the section title inside a card
local ACCENT_BLUE = rgbm(0.40, 0.68, 1.0, 1)
local ACCENT_GREEN = rgbm(0.30, 0.90, 0.45, 1)
local ACCENT_AMBER = rgbm(1.0, 0.72, 0.28, 1)
local ACCENT_PURPLE = rgbm(0.72, 0.55, 1.0, 1)
local ACCENT_CYAN = rgbm(0.35, 0.90, 0.95, 1)
local ACCENT_RED = rgbm(1.0, 0.45, 0.40, 1)

-- Card heights measured on the previous frame (a card is drawn before its content, so it needs
-- last frame's height; the one-frame lag is not visible).
local sectionHeights = {}

local function drawBackgroundAndTitle(w, h)
  -- Main panel: square, inset from the window edge, with a hairline border.
  ui.drawRectFilled(vec2(6, 6), vec2(w - 6, h - 6), rgbm(0.03, 0.04, 0.06, 0.94), 0)
  ui.drawRect(vec2(6, 6), vec2(w - 6, h - 6), rgbm(1, 1, 1, 0.10), 0, nil, 1)

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

-- Column layout for the current frame: cards are dropped into the shortest column.
local layout = { cols = 1, cardW = 0, ys = {}, baseX = 0 }
local MIN_CARD_W = 300
local COL_GAP = 14

local function beginColumns(w, top)
  local cols = math.floor((w - 2 * CARD_MARGIN + COL_GAP) / (MIN_CARD_W + COL_GAP))
  layout.cols = math.max(1, math.min(3, cols))
  layout.cardW = (w - 2 * CARD_MARGIN - (layout.cols - 1) * COL_GAP) / layout.cols
  layout.ys = {}
  for c = 1, layout.cols do layout.ys[c] = top end
end

-- Draws one titled card around the content produced by fn.
-- matchKey (optional): never be shorter than that section, so side-by-side cards line up.
local function section(key, title, accent, fn, matchKey)
  local c = 1
  for i = 2, layout.cols do
    if layout.ys[i] < layout.ys[c] - 0.5 then c = i end
  end
  local y0 = layout.ys[c]
  local x1 = CARD_MARGIN + (c - 1) * (layout.cardW + COL_GAP)
  local x2 = x1 + layout.cardW
  local height = math.max(sectionHeights[key] or 80, matchKey and sectionHeights[matchKey] or 0)
  Draw.DrawCard(x1, y0, x2, y0 + height)

  colX = x1 + 12
  ui.pushFont(ui.Font.Small)
  ui.setCursor(vec2(colX + 2, y0 + 11))
  ui.textColored(title, Draw.WithAlpha(accent, 0.95))
  ui.popFont()

  -- Line breaks inside the card return to the indent, so move it to this column.
  local indent = colX - layout.baseX
  if indent > 0 then ui.indent(indent) else indent = 0 end
  ui.setCursor(vec2(colX, y0 + CARD_PAD_TOP))
  local wrapped = type(ui.pushTextWrapPos) == "function" and type(ui.popTextWrapPos) == "function"
  if wrapped then ui.pushTextWrapPos(x2 - 12) end
  layout.innerW = layout.cardW - 24
  fn(layout.cardW + 24)   -- the section functions size full-width controls as (w - 48)
  if wrapped then ui.popTextWrapPos() end
  if indent > 0 then ui.unindent(indent) end

  local used = ui.getCursor().y - y0
  sectionHeights[key] = used
  layout.ys[c] = y0 + math.max(used, matchKey and sectionHeights[matchKey] or 0) + 12
end

-- Checkbox whose label wraps inside the card instead of running past its edge. Returns true when
-- the box (or its label) was clicked, like ui.checkbox.
local function checkRow(label, value)
  local clicked = ui.checkbox("##" .. label, value)
  ui.sameLine()
  ui.textWrapped(label)
  if type(ui.itemClicked) == "function" and ui.itemClicked() then clicked = true end
  return clicked
end

-- Rounded, softly coloured widgets. Returns how many colours / vars were pushed.
local function pushModernStyle()
  local colours = {
    { "FrameBg", rgbm(1, 1, 1, 0.06) }, { "FrameBgHovered", rgbm(1, 1, 1, 0.12) },
    { "FrameBgActive", rgbm(1, 1, 1, 0.16) },
    { "Button", rgbm(0.40, 0.68, 1.0, 0.22) }, { "ButtonHovered", rgbm(0.40, 0.68, 1.0, 0.40) },
    { "ButtonActive", rgbm(0.40, 0.68, 1.0, 0.60) },
    { "CheckMark", ACCENT_BLUE },
    { "Header", rgbm(0.40, 0.68, 1.0, 0.25) }, { "HeaderHovered", rgbm(0.40, 0.68, 1.0, 0.40) },
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

local function onFolderChosen(err, path)
  if (not err or err == "") and path and path ~= "" then
    S.resultFolder = U.NormalizePath(path)
    S.storedSettings.resultFolder = S.resultFolder
    ui.toast(ui.Icons.Play, "Session result folder set")
  elseif err and err ~= "" then
    ui.toast(ui.Icons.Warning, "Error choosing folder: " .. err)
  end
end

local function openFolderDialog()
  if type(os.openFileDialog) ~= "function" then
    ui.toast(ui.Icons.Warning, "Folder dialog is not available on this CSP version.")
    return
  end
  local defaultFolder = ac.getFolder and ac.getFolder(ac.FolderID.Documents) or ""
  os.openFileDialog({
    title = "Choose save folder for session results",
    defaultFolder = defaultFolder,
    folder = (S.resultFolder ~= "") and S.resultFolder or nil,
    flags = os.DialogFlags and bit.bor(os.DialogFlags.PickFolders, os.DialogFlags.PathMustExist) or nil
  }, onFolderChosen)
end

-- Draws text wrapped to the card's inner width. Breaks at spaces, and inside a word that is too
-- long on its own (such as a folder path), so nothing runs into the next column.
local function drawWrapped(text, color)
  local maxW = layout.innerW or 240
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
  for _, l in ipairs(lines) do
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

local function drawResultFolderLabel()
  if S.resultFolder ~= "" then
    ui.textColored("RESULT FOLDER", MUTED)
    drawPathField(S.resultFolder)
    if ui.button("Reset to Default", vec2(230, 22)) then
      S.resultFolder = ""
      S.storedSettings.resultFolder = ""
    end
    return
  end
  local resolved = Writer.ResultEffectiveFolder()
  if resolved ~= "" then
    ui.textColored("RESULT FOLDER (DEFAULT)", MUTED)
    drawPathField(resolved, MUTED,
        "The Multi_Class app folder (apps\\lua\\Multi_Class). Point the dashboard's 'Lua App Result Path' there, or click 'Choose result folder...' to change it.")
  else
    drawWrapped("Result folder: not resolved - please choose a folder", rgbm(1.0, 0.7, 0.35, 1))
  end
end

local function drawAutoSaveCheckbox()
  -- Available in Race, Practice and Qualifying alike.
  if checkRow("Auto-save session result when the session finishes", S.resultExportEnabled) then
    S.resultExportEnabled = not S.resultExportEnabled
    S.storedSettings.resultExportEnabled = S.resultExportEnabled
    if not S.resultExportEnabled then
      Exporter.ResetResultExport()
    end
  end
end

local function drawChooseFolderButton(w)
  ui.setCursor(vec2(colX, ui.getCursor().y + 4))
  if ui.button("Choose result folder...", vec2(w - 48, 28)) then
    local dlgOk, dlgErr = pcall(openFolderDialog)
    if not dlgOk then
      ui.toast(ui.Icons.Warning, "Error opening folder dialog: " .. tostring(dlgErr))
    end
  end
end

local function drawResultExportSection(w)
  drawAutoSaveCheckbox()
  drawChooseFolderButton(w)

  ui.setCursor(vec2(colX, ui.getCursor().y + 6))
  ui.pushFont(ui.Font.Small)
  drawResultFolderLabel()

  ui.setCursor(vec2(colX, ui.getCursor().y + 2))
  drawWrapped("Results are split into practice, qualifying and race subfolders. Point the dashboard's 'Lua App Result Path' at the same folder.")
  ui.setCursor(vec2(colX, ui.getCursor().y + 2))
  if S.resultExportStatus ~= "" then
    drawWrapped(S.resultExportStatus)
  else
    ui.textColored("No session result saved yet.", MUTED)
  end
  ui.popFont()
  ui.dummy(vec2(0, 8))
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

local function drawYellowFlagSection(w)
  if checkRow("Full course yellow when a car stops on track", S.yellowFlagEnabled) then
    S.yellowFlagEnabled = not S.yellowFlagEnabled
    S.storedSettings.yellowFlagEnabled = S.yellowFlagEnabled
  end
  if checkRow("Lapped cars let faster cars pass (blue flag)", S.blueFlagEnabled) then
    S.blueFlagEnabled = not S.blueFlagEnabled
    S.storedSettings.blueFlagEnabled = S.blueFlagEnabled
  end
  if ui.itemHovered() then
    ui.setTooltip("A lapped AI car lifts and moves aside for the faster car behind, and stays back until it is well clear.")
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

-- Classes/tags found on the current grid that have not been added yet.
local function availableClassOptions()
  local added = {}
  for _, name in ipairs(S.releaseClasses) do added[string.lower(name)] = true end
  local options = {}
  local ok, names = pcall(Detect.GetAvailableClassNames)
  for _, name in ipairs(ok and names or {}) do
    if not added[string.lower(name)] then options[#options + 1] = name end
  end
  table.sort(options, function(a, b) return string.lower(a) < string.lower(b) end)
  return options
end

local function drawAddClassControls(w)
  ui.setCursor(vec2(colX, ui.getCursor().y))
  ui.text("Add Class (auto-assign by car tag):")
  ui.setCursor(vec2(colX, ui.getCursor().y + 4))
  ui.pushItemWidth(w - 48)
  local options = availableClassOptions()
  local current = S.classAddInput or ""
  local stillOffered = false
  for _, name in ipairs(options) do
    if name == current then stillOffered = true break end
  end
  if not stillOffered then current = "" end
  S.classAddInput = current
  local preview = current ~= "" and current or (#options > 0 and "Select a class..." or "No classes left on the grid")
  ui.combo("##NewReleaseClass", preview, function()
    for _, name in ipairs(options) do
      if ui.selectable(name, name == current) then S.classAddInput = name end
    end
  end)
  ui.popItemWidth()
  ui.setCursor(vec2(colX, ui.getCursor().y + 6))
  if ui.button("Add Class", vec2(w - 48, 28)) then
    local addOk, addErr = pcall(function()
      Classes.AddReleaseClass(S.classAddInput)
    end)
    if not addOk then
      S.classAddError = "Error adding class: " .. tostring(addErr)
    end
  end
  ui.setCursor(vec2(colX, ui.getCursor().y + 6))
  if ui.button("Verify", vec2(w - 48, 28)) then
    pcall(Classes.VerifyClassAssignments)
  end
  if ui.itemHovered() then
    ui.setTooltip("Use 'Add Class' first to assign cars by tag, then 'Verify' to check every car belongs to an added class.")
  end
end

local function drawClassAddError()
  if S.classAddError then
    ui.setCursor(vec2(colX, ui.getCursor().y + 8))
    ui.pushFont(ui.Font.Small)
    ui.textColored(S.classAddError, rgbm.colors.red)
    ui.popFont()
  end
end

local function drawClassRow(ci)
  local clsName = S.releaseClasses[ci]
  local carList = {}
  local count = 0
  for i = 0, S.driverCount - 1 do
    if S.manualDriverClass[i] == clsName then
      count = count + 1
      local cn = ac.getCarName and ac.getCarName(i) or sFormat("Car %d", i + 1)
      carList[#carList + 1] = cn
    end
  end
  ui.setCursor(vec2(colX + 8, ui.getCursor().y + 2))
  ui.text(sFormat("%s (%d car%s)", clsName, count, count == 1 and "" or "s"))
  if count > 0 then
    if ui.itemHovered() then
      ui.setTooltip("Cars: " .. table.concat(carList, ", "))
    end
  end
  ui.sameLine()
  ui.pushStyleVar(ui.StyleVar.FramePadding, vec2(6, 2))
  ui.pushStyleVar(ui.StyleVar.ButtonTextAlign, vec2(0.5, 0.5))
  if ui.button("Remove##removeclass" .. ci, vec2(64, 20)) then
    Classes.RemoveReleaseClass(ci)
  end
  ui.popStyleVar(2)
end

local function drawClassList()
  if #S.releaseClasses > 0 then
    ui.setCursor(vec2(colX, ui.getCursor().y + 12))
    ui.text("Classes:")
    for ci = 1, #S.releaseClasses do
      drawClassRow(ci)
    end
  end
end

local function drawEnduranceSection(w)
  if checkRow("Enable Multiple Class Race", S.enduranceEnabled) then
    S.enduranceEnabled = not S.enduranceEnabled
    S.storedSettings.enduranceEnabled = S.enduranceEnabled
  end

  if S.enduranceEnabled then
    if S.driverCount > 0 then
      drawAddClassControls(w)
      drawClassAddError()
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
  ui.textWrapped(err or "Class-based mode cannot be enabled under current conditions!")
  ui.popItemWidth()
  ui.dummy(vec2(0, 8))
end

function Config.Draw()
  local valid, err = Sess.CheckSessionValidity()
  local sim = ac.getSim()
  local w = ui.windowWidth()
  local h = ui.windowHeight()

  layout.baseX = ui.getCursor().x   -- the window's default line-start x

  ui.pushStyleVar(ui.StyleVar.FramePadding, vec2(6, 6))
  local pushedColours = pushModernStyle()

  local top = drawBackgroundAndTitle(w, h)
  beginColumns(w, top)

  section("race", "RACE MODE", ACCENT_GREEN, drawEnduranceSection)

  if S.enduranceEnabled then
    section("results", "SESSION RESULTS", ACCENT_BLUE, drawResultExportSection, "race")
    section("music", "MUSIC", ACCENT_PURPLE, drawMusicSection)
    section("autopilot", "AUTOPILOT", ACCENT_CYAN, drawAutopilotSection)
    section("flags", "FLAGS", ACCENT_AMBER, drawYellowFlagSection)

    if not valid then
      section("notice", "NOTICE", ACCENT_RED, function(width) drawUnsupportedMessage(width, err) end)
    end
  end

  -- Reserve the room below the tallest column so the auto-sized window includes it.
  local bottom = top
  for c = 1, layout.cols do bottom = math.max(bottom, layout.ys[c]) end
  ui.setCursor(vec2(layout.baseX, bottom))
  ui.dummy(vec2(1, 14))

  ui.popStyleVar(2)   -- FrameRounding + FramePadding
  if pushedColours > 0 then ui.popStyleColor(pushedColours) end
end

return Config
