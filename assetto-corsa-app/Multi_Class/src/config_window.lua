-- Configuration window (script.clConfig body).
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local Classes = require("src/classes/manager")
local Writer = require("src/export/writer")
local Exporter = require("src/export/exporter")
local Audio = require("src/audio")

local Config = {}

local sFormat = string.format
local COL_X = 24
local MUTED = rgbm(0.6, 0.6, 0.7, 1)

local function drawBackgroundAndTitle(w, h)
  ui.drawRectFilled(vec2(0, 0), vec2(w, h), rgbm(0.04, 0.05, 0.07, 0.85), 0)
  ui.drawRect(vec2(0, 0), vec2(w, h), rgbm(0.18, 0.18, 0.20, 0.6), 0, nil, 1)

  ui.pushFont(ui.Font.Title)
  local titleText = "Multiple Class Race Configuration"
  local titleSize = ui.measureText(titleText)
  local titleX = (w - titleSize.x) / 2
  ui.setCursor(vec2(titleX, 12))
  ui.text(titleText)
  ui.popFont()

  local separatorY = ui.getCursor().y + 10
  ui.drawLine(vec2(COL_X, separatorY), vec2(w - 24, separatorY), rgbm(0.25, 0.25, 0.28, 0.5), 1)
  ui.setCursor(vec2(COL_X, separatorY + 12))
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

local function drawResultFolderLabel()
  if S.resultFolder ~= "" then
    ui.textWrapped("Result folder: " .. S.resultFolder)
    ui.setCursor(vec2(COL_X, ui.getCursor().y + 2))
    if ui.button("Reset to Default", vec2(230, 22)) then
      S.resultFolder = ""
      S.storedSettings.resultFolder = ""
    end
    return
  end
  local resolved = Writer.ResultEffectiveFolder()
  if resolved ~= "" then
    ui.textColored("Result folder: " .. resolved .. " (default)", MUTED)
    if ui.itemHovered() then
      ui.setTooltip("Documents\\Assetto Corsa\\mcr-results, which is also the dashboard default. Click 'Choose result folder...' to change it.")
    end
  else
    ui.textColored("Result folder: not resolved - please choose a folder", rgbm(1.0, 0.7, 0.35, 1))
  end
end

local function drawAutoSaveCheckbox()
  -- Available in Race, Practice and Qualifying alike.
  if ui.checkbox("Auto-save session result when the session finishes", S.resultExportEnabled) then
    S.resultExportEnabled = not S.resultExportEnabled
    S.storedSettings.resultExportEnabled = S.resultExportEnabled
    if not S.resultExportEnabled then
      Exporter.ResetResultExport()
    end
  end
end

local function drawChooseFolderButton(w)
  ui.setCursor(vec2(COL_X, ui.getCursor().y + 4))
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

  ui.setCursor(vec2(COL_X, ui.getCursor().y + 6))
  ui.pushFont(ui.Font.Small)
  drawResultFolderLabel()

  ui.setCursor(vec2(COL_X, ui.getCursor().y + 2))
  ui.textWrapped("Results are split into practice, qualifying and race subfolders. Point the dashboard's 'Lua App Result Path' at the same folder.")
  ui.setCursor(vec2(COL_X, ui.getCursor().y + 2))
  if S.resultExportStatus ~= "" then
    ui.textWrapped(S.resultExportStatus)
  else
    ui.textColored("No session result saved yet.", MUTED)
  end
  ui.popFont()
  ui.dummy(vec2(0, 8))
end

local function drawMusicSection(w)
  if ui.checkbox("Play music when you enter double file (rolling start)", S.rollingMusicEnabled) then
    S.rollingMusicEnabled = not S.rollingMusicEnabled
    S.storedSettings.rollingMusicEnabled = S.rollingMusicEnabled
    if not S.rollingMusicEnabled then Audio.Reset() end
  end
  S.rollingMusicVolume = 4.0
  S.storedSettings.rollingMusicVolume = 4.0
  ui.dummy(vec2(0, 8))
end

local function drawYellowFlagSection(w)
  if ui.checkbox("Full course yellow when a car stops on track", S.yellowFlagEnabled) then
    S.yellowFlagEnabled = not S.yellowFlagEnabled
    S.storedSettings.yellowFlagEnabled = S.yellowFlagEnabled
  end
  if ui.checkbox("Play yellow / green flag sounds", S.flagSoundEnabled) then
    S.flagSoundEnabled = not S.flagSoundEnabled
    S.storedSettings.flagSoundEnabled = S.flagSoundEnabled
    if not S.flagSoundEnabled then Audio.ResetFlags() end
  end
  S.flagSoundVolume = 4.0
  S.storedSettings.flagSoundVolume = 4.0
  ui.dummy(vec2(0, 8))
end

local function drawPoleSideSection(w)
  ui.setCursor(vec2(COL_X, ui.getCursor().y))
  ui.text("Rolling Start - lane for pole position (P1, P3, ...):")
  ui.setCursor(vec2(COL_X, ui.getCursor().y + 4))
  local laneButtonText = S.rollingStartPoleOnRight
      and "RIGHT LANE (P1, P3, ...)" or "LEFT LANE (P1, P3, ...)"
  if ui.button(laneButtonText, vec2(w - 48, 28)) then
    S.rollingStartPoleOnRight = not S.rollingStartPoleOnRight
    S.rollingStartPoleSideSource = "manual"
  end
  if ui.itemHovered() then
    ui.setTooltip("The lane taken by odd grid slots (P1, P3, ...) during the rolling start; even slots take the other lane.\n\nDefault is auto-detected from the side your car occupies on the starting grid. Click to override for this session.")
  end
  ui.setCursor(vec2(COL_X, ui.getCursor().y + 4))
  ui.pushFont(ui.Font.Small)
  local srcText = (S.rollingStartPoleSideSource == "manual")
      and "Overridden manually for this session."
      or "Auto-detected from your starting-grid position."
  ui.textColored(srcText, MUTED)
  ui.popFont()
  ui.dummy(vec2(0, 8))
end

local function drawAddClassControls(w)
  ui.setCursor(vec2(COL_X, ui.getCursor().y))
  ui.text("Add Class (auto-assign by car tag):")
  ui.setCursor(vec2(COL_X, ui.getCursor().y + 4))
  ui.pushItemWidth(w - 48)
  S.classAddInput = ui.inputText("##NewReleaseClass", S.classAddInput or "")
  ui.popItemWidth()
  ui.setCursor(vec2(COL_X, ui.getCursor().y + 6))
  if ui.button("Add Class", vec2(w - 48, 28)) then
    local addOk, addErr = pcall(function()
      Classes.AddReleaseClass(S.classAddInput)
    end)
    if not addOk then
      S.classAddError = "Error adding class: " .. tostring(addErr)
    end
  end
  ui.setCursor(vec2(COL_X, ui.getCursor().y + 6))
  if ui.button("Verify", vec2(w - 48, 28)) then
    pcall(Classes.VerifyClassAssignments)
  end
  if ui.itemHovered() then
    ui.setTooltip("Use 'Add Class' first to assign cars by tag, then 'Verify' to check every car belongs to an added class.")
  end
end

local function drawClassAddError()
  if S.classAddError then
    ui.setCursor(vec2(COL_X, ui.getCursor().y + 8))
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
  ui.setCursor(vec2(COL_X + 8, ui.getCursor().y + 2))
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
    ui.setCursor(vec2(COL_X, ui.getCursor().y + 12))
    ui.text("Classes:")
    for ci = 1, #S.releaseClasses do
      drawClassRow(ci)
    end
  end
end

local function drawEnduranceSection(w)
  if ui.checkbox("Enable Multiple Class Race", S.enduranceEnabled) then
    S.enduranceEnabled = not S.enduranceEnabled
    S.storedSettings.enduranceEnabled = S.enduranceEnabled
  end

  if S.enduranceEnabled then
    if S.driverCount > 0 then
      drawAddClassControls(w)
      drawClassAddError()
      drawClassList()
    else
      ui.setCursor(vec2(COL_X, ui.getCursor().y))
      ui.pushFont(ui.Font.Small)
      ui.textColored("Waiting for car data to load...", rgbm(0.6, 0.7, 0.9, 1))
      ui.popFont()
    end

    ui.dummy(vec2(0, 8))
  end
end

local function drawUnsupportedMessage(w, err)
  ui.setCursor(vec2(COL_X, ui.getCursor().y))
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

  ui.pushStyleVar(ui.StyleVar.FramePadding, vec2(6, 6))

  drawBackgroundAndTitle(w, h)

  drawEnduranceSection(w)

  if not S.enduranceEnabled then
    ui.popStyleVar()
    return
  end

  drawResultExportSection(w)
  drawMusicSection(w)
  drawYellowFlagSection(w)

  if valid then
    if Sess.IsRaceMode(sim) then
      drawPoleSideSection(w)
    end
  else
    drawUnsupportedMessage(w, err)
  end

  ui.popStyleVar()
end

return Config
