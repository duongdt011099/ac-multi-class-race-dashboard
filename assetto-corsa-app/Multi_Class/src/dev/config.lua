-- Developer mode. Hidden from the in-game config panel on purpose: it is switched on only by
-- "enableDeveloperMode": true in config.json (next to Multi_Class.lua). The file is re-read every
-- few seconds, so editing it takes effect without restarting the game.
--
-- While on: the human car is driven by the AI for the whole session (see rolling/control.lua and
-- frame.lua), the manual-control banner and audio are suppressed, and the live log window
-- (cl_devlog) shows everything U.Log writes.
local S = require("src/state")

local M = {}

local POLL_SECONDS = 2.0
local lastPoll = -1000.0
local synced = false

local function configPath()
  return ac.getFolder(ac.FolderID.ACApps) .. "/lua/Multi_Class/config.json"
end

local function readFlag()
  local ok, flag = pcall(function()
    local file = io.open(configPath(), "r")
    if not file then return false end
    local text = file:read("*a") or ""
    file:close()
    local value = text:match('"enableDeveloperMode"%s*:%s*(%a+)')
    return value == "true"
  end)
  return ok and flag == true
end

-- Re-reads the flag. Returns true when it just changed.
function M.Refresh()
  local flag = readFlag()
  if flag == (S.developerMode == true) then
    -- The game remembers a window's open state between sessions: close a stale log window once.
    if not synced then
      synced = true
      if not flag then pcall(ac.setWindowOpen, "cl_devlog", false) end
    end
    return false
  end
  synced = true
  S.developerMode = flag
  if flag then
    S.logBuffer = S.logBuffer or {}
    pcall(ac.setWindowOpen, "cl_devlog", true)
  else
    pcall(ac.setWindowOpen, "cl_devlog", false)
  end
  return true
end

-- Called every frame; returns true on the frame the flag flipped.
function M.Poll()
  local now = os.clock()
  if now - lastPoll < POLL_SECONDS then return false end
  lastPoll = now
  return M.Refresh()
end

return M
