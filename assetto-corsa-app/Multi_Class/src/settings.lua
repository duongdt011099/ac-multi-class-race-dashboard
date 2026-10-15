-- Restores persisted settings from ac.storage into the live state.
local S = require("src/state")
local U = require("src/util")

local Settings = {}

local function restoreExportSettings(legacyEnabledOk, legacyEnabled, legacyFolderOk, legacyFolder)
  if legacyEnabledOk and legacyEnabled ~= nil then
    S.resultExportEnabled = legacyEnabled
    S.storedSettings.resultExportEnabled = legacyEnabled
  elseif S.storedSettings.resultExportEnabled ~= nil then
    S.resultExportEnabled = S.storedSettings.resultExportEnabled
  end

  if legacyFolderOk and legacyFolder ~= nil then
    S.resultFolder = U.NormalizePath(legacyFolder or "")
    S.storedSettings.resultFolder = S.resultFolder
  elseif S.storedSettings.resultFolder ~= nil then
    S.resultFolder = U.NormalizePath(S.storedSettings.resultFolder or "")
  end
end

local function applySettings()
  if S.storedSettings.enduranceEnabled ~= nil then
    S.enduranceEnabled = S.storedSettings.enduranceEnabled
  end
  -- Probe legacy settings independently: ac.storage rejects undeclared keys, and a
  -- failed legacy lookup must not abort restoration of the current settings below.
  local legacyEnabledOk, legacyEnabled = pcall(function()
    return S.storedSettings.qualiSaveEnabled
  end)
  local legacyFolderOk, legacyFolder = pcall(function()
    return S.storedSettings.qualiSaveFolder
  end)

  restoreExportSettings(legacyEnabledOk, legacyEnabled, legacyFolderOk, legacyFolder)

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
    U.Log("LoadManualClassesFromStorage: settings restore FAILED err=" .. tostring(settingsErr))
  end
end

return Settings
