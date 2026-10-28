-- All mutable app state, shared by every module through this single table.
-- Field names are identical to the file-level locals of the old single-file app.
local C = require("src/constants")
local S = {}

-- Persisted user settings. ac.storage rejects undeclared keys, so every key is declared here.
S.storedSettings = ac.storage({
  enduranceEnabled = true,
  resultExportEnabled = true,
  resultFolder = "",
  rollingMusicEnabled = true,
  rollingMusicVolume = C.AUDIO_VOLUME,
  flagSoundEnabled = true,
  flagSoundVolume = C.AUDIO_VOLUME,
  yellowFlagEnabled = true,
  autopilotEnabled = false,
  blueFlagEnabled = true,
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
-- Armed when ac.onSessionStart reports an in-place restart (or when a started session ends in
-- place). While set, a staging attempt that finds the race still moving is retried on the next
-- frame instead of permanently marking the rolling start unavailable: right after a restart the
-- sim still reports the aborted attempt's state for a few frames.
S.rollingRestartPending    = false

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
-- enduranceEnabled has to be known before the first frame: while it is off, the frame
-- update returns early and never reaches the class-loading path that restores settings.
S.enduranceEnabled   = S.storedSettings.enduranceEnabled ~= false
S.resultExportEnabled = true
S.resultFolder       = ""
S.rollingMusicEnabled = S.storedSettings.rollingMusicEnabled ~= false
S.rollingMusicVolume  = C.AUDIO_VOLUME
S.flagSoundEnabled    = S.storedSettings.flagSoundEnabled ~= false
S.flagSoundVolume     = C.AUDIO_VOLUME
S.yellowFlagEnabled   = S.storedSettings.yellowFlagEnabled ~= false
S.autopilotEnabled    = S.storedSettings.autopilotEnabled == true
S.blueFlagEnabled     = S.storedSettings.blueFlagEnabled ~= false
-- Lapped-car yielding: per-car phase tables (see blue/blue.lua).
S.blueCars            = {}
if S.storedSettings.rollingMusicVolume ~= nil then S.storedSettings.rollingMusicVolume = C.AUDIO_VOLUME end
if S.storedSettings.flagSoundVolume ~= nil then S.storedSettings.flagSoundVolume = C.AUDIO_VOLUME end

-- Full course yellow: "green" -> "yellow_pending" (countdown clip) -> "yellow" -> "green_pending"
-- (green clip) -> "green". yellowPlayerActive is true while the yellow panel shows.
S.fcy                 = "green"
S.fcyAt               = 0.0
S.yellowHazards       = {}
S.yellowCars          = {}
S.yellowArmed         = false
S.yellowLeaderDist    = 0.0
S.yellowGuardUntil    = 0.0
S.yellowSlowCooldownUntil = 0.0
S.yellowPlayerActive  = false
S.yellowClock         = 0.0
S.yellowSlowKmh       = nil
S.yellowGreenUntil    = 0.0

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
S.rollingSkipSessionKey      = nil   -- race session that was running when a replay took over
S.rollingStartGroups        = {}
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
-- The starting grid and the pole side are captured once per session. AC's in-place restart reuses
-- the same grid, but the cars are not back on it when the restart is detected: capturing again
-- there would record the live race classification instead of the grid slots, and the pole side
-- would be measured from the aborted attempt's formation and mirror the whole field.
S.rollingStartGridCaptured  = false
-- Which side of its two-wide grid row each car starts on (+1 = right of its row partner,
-- -1 = left). Measured once per session, for the same reason as the capture above.
S.rollingStartStartSides    = {}
-- One-shot guard for the lane-placement diagnostic (see MaintainRollingFormation).
S.rollingStartLaneDiagDone   = false
-- physics.blockTeleportingToPits() disposable, held only while the formation is running.
S.rollingPitBlock            = nil


-- Debug log
S.debugLogFile = nil

return S
