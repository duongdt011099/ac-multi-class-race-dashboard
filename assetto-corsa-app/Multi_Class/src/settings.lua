-- Restores persisted settings from ac.storage into the live state.
local S = require("src/state")
local U = require("src/util")

local Settings = {}

local function restoreExportSettings(legacyEnabledOk, legacyEnabled)
  -- Saving each finished session result is always on; the dashboard depends on it.
  S.resultExportEnabled = true
  S.storedSettings.resultExportEnabled = true

  -- The result folder is fixed (see Writer.ResultDefaultFolder); any folder chosen in older versions is dropped.
  S.resultFolder = ""
  S.storedSettings.resultFolder = ""
end

local function applySettings()
  S.enduranceEnabled = true
  S.storedSettings.enduranceEnabled = true
  -- Probe legacy settings independently: ac.storage rejects undeclared keys, and a
  -- failed legacy lookup must not abort restoration of the current settings below.
  local legacyEnabledOk, legacyEnabled = pcall(function()
    return S.storedSettings.qualiSaveEnabled
  end)
  local legacyFolderOk, legacyFolder = pcall(function()
    return S.storedSettings.qualiSaveFolder
  end)

  restoreExportSettings(legacyEnabledOk, legacyEnabled)

  if legacyEnabledOk then
    pcall(function() S.storedSettings.qualiSaveEnabled = nil end)
  end
  if legacyFolderOk then
    pcall(function() S.storedSettings.qualiSaveFolder = nil end)
  end
end

-- Restoring settings must never be able to abort class loading, so it is isolated.
function Settings.RestoreStoredSettings()
  local okSettings, settingsErr = pcall(applySettings)
  if not okSettings then
    U.Log("LoadClassesFromSessionFile: settings restore FAILED err=" .. tostring(settingsErr))
  end
end

return Settings
