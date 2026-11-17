-- Session name / type helpers and session validity checks.
local C = require("src/constants")

local Sess = {}

local sFormat = string.format

function Sess.GetSessionNameLower(sim)
  sim = sim or ac.getSim()
  local ok, name = pcall(function() return ac.getSessionName(sim.currentSessionIndex) end)
  if not ok or not name then return "" end
  return string.lower(name)
end

function Sess.IsRaceMode(sim)
  return Sess.GetSessionNameLower(sim):find("race") ~= nil
end

function Sess.IsQualiPracticeMode(sim)
  local sn = Sess.GetSessionNameLower(sim)
  return sn:find("qualifying") ~= nil or sn:find("practice") ~= nil
end

-- The dashboard derives the session type from the subfolder the result lands in, so these
-- three names are a contract with Services/LuaResultPaths.cs.
--
-- Resolved from the ac.SessionType enum rather than the session display name: the name is
-- already gone by the time the session ends and the car returns to the pits, which made
-- the export silently produce nothing. Hotlap is a practice-style session, so it is
-- reported as practice instead of being dropped.
function Sess.SessionTypeToFolder(sessionType)
  if type(sessionType) ~= "number" then return nil end
  if sessionType == C.SESSION_TYPE_RACE then return C.SUBFOLDER_RACE end
  if sessionType == C.SESSION_TYPE_QUALIFY then return C.SUBFOLDER_QUALIFYING end
  if sessionType == C.SESSION_TYPE_PRACTICE or sessionType == C.SESSION_TYPE_HOTLAP then
    return C.SUBFOLDER_PRACTICE
  end
  return nil
end

-- Reads the live session type from the session object, falling back to the sim's
-- raceSessionType. Only valid while the session is still running.
function Sess.ReadLiveSessionType(sim)
  sim = sim or ac.getSim()

  if ac.getSession then
    local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
    if okSession and session then
      local okType, sessionType = pcall(function() return session.type end)
      if okType and type(sessionType) == "number" then return sessionType end
    end
  end

  local okRace, raceType = pcall(function() return sim.raceSessionType end)
  if okRace and type(raceType) == "number" then return raceType end

  return nil
end

-- Only used as a fallback when the app starts while results are already on screen, where
-- the enum is unavailable and the display name is all there is.
function Sess.GetExportSessionType()
  if Sess.IsRaceMode() then return C.SUBFOLDER_RACE end
  if not Sess.IsQualiPracticeMode() then return nil end
  if Sess.GetSessionNameLower():find("qualifying") ~= nil then return C.SUBFOLDER_QUALIFYING end
  return C.SUBFOLDER_PRACTICE
end

function Sess.GetTrackNameSafe()
  if ac.getTrackName then
    local ok, name = pcall(ac.getTrackName)
    if ok and type(name) == "string" and name ~= "" then return name end
  end
  return ""
end

function Sess.GetCurrentSessionKey(sim)
  sim = sim or ac.getSim()
  local name = ""
  local ok, n = pcall(function() return ac.getSessionName(sim.currentSessionIndex) end)
  if ok and type(n) == "string" then name = n end
  return sFormat("%s:%s", tostring(sim.currentSessionIndex), name)
end

function Sess.ReadRollingStartLapOffset()
  if not ac.INIConfig or type(ac.INIConfig.raceConfig) ~= "function" then return 0 end

  local okConfig, config = pcall(ac.INIConfig.raceConfig)
  if not okConfig or not config then return 0 end

  local okOffset, offset = pcall(function()
    return config:get("HEADER", "__MCR_ROLLING_START_LAP_OFFSET", 0)
  end)
  if not okOffset then return 0 end

  return tonumber(offset) == 1 and 1 or 0
end

local function sessionIsOver(sim)
  if not ac.getSession then return false end
  local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
  if not (okSession and session) then return false end
  local okOver, isOver = pcall(function() return session.isOver end)
  return okOver and isOver == true
end

-- ac.StateSession.isOver and sim.isLookingAtSessionResults are the two documented
-- "this session is done" signals; either one means the result is final.
function Sess.IsSessionFinished(sim)
  sim = sim or ac.getSim()
  if sim.isInMainMenu == true then return false end

  local okResults, showingResults = pcall(function() return sim.isLookingAtSessionResults end)
  if okResults and showingResults == true then return true end

  return sessionIsOver(sim)
end

function Sess.CheckSessionValidity()
  local sim = ac.getSim()

  if ac.getPatchVersionCode() < 3334 then
    return false, "Please update your Custom Shaders Patch (CSP) to version 0.2.7 or higher!"
  end

  local sn = Sess.GetSessionNameLower()
  if not (sn:find("race") or sn:find("practice") or sn:find("qualifying") or sn:find("hotlap")) then
    return false, "This app works in Race, Practice, or Qualifying sessions!"
  end

  if sim.isOnlineRace then
    return false, "This app only works for Offline/Single-player sessions!"
  end

  return true, nil
end

-- The dashboard stamps race.ini with a marker and a fresh launch id every time it starts a session.
-- The game re-saves race.ini on later launches and keeps unknown HEADER keys, so the marker alone
-- would outlive the dashboard launch. Each launch id is therefore accepted by one game run only:
-- a later run that finds the id already used was started some other way.
Sess.DASHBOARD_HEADER = "__MCR_DASHBOARD"
Sess.LAUNCH_ID_HEADER = "__MCR_LAUNCH_ID"
Sess.NOT_FROM_DASHBOARD_MESSAGE = "The multi class app can only be used from the dashboard app."

function Sess.IsDashboardLaunch()
  local S = require("src/state")
  if S.dashboardLaunch ~= nil then return S.dashboardLaunch end

  if not ac.INIConfig or type(ac.INIConfig.raceConfig) ~= "function" then return false end
  local okConfig, config = pcall(ac.INIConfig.raceConfig)
  if not okConfig or not config then return false end

  local okMarker, marker = pcall(function() return config:get("HEADER", Sess.DASHBOARD_HEADER, 0) end)
  local okId, id = pcall(function() return config:get("HEADER", Sess.LAUNCH_ID_HEADER, "") end)
  id = okId and tostring(id or "") or ""

  local fromDashboard = okMarker and tonumber(marker) == 1 and id ~= ""
  if fromDashboard then
    local okUsed, used = pcall(function() return S.storedSettings.dashboardLaunchId end)
    if okUsed and used == id then
      fromDashboard = false
    else
      pcall(function() S.storedSettings.dashboardLaunchId = id end)
    end
  end

  S.dashboardLaunch = fromDashboard
  return fromDashboard
end


-- The dashboard also stamps race.ini with __MCR_QUICK_RACE=1 when the session is a quick race. race.ini is
-- written afresh for every dashboard launch, so unlike the launch id the flag cannot go stale. Read once
-- per game run: the exporter prefixes the result file name with it (see export/exporter.lua).
Sess.QUICK_RACE_HEADER = "__MCR_QUICK_RACE"

function Sess.IsQuickRace()
  local S = require("src/state")
  if S.quickRace ~= nil then return S.quickRace end

  if not ac.INIConfig or type(ac.INIConfig.raceConfig) ~= "function" then return false end
  local okConfig, config = pcall(ac.INIConfig.raceConfig)
  if not okConfig or not config then return false end

  local okValue, value = pcall(function() return config:get("HEADER", Sess.QUICK_RACE_HEADER, 0) end)

  S.quickRace = okValue and tonumber(value) == 1
  return S.quickRace
end

-- True while the app does nothing: switched off by the user, or the game was not started from the dashboard.
function Sess.AppDisabled()
  local S = require("src/state")
  return not S.enduranceEnabled or not Sess.IsDashboardLaunch()
end

return Sess
