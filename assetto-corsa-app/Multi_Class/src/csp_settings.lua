-- Reads the CSP settings the app has to respect.
--
-- AI rejoin: CSP "New Behaviour" > "AI race retirement" > "Get back to the race after going to pits"
-- ([AI_RACE_RETIREMENT] REJOIN in new_behaviour.ini). With it off, an AI car that went to the pits
-- after a crash / stuck teleport stays there, and the app must not send it back out either
-- (developer mode excepted, where the app keeps the whole field running for testing).
local S = require("src/state")
local U = require("src/util")

local M = {}

local REFRESH_SECONDS = 5.0   -- the setting can be changed in-game from the CSP settings
local cachedRejoin = nil
local cachedAt = -1000.0
local loggedValue = nil

local function readFromFile(folderId)
  local ok, value = pcall(function()
    local path = ac.getFolder(folderId) .. "/new_behaviour.ini"
    local cfg = ac.INIConfig.load(path)
    local v = cfg:get("AI_RACE_RETIREMENT", "REJOIN", -1)
    if v == -1 then return nil end
    return v ~= 0
  end)
  if ok then return value end
  return nil
end

local function readRejoin()
  -- CSP's own merged view (defaults + user overrides).
  if ac.INIConfig and ac.INIConfig.cspModule and ac.CSPModuleID and ac.CSPModuleID.NewBehaviour then
    local ok, value = pcall(function()
      return ac.INIConfig.cspModule(ac.CSPModuleID.NewBehaviour):get("AI_RACE_RETIREMENT", "REJOIN", false)
    end)
    if ok and type(value) == "boolean" then return value end
  end
  -- Fallback: the user's override file, then CSP's defaults.
  local user = readFromFile(ac.FolderID.ExtCfgUser)
  if user ~= nil then return user end
  local sys = readFromFile(ac.FolderID.ExtCfgSys)
  if sys ~= nil then return sys end
  return false   -- CSP's default
end

function M.CspAIRejoinEnabled()
  local now = os.clock()
  if cachedRejoin == nil or now - cachedAt >= REFRESH_SECONDS then
    cachedRejoin = readRejoin()
    cachedAt = now
    if loggedValue ~= cachedRejoin then
      loggedValue = cachedRejoin
      U.Log("CSP: AI rejoin after going to the pits is " .. (cachedRejoin and "ON" or "OFF"))
    end
  end
  return cachedRejoin
end

-- True when the app may send an AI car back into the race after it went to the pits.
function M.AIRejoinAllowed()
  return S.developerMode == true or M.CspAIRejoinEnabled()
end

return M
