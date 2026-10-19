-- All mutable app state, shared by every module through this single table.
-- Field names are identical to the file-level locals of the old single-file app.
local S = {}

-- Persisted user settings. ac.storage rejects undeclared keys, so every key is declared here.
S.storedSettings = ac.storage({
  enduranceEnabled = true,
  resultExportEnabled = true,
  resultFolder = "",
})

-- Grid / session tracking
S.driverCount               = 0
S.allDriversStartingPos     = {}
S.rollingStartGridPositions = {}
S.firstFrame                = true
S.leaderboardOpened         = false
S.lastSessionIndex          = nil
S.lastSessionStarted        = nil
S.playerStoppedState        = false
S.raceDisplayStable         = nil
S.raceDisplayPending        = nil
S.gridReordered             = false
S.classReloadAttempted      = false
-- Armed when a started session ends (in-place restart) so the next pre-start re-stages the field
-- even though AC reuses the same session index/name and does not fire ac.onSessionStart.
S.rollingPendingReinit      = false

-- Class assignment
S.driverClass       = {}
S.displayClass      = {}
S.classOrder        = {}
S.releaseClasses    = {}
S.manualDriverClass = {}
S.classAddInput     = ""
S.classAddError     = nil
S.carClassCache     = {}
S.uiSourceCache     = {}

-- Settings mirrored from storedSettings
S.enduranceEnabled   = true
S.resultExportEnabled = true
S.resultFolder       = ""

-- Result export
S.resultExportStatus         = ""
S.exportedSessionKey         = nil
S.nextExportRetryAt          = 0
S.resultResetAfterExport     = false
S.lastExportDiagnostic       = nil
S.lastExportCaptureError     = nil
S.lastExportUpdateDiagnostic = nil
S.lastExportTransitionError  = nil

-- Snapshot of the session that is currently running. It is captured every frame while
-- the session is alive because by the time the session has ended Assetto Corsa has
-- already discarded the display name, the session index and the lap count, and the car
-- may have been moved back to the pits. The result is therefore written from this
-- snapshot, never from whatever the live state happens to be at the end.
S.liveSessionType           = nil
S.liveSessionKey            = nil
S.liveTrackName             = ""
S.liveTotalLaps             = 0
S.liveSessionPending        = false
S.liveSessionCars           = {}
S.liveSessionCarCount       = 0
S.liveSessionTimeLeft       = nil
S.liveSessionHadCountdown   = false
S.finishedRacePositions     = {}
S.liveRollingStartLapOffset = 0

-- Rolling start
S.rollingStartState          = "idle"
S.rollingStartStatus         = ""
S.rollingStartGroups         = {}
S.rollingStartCars           = {}
-- Flat front-to-back release order (overall leader first). Cars are released in class-relative
-- two-car rows, one class after another, on the shared release timer.
S.rollingStartQueue          = {}
S.rollingStartRaceHasStarted = false
S.rollingStartReleaseTimer   = 0.0
S.rollingStartSessionTime    = 0.0
S.rollingStartLastFrame      = nil
S.rollingStartSessionKey     = nil
S.rollingStartDiagFrame      = 0
-- Which physical side the odd-numbered grid slots occupy during the rolling-start formation.
-- true = P1/P3/... form on the right, false = on the left (the reference app default). This is
-- re-detected from the human car's actual grid side every session and can be overridden in the
-- config window for the current session.
S.rollingStartPoleOnRight    = false
-- Set once per session by DetectRollingStartPoleSide: "auto" when measured from the grid,
-- "manual" when the player toggled it afterwards.
S.rollingStartPoleSideSource = "auto"
-- One-shot guard for the lane-placement diagnostic (see MaintainRollingFormation).
S.rollingStartLaneDiagDone   = false
-- physics.blockTeleportingToPits() disposable, held only while the formation is running.
S.rollingPitBlock            = nil


-- Debug log
S.debugLogFile = nil

return S
