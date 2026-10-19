-- Immutable tuning values and enums. Nothing in here changes at runtime.
local C = {}

C.DEBUG_ENABLED = true

C.RACE_POS_STABLE_FRAMES = 2
C.EXPORT_RETRY_SECONDS   = 2

-- Rolling starts are automatic in race sessions. Dashboard-launched races with a usable
-- fast_lane.ai receive one extra AC lap; the Lua result writer removes that formation segment.
--
-- The procedure mirrors the proven rapidough1s rolling-start app: the field is held on the grid,
-- released in two-car rows on a timer, runs single file up to the formation point and then stacks
-- into two columns for the start. The only addition is a per-class time gap so the classes stay
-- separated. Speeds are the reference defaults.
C.ROLLING_START_FORMATION_SPEED_KMH = 100
C.ROLLING_START_SPEED_KMH           = 70
-- Percentage of track progress where a car that has already crossed the line stacks into two
-- columns (double file) for the rolling start.
C.ROLLING_START_FORMATION_POINT     = 0.67
-- A car is considered to have rolled over the start line once its spline position drops below this.
C.ROLLING_START_ROLLED_OVER_SPLINE  = 0.3
-- Lateral distance of each column from the racing line, in metres. CSP keeps the AI on a lane with
-- the metre-based setAISplineAbsoluteOffset; single file is offset 0.
C.ROLLING_START_LANE_METERS         = 3.0
C.ROLLING_START_LANE_RAMP_MPS       = 1.5
-- After a class goes green its lane offset eases back to the racing line at this rate, and its
-- caution level ramps from 0 to 1 over GREEN_WINDOW_SECONDS (reference green-flag transition).
C.ROLLING_START_GREEN_OFFSET_RAMP_MPS = 0.5
C.ROLLING_START_GREEN_WINDOW_SECONDS  = 10.0
-- Minimum lap-fraction a class leader must have covered since leaving the grid before its next
-- start-line crossing counts as the race start. This keeps the green flag track-agnostic: tracks
-- whose grid sits before the line (first crossing immediate, green after a full lap) and tracks
-- whose grid sits after the line (first crossing is already ~a lap in, green right there) both go
-- green after a single formation lap.
C.ROLLING_START_GREEN_MIN_TRAVEL_LAP  = 0.5
-- Catch-up multiplier used by the phase-two spacing (reference GetLinearMultiplier default).
C.ROLLING_START_CATCHUP_LIMIT       = 1.5
-- Single-file run-up catch-up. Cars released later begin further back; the rear of the pack may
-- run up to PHASE1_CATCHUP times the formation speed to close on the car ahead before the
-- double-file zone, so the class is not still strung out when it stacks into columns. The boost
-- starts only beyond MIN_GAP metres and eases back to 1.0 by FULL_GAP, keeping it gentle enough
-- that a follower never overshoots into hard braking.
C.ROLLING_START_PHASE1_CATCHUP      = 1.15
C.ROLLING_START_PHASE1_MIN_GAP      = 10.0
C.ROLLING_START_PHASE1_FULL_GAP     = 80.0
-- Release cadence: the class leaves strictly in grid order, one car every CAR_SECONDS, so the
-- run-up is a true single file and the double-file rows pair P1+P2, P3+P4, ... Each class starts
-- CLASS_GAP_SECONDS after the previous class's final car, so the classes never bunch on the run-up.
C.ROLLING_START_CAR_SECONDS         = 0.7
-- A car is only released once the car ahead of it has actually started moving.
C.ROLLING_START_RELEASE_MOVED_METERS     = 6
C.ROLLING_START_RELEASE_MOVED_KMH        = 10
C.ROLLING_START_RELEASE_WAIT_TIMEOUT     = 10.0
-- A car just released is given this long to get going before it counts as stuck.
C.ROLLING_START_RELEASE_GRACE_SECONDS    = 2.5
-- After this long the gate is ignored entirely, so the back of the grid is never left standing
-- long enough for AC to teleport it to the pits.
C.ROLLING_START_RELEASE_MAX_SECONDS      = 45.0
-- Launch ramp after release: throttle limit and top speed climb gradually so cold-tyre cars do not spin.
C.ROLLING_START_LAUNCH_RAMP_SECONDS      = 5.0
C.ROLLING_START_LAUNCH_THROTTLE_START    = 0.25
C.ROLLING_START_LAUNCH_SPEED_START_KMH   = 25
C.ROLLING_START_LAUNCH_LANE_SCALE        = 0.5
C.ROLLING_START_CLASS_GAP_SECONDS   = 3.0
-- AC resets an AI to the pits once its internal "stopped for" timer (physics.setAIStopCounter)
-- exceeds a few seconds. While a car is held on the grid we must keep that counter near zero, or
-- the longest-held cars at the back get teleported to the pits. This small value matches the
-- working rolling-start reference app.
C.ROLLING_START_STOP_COUNTER_HELD   = 0.5
-- Minimum track length required before the procedure is allowed to run.
C.ROLLING_START_MIN_TRACK_LENGTH_M  = 500

-- The dashboard watches these subfolders and derives the session type from the
-- folder name, so every file must be named exactly yyMMdd-HHmmss.json.
C.SUBFOLDER_PRACTICE   = "practice"
C.SUBFOLDER_QUALIFYING = "qualifying"
C.SUBFOLDER_RACE       = "race"

-- ac.StateSession.type and ac.SimState.raceSessionType are ac.SessionType enums, which
-- survive the end of a session. The session *name* does not, which is why the export
-- used to fail whenever the car returned to the pits. Values are hardcoded as a fallback
-- for CSP builds that do not expose the enum table.
C.SESSION_TYPE_PRACTICE = (ac.SessionType and ac.SessionType.Practice) or 1
C.SESSION_TYPE_QUALIFY  = (ac.SessionType and ac.SessionType.Qualify) or 2
C.SESSION_TYPE_RACE     = (ac.SessionType and ac.SessionType.Race) or 3
C.SESSION_TYPE_HOTLAP   = (ac.SessionType and ac.SessionType.Hotlap) or 4

return C
