-- Export orchestration: when to write a result, retries, and session transition handling.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local Snap = require("src/export/snapshot")
local Json = require("src/export/json")
local Writer = require("src/export/writer")

local Exporter = {}

local sFormat = string.format

-- Sets the status once per distinct message; optionally logs it, and toasts unless silent.
local function reportStatus(msg, silent, logIt)
  if S.resultExportStatus == msg then return end
  S.resultExportStatus = msg
  if logIt then U.Log("EXPORT: " .. msg) end
  if not silent then ui.toast(ui.Icons.Warning, msg) end
end

-- A failed export is retried rather than abandoned, but the results screen can sit for
-- minutes and each attempt probes the folder and rebuilds the JSON, so the retries are
-- throttled instead of running on every frame.
local function retryThrottled(force)
  if not force and type(os.time) == "function" then
    local now = os.time()
    if now < S.nextExportRetryAt then return true end
    S.nextExportRetryAt = now + C.EXPORT_RETRY_SECONDS
  end
  return false
end

-- Returns the writable target directory, or nil after reporting why there is none.
local function resolveTargetDir(sessionType, silent)
  local root = Writer.ResultEffectiveFolder()
  if root == "" then
    reportStatus("No result folder available - choose one with 'Choose result folder...'", silent, true)
    return nil
  end

  local targetDir = root .. "/" .. sessionType
  local writable, why = Writer.ResultFolderWritable(targetDir)
  if not writable then
    reportStatus("Cannot save session result: " .. tostring(why) .. " (" .. targetDir .. ")", silent, true)
    return nil
  end

  if type(os.date) ~= "function" then
    reportStatus("os.date is unavailable on this CSP version - cannot name the result file.", silent, true)
    return nil
  end
  return targetDir
end

local function buildContents(sessionType, trackName, totalLaps)
  if sessionType == C.SUBFOLDER_RACE then
    return Json.BuildRaceResultJson(trackName, totalLaps)
  end
  return Json.BuildSessionResultJson()
end

local function onWriteSucceeded(sessionType, sessionKey, fileName, targetPath, silent)
  S.exportedSessionKey = sessionKey
  S.liveSessionPending = false
  S.resultExportStatus = sFormat(
    "Saved %s result at %s -> %s/%s",
    sessionType,
    os.date("%H:%M:%S"),
    sessionType,
    fileName)
  if not silent then ui.toast(ui.Icons.Play, "Session result saved") end
  U.Log("EXPORT: wrote " .. targetPath)
end

-- exportedSessionKey is left unset so the next frame retries rather than
-- silently losing the session result.
local function onWriteFailed(err, silent)
  reportStatus("Failed to save session result: " .. tostring(err), silent, false)
  U.Log("EXPORT: FAILED err=" .. tostring(err))
end

-- Writes the result from the snapshot taken while the session was running. sessionKey is
-- remembered on success so the same session can never be written twice.
function Exporter.ExportSessionResult(sessionType, sessionKey, trackName, totalLaps, silent, force)
  if retryThrottled(force) then return end
  if sessionType == nil then return end

  local targetDir = resolveTargetDir(sessionType, silent)
  if not targetDir then return end

  local fileName = os.date("%y%m%d-%H%M%S") .. ".json"
  local targetPath = targetDir .. "/" .. fileName
  local contents = buildContents(sessionType, trackName, totalLaps)

  U.Log("EXPORT: writing " .. targetPath)
  local ok, err = pcall(function()
    Writer.WriteResultFile(targetPath, contents)
  end)

  if ok then
    onWriteSucceeded(sessionType, sessionKey, fileName, targetPath, silent)
  else
    onWriteFailed(err, silent)
  end
end

-- Called once per frame from script.update. Writes at most once per session.
--
-- The primary trigger is the armed end-of-session flag, not the results screen: when the
-- car returns to the pits the results screen may never appear and the session name is
-- already gone. The results-screen check is kept as a secondary trigger for the case
-- where the app starts while results are already on screen.
function Exporter.CheckForFinishedSession(sim)
  if not S.resultExportEnabled then return end
  if S.liveSessionType == nil then return end
  if S.liveSessionCarCount <= 0 then return end
  if S.liveSessionPending == false and not Sess.IsSessionFinished(sim) then return end
  if S.exportedSessionKey ~= nil and S.exportedSessionKey == S.liveSessionKey then return end

  Exporter.ExportSessionResult(S.liveSessionType, S.liveSessionKey, S.liveTrackName, S.liveTotalLaps)
end

-- Only called when a new session begins. Resetting on the end of a session instead would
-- clear the guard that stops the result being written twice.
function Exporter.ResetResultExport()
  S.exportedSessionKey = nil
  S.nextExportRetryAt = 0
  Snap.ClearLiveSession()
end

function Exporter.CaptureLiveSessionSafely(sim)
  local ok, err = pcall(Snap.CaptureLiveSession, sim)
  if ok then
    S.lastExportCaptureError = nil
  else
    local message = tostring(err)
    if message ~= S.lastExportCaptureError then
      U.Log("EXPORT: live-session capture failed: " .. message)
      S.lastExportCaptureError = message
    end
  end
end

function Exporter.CheckForFinishedSessionSafely(sim)
  local ok, err = pcall(Exporter.CheckForFinishedSession, sim)
  if not ok then
    U.Log("EXPORT: finish check failed: " .. tostring(err))
  end
end

local function currentTimeLeftOr(fallback)
  local currentTimeLeft = fallback
  local okSim, sim = pcall(ac.getSim)
  if okSim and sim then
    local okTime, timeLeft = pcall(function() return sim.sessionTimeLeft end)
    if okTime and type(timeLeft) == "number" then currentTimeLeft = timeLeft end
  end
  return currentTimeLeft
end

function Exporter.ExportExpiredSessionOnRelease()
  local currentTimeLeft = currentTimeLeftOr(S.liveSessionTimeLeft)

  U.Log(sFormat(
    "EXPORT: release check enabled=%s type=%s key=%s snapshotCars=%d hadCountdown=%s leftMs=%s exported=%s",
    tostring(S.resultExportEnabled), tostring(S.liveSessionType), tostring(S.liveSessionKey),
    S.liveSessionCarCount, tostring(S.liveSessionHadCountdown), tostring(currentTimeLeft),
    tostring(S.exportedSessionKey == S.liveSessionKey)))

  if not S.resultExportEnabled then return end
  if S.liveSessionType == nil or S.liveSessionCarCount <= 0 then return end
  if not S.liveSessionHadCountdown or type(currentTimeLeft) ~= "number" or currentTimeLeft > 0 then return end
  if S.exportedSessionKey ~= nil and S.exportedSessionKey == S.liveSessionKey then return end

  S.liveSessionPending = true
  local ok, err = pcall(
    Exporter.ExportSessionResult,
    S.liveSessionType,
    S.liveSessionKey,
    S.liveTrackName,
    S.liveTotalLaps,
    true,
    true)
  if not ok then
    U.Log("EXPORT: release fallback failed: " .. tostring(err))
  end
end

local function readSessionTimeLeft(sim)
  local okTimeLeft, timeLeft = pcall(function() return sim.sessionTimeLeft end)
  if okTimeLeft and type(timeLeft) == "number" then return timeLeft end
  return nil
end

local function readSessionOver(sim)
  local sessionOver = nil
  if ac.getSession then
    local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
    if okSession and session then
      local okOver, over = pcall(function() return session.isOver end)
      if okOver then sessionOver = over end
    end
  end
  return sessionOver
end

local function readResultsVisible(sim)
  local resultsVisible = nil
  local okResults, results = pcall(function() return sim.isLookingAtSessionResults end)
  if okResults then resultsVisible = results end
  return resultsVisible
end

local function readSessionName(sim)
  local okName, name = pcall(function() return ac.getSessionName(sim.currentSessionIndex) end)
  if okName and type(name) == "string" then return name end
  return ""
end

local function buildSignature(d, sessionEnded, sessionBegan, sessionSwitched)
  local sim = d.sim
  return table.concat({
    tostring(sim.isSessionStarted),
    tostring(S.lastSessionStarted),
    tostring(sim.currentSessionIndex),
    tostring(S.lastSessionIndex),
    tostring(S.driverCount),
    tostring(d.sessionName),
    tostring(d.sessionType),
    tostring(sim.raceSessionType),
    tostring(d.sessionTimeBucket),
    tostring(d.playerInPit),
    tostring(d.capturedTimeBucket),
    tostring(S.liveSessionHadCountdown),
    tostring(d.sessionOver),
    tostring(d.resultsVisible),
    tostring(sessionEnded),
    tostring(sessionBegan),
    tostring(sessionSwitched),
    tostring(S.liveSessionType),
    tostring(S.liveSessionKey),
    tostring(S.liveSessionCarCount),
    tostring(S.liveSessionPending),
    tostring(S.resultResetAfterExport),
    tostring(S.resultExportEnabled),
  }, "|")
end

local function logStateLine(d, sessionEnded, sessionBegan, sessionSwitched)
  local sim = d.sim
  U.Log(sFormat(
    "EXPORT: state started=%s prevStarted=%s index=%s prevIndex=%s cars=%d name='%s' " ..
    "sessionType=%s raceType=%s leftMs=%s playerInPit=%s capturedLeftMs=%s hadCountdown=%s " ..
    "over=%s results=%s ended=%s began=%s switched=%s liveType=%s liveKey=%s " ..
    "snapshotCars=%d pending=%s deferredReset=%s enabled=%s",
    tostring(sim.isSessionStarted), tostring(S.lastSessionStarted),
    tostring(sim.currentSessionIndex), tostring(S.lastSessionIndex), S.driverCount,
    d.sessionName, tostring(d.sessionType), tostring(sim.raceSessionType),
    tostring(d.sessionTimeLeft), tostring(d.playerInPit), tostring(S.liveSessionTimeLeft),
    tostring(S.liveSessionHadCountdown), tostring(d.sessionOver), tostring(d.resultsVisible),
    tostring(sessionEnded), tostring(sessionBegan), tostring(sessionSwitched),
    tostring(S.liveSessionType), tostring(S.liveSessionKey), S.liveSessionCarCount,
    tostring(S.liveSessionPending), tostring(S.resultResetAfterExport),
    tostring(S.resultExportEnabled)))
end

local function gatherDiagnostics(sim)
  local d = { sim = sim }
  d.sessionType = Sess.ReadLiveSessionType(sim)
  d.sessionTimeLeft = readSessionTimeLeft(sim)
  d.playerInPit = Snap.IsPlayerInPit()
  d.sessionTimeBucket = d.sessionTimeLeft and math.floor(d.sessionTimeLeft / 1000) or nil
  d.capturedTimeBucket = S.liveSessionTimeLeft and math.floor(S.liveSessionTimeLeft / 1000) or nil
  d.sessionOver = readSessionOver(sim)
  d.resultsVisible = readResultsVisible(sim)
  d.sessionName = readSessionName(sim)
  return d
end

-- Emit one diagnostic line whenever AC's export-relevant state changes. This makes it
-- possible to distinguish a missed end transition from a missing session type, empty car
-- snapshot, disabled setting, or an actual write failure without logging every frame.
function Exporter.LogExportState(sim, sessionEnded, sessionBegan, sessionSwitched)
  local d = gatherDiagnostics(sim)
  local signature = buildSignature(d, sessionEnded, sessionBegan, sessionSwitched)
  if signature ~= S.lastExportDiagnostic then
    S.lastExportDiagnostic = signature
    logStateLine(d, sessionEnded, sessionBegan, sessionSwitched)
  end
end

local function hasEndSignal(sim, sessionSwitched)
  local currentSessionKey = Sess.GetCurrentSessionKey(sim)
  local currentSessionFolder = Sess.SessionTypeToFolder(Sess.ReadLiveSessionType(sim))
  local sessionTimeLeft = readSessionTimeLeft(sim)
  local playerInPit = Snap.IsPlayerInPit()
  local switchedAwayFromTrackedSession = sessionSwitched
                        and S.liveSessionType ~= nil
                        and S.liveSessionKey ~= nil
                        and currentSessionKey ~= S.liveSessionKey
  local sessionName = Sess.GetSessionNameLower(sim)
  local lostTrackedSessionData = S.driverCount <= 0
                        and S.liveSessionType ~= nil
                        and S.liveSessionCarCount > 0
                        and (currentSessionFolder == nil or sessionName == "")
  local timedSessionExpiredInPit = S.liveSessionType ~= nil
                        and S.liveSessionHadCountdown
                        and sessionTimeLeft ~= nil
                        and sessionTimeLeft <= 0
                        and playerInPit == true
  return (S.lastSessionStarted == true and sim.isSessionStarted ~= true)
                        or switchedAwayFromTrackedSession
                        or lostTrackedSessionData
                        or timedSessionExpiredInPit
end

-- Derives this frame's end/begin/switch flags from the previous and current sim state.
local function computeTransitions(sim)
  local sessionSwitched = S.lastSessionIndex ~= nil
                        and S.lastSessionIndex ~= sim.currentSessionIndex
  local endSignal = hasEndSignal(sim, sessionSwitched)
  local sessionNeedsExport = S.liveSessionType ~= nil
                        and (S.liveSessionPending or S.exportedSessionKey ~= S.liveSessionKey)
  local sessionEnded = endSignal and sessionNeedsExport
  local sessionBegan = S.lastSessionStarted ~= nil and S.lastSessionStarted == false
                        and sim.isSessionStarted == true and S.lastSessionIndex ~= nil
  local newlyEnded = sessionEnded and not S.liveSessionPending
  local sessionChanged = newlyEnded or sessionBegan or sessionSwitched
  return sessionChanged, sessionEnded, sessionBegan, sessionSwitched
end

local function handleSessionEnded(sim, sessionSwitched, sessionBegan)
  -- Refresh the old snapshot only if AC is still showing that same session. If the
  -- index already moved, the live cars belong to the incoming state (or are gone).
  if not sessionSwitched and S.driverCount > 0 then
    local ok, err = pcall(Snap.CaptureLiveCarResults, true)
    if not ok then U.Log("EXPORT: final car snapshot failed: " .. tostring(err)) end
  end
  Snap.ArmSessionEnded()
  if sessionSwitched or sessionBegan then
    S.resultResetAfterExport = true
  end
  Exporter.CheckForFinishedSessionSafely(sim)
end

local function handleSessionStartedOrSwitched(sim)
  if S.liveSessionPending then
    -- Don't discard a result that still needs retries just because AC began its next
    -- session. Finish exporting the previous snapshot first.
    S.resultResetAfterExport = true
    Exporter.CheckForFinishedSessionSafely(sim)
  else
    Exporter.ResetResultExport()
    S.resultResetAfterExport = false
    Exporter.CaptureLiveSessionSafely(sim)
  end
end

local function finishDeferredReset(sim)
  if S.resultResetAfterExport and not S.liveSessionPending then
    Exporter.ResetResultExport()
    S.resultResetAfterExport = false
    if sim.isSessionStarted == true then
      Exporter.CaptureLiveSessionSafely(sim)
    end
  end
end

-- Process the export-related session transitions in one place so their ordering is
-- testable. If the session ends on the same frame that AC switches to its pit/garage
-- session, retain and export the ended session snapshot instead of clearing it as a new
-- session. A later session begin/switch still resets the previous export state.
function Exporter.UpdateResultExportFrame(sim)
  local sessionChanged, sessionEnded, sessionBegan, sessionSwitched = computeTransitions(sim)

  Exporter.LogExportState(sim, sessionEnded, sessionBegan, sessionSwitched)

  if sessionEnded then
    handleSessionEnded(sim, sessionSwitched, sessionBegan)
  elseif sessionBegan or sessionSwitched then
    handleSessionStartedOrSwitched(sim)
  elseif S.resultResetAfterExport then
    Exporter.CheckForFinishedSessionSafely(sim)
  else
    Exporter.CaptureLiveSessionSafely(sim)
    Exporter.CheckForFinishedSessionSafely(sim)
  end

  finishDeferredReset(sim)

  S.lastSessionIndex = sim.currentSessionIndex
  S.lastSessionStarted = sim.isSessionStarted
  return sessionChanged, sessionEnded, sessionBegan, sessionSwitched
end

return Exporter
