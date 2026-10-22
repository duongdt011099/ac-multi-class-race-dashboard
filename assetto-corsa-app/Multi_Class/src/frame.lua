-- Per-frame update (script.update body) and the session-start callback.
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local Classes = require("src/classes/manager")
local Grid = require("src/grid")
local Snap = require("src/export/snapshot")
local Exporter = require("src/export/exporter")
local RCtl = require("src/rolling/control")
local PoleSide = require("src/rolling/pole_side")
local Prepare = require("src/rolling/prepare")
local RUpdate = require("src/rolling/update")
local Audio = require("src/audio")
local Yellow = require("src/yellow/yellow")

local Frame = {}

local sFormat = string.format

local function readSessionName(sim)
  local sessionName = ""
  local okName, name = pcall(function() return ac.getSessionName(sim.currentSessionIndex) end)
  if okName and type(name) == "string" then sessionName = name end
  return sessionName
end

local function readSessionTypeAndOver(sim)
  local sessionType, sessionOver = nil, nil
  if ac.getSession then
    local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
    if okSession and session then
      local okType, value = pcall(function() return session.type end)
      if okType then sessionType = value end
      local okOver, over = pcall(function() return session.isOver end)
      if okOver then sessionOver = over end
    end
  end
  return sessionType, sessionOver
end

local function readSessionTimeBucket(sim)
  local sessionTimeLeft = nil
  local okTimeLeft, timeLeft = pcall(function() return sim.sessionTimeLeft end)
  if okTimeLeft and type(timeLeft) == "number" then sessionTimeLeft = timeLeft end
  return sessionTimeLeft and math.floor(sessionTimeLeft / 1000) or nil
end

local function buildEntryLine(sim)
  local sessionName = readSessionName(sim)
  local sessionType, sessionOver = readSessionTypeAndOver(sim)

  local resultsVisible = nil
  local okResults, results = pcall(function() return sim.isLookingAtSessionResults end)
  if okResults then resultsVisible = results end
  local sessionTimeBucket = readSessionTimeBucket(sim)
  local playerInPit = Snap.IsPlayerInPit()

  return sFormat(
    "EXPORT: update-entry started=%s prevStarted=%s index=%s prevIndex=%s cars=%d " ..
    "name='%s' sessionType=%s raceType=%s leftSec=%s playerInPit=%s over=%s results=%s",
    tostring(sim.isSessionStarted), tostring(S.lastSessionStarted),
    tostring(sim.currentSessionIndex), tostring(S.lastSessionIndex), S.driverCount,
    sessionName, tostring(sessionType), tostring(sim.raceSessionType),
    tostring(sessionTimeBucket), tostring(playerInPit),
    tostring(sessionOver), tostring(resultsVisible))
end

-- Log the incoming state before any export-side API calls. If the transition handler
-- encounters an AC/CSP state object that is no longer valid after cars disappear, this
-- entry still records the zero-car state that led to the failure.
local function logUpdateEntry(sim)
  local entryOk, entryLine = pcall(buildEntryLine, sim)
  if entryOk and entryLine ~= S.lastExportUpdateDiagnostic then
    S.lastExportUpdateDiagnostic = entryLine
    U.Log(entryLine)
  elseif not entryOk then
    U.Log("EXPORT: update-entry diagnostics failed: " .. tostring(entryLine))
  end
end

local function resetRollingToIdle()
  if S.rollingStartState ~= "idle" then
    RCtl.RestoreRollingStartControls(false)
    S.rollingStartState = "idle"
    S.rollingStartStatus = ""
  end
end

-- AC's in-place "Restart Session" reuses the same session index/name and does not fire
-- ac.onSessionStart, so detect the true -> false edge of isSessionStarted ourselves and arm a
-- fresh re-init for the coming pre-start countdown. This is read before the export frame so
-- lastSessionStarted still holds the previous frame's value.
local function detectInPlaceRestart(sim)
  if S.lastSessionStarted == true and sim.isSessionStarted ~= true then
    resetRollingToIdle()
    S.rollingPendingReinit = true
  end
  if sim.isSessionStarted == true then
    S.rollingPendingReinit = false
  end
  if S.rollingPendingReinit and S.driverCount > 0 then
    S.rollingPendingReinit = false
    S.firstFrame = true
    S.gridReordered = false
    S.classReloadAttempted = false
  end
end

local function runExportFrame(sim)
  local updateOk, sessionChanged, sessionEnded, sessionBegan, sessionSwitched =
      pcall(Exporter.UpdateResultExportFrame, sim)
  if not updateOk then
    local message = tostring(sessionChanged)
    if message ~= S.lastExportTransitionError then
      U.Log("EXPORT: transition handler failed: " .. message)
      S.lastExportTransitionError = message
    end
    sessionChanged = false
  else
    S.lastExportTransitionError = nil
  end
  return sessionChanged, sessionSwitched
end

local function handleSessionChange(sessionChanged, sessionSwitched)
  if sessionSwitched then
    resetRollingToIdle()
    -- A different session: its own grid and its own lane convention have to be captured again.
    S.rollingStartGridCaptured = false
    S.rollingStartStartSides = {}
  end
  if sessionChanged then
    S.firstFrame = true
    S.carClassCache = {}
    S.uiSourceCache = {}
  end
end

-- Captures the starting grid and the pole side. This happens ONCE per session: AC's in-place
-- restart keeps the same grid, but the cars are not back on it when the restart is detected, so
-- recording the slots again there would capture the live race classification instead of the grid,
-- and the pole side would be measured from the aborted attempt's formation, mirroring every
-- column of the field.
local function recordAndReorderGrid(sim)
  if not S.rollingStartGridCaptured then
    -- A new session: the manual lane override only lasts for one session.
    S.rollingStartPoleOnRight = false
    S.rollingStartPoleSideSource = "auto"
    S.rollingStartStartSides = {}
    Grid.RecordStartingPositions()
    PoleSide.DetectRollingStartPoleSide()
    S.rollingStartGridCaptured = true
  else
    U.Log("ROLLING: grid capture reused (same session); pole side and grid slots kept")
  end
  Grid.ReorderGridByClass()
  S.gridReordered = true
end

local function logGridCars()
  for i = 0, S.driverCount - 1 do
    local okID, carID = pcall(function() return ac.getCarID and ac.getCarID(i) end)
    if not okID then carID = nil end
    U.Log(sFormat("GRID: i=%d carID=%s name=%s", i, tostring(carID), tostring(ac.getCarName and ac.getCarName(i))))
  end
end

local function firstFrameGridSetup(sim)
  logGridCars()
  local okLoad, loadErr = pcall(Classes.LoadManualClassesFromStorage)
  if not okLoad then
    U.Log("FIRSTFRAME: LoadManualClassesFromStorage FAILED err=" .. tostring(loadErr))
  end
  if Sess.IsRaceMode(sim) then
    recordAndReorderGrid(sim)
  end
end

local function handleFirstFrame(sim)
  if not S.firstFrame then return end
  -- Cleared before the work runs: if anything below throws, the flag stays false so a
  -- single failure cannot turn into a per-frame infinite loop.
  S.firstFrame = false
  U.Log(sFormat("FIRSTFRAME: idx=%s race=%s dc=%d rc=%d", tostring(sim.currentSessionIndex), tostring(Sess.IsRaceMode()), S.driverCount, #S.releaseClasses))
  S.leaderboardOpened = false
  S.playerStoppedState = false
  -- The captured grid is only dropped when it belongs to an older session: an in-place restart
  -- reuses the same grid, and clearing it here would leave the rolling start with the live race
  -- classification instead of the grid slots.
  if not S.rollingStartGridCaptured then
    S.allDriversStartingPos = {}
    S.rollingStartGridPositions = {}
  end
  if S.driverCount > 0 then
    firstFrameGridSetup(sim)
  end
  U.Log(sFormat("FIRSTFRAME DONE: rc=%d classes={%s}", #S.releaseClasses, table.concat(S.releaseClasses, ",")))
end

-- Attempts at most once per session. Without this guard the block re-ran every frame
-- whenever no class was assigned, reloading storage for the whole grid each time and
-- starving the leaderboard.
local function reloadClassesIfMissing(sim)
  if not S.classReloadAttempted and #S.releaseClasses == 0 and S.driverCount > 0 then
    S.classReloadAttempted = true
    Classes.LoadManualClassesFromStorage()
    if #S.releaseClasses > 0 then
      Classes.BuildClassInfo()
      if Sess.IsRaceMode(sim) then
        recordAndReorderGrid(sim)
      end
    end
  end
end

local function prepareRollingIfNeeded(sim, valid)
  if valid and Sess.IsRaceMode(sim) and S.rollingStartState == "idle" and S.driverCount > 0
      and not Sess.IsSessionFinished(sim) then
    if not S.gridReordered then
      recordAndReorderGrid(sim)
    end
    local okRolling, rollingErr = pcall(Prepare.PrepareRollingStart, sim)
    if not okRolling then
      RCtl.MarkRollingStartUnavailable("Could not prepare the field: " .. tostring(rollingErr), true)
    end
  end
end

local function stopForInvalidSession(validationError)
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then
    RCtl.MarkRollingStartUnavailable(
      "Rolling start stopped: " .. tostring(validationError or "this session is not supported."),
      false)
  end
end

local function updateRolling(sim, dt)
  pcall(Audio.Update, dt)
  local rollingOk, rollingErr = pcall(RUpdate.UpdateRollingStart, sim)
  if not rollingOk then
    RCtl.MarkRollingStartUnavailable("Rolling start stopped safely: " .. tostring(rollingErr), false)
  end
  local yellowOk, yellowErr = pcall(Yellow.Update, sim, dt)
  if not yellowOk then
    U.Log("YELLOW: update failed: " .. tostring(yellowErr))
    pcall(Yellow.Reset)
  end
end

function Frame.Update(dt)
  local sim = ac.getSim()
  S.driverCount = sim.carsCount or 0

  logUpdateEntry(sim)
  detectInPlaceRestart(sim)
  handleSessionChange(runExportFrame(sim))
  local valid, validationError = Sess.CheckSessionValidity()
  handleFirstFrame(sim)
  reloadClassesIfMissing(sim)
  prepareRollingIfNeeded(sim, valid)

  if sim.isInMainMenu == true then
    ac.setWindowOpen("cl_config", true)
  end

  if not valid then
    stopForInvalidSession(validationError)
    return
  end

  if not S.leaderboardOpened and sim.isInMainMenu ~= true then
    ac.setWindowOpen("cl_leaderboard", true)
    S.leaderboardOpened = true
  end

  updateRolling(sim, dt)
end

function Frame.OnSessionStart()
  -- A new session always drops any field left over from the previous one, whatever state it was
  -- in (a restart can catch us mid staged/rolling).
  RCtl.RestoreRollingStartControls(false)
  pcall(Yellow.Reset)
  S.rollingStartState = "idle"
  S.rollingStartStatus = ""

  S.firstFrame          = true
  S.playerStoppedState  = false
  S.raceDisplayStable   = nil
  S.raceDisplayPending  = nil
  S.gridReordered       = false
  S.classReloadAttempted = false
  S.carClassCache       = {}
  S.allDriversStartingPos = {}
  S.uiSourceCache       = {}
  -- A new session brings its own grid: the capture and the measured grid sides are taken again.
  S.rollingStartGridCaptured = false
  S.rollingStartStartSides   = {}
  if S.liveSessionPending then
    S.resultResetAfterExport = true
  else
    Exporter.ResetResultExport()
    S.resultResetAfterExport = false
  end
  S.lastExportDiagnostic = nil
  U.Log("onSessionStart: firstFrame=true")
end

return Frame
