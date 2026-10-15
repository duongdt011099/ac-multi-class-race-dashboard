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

-- Blue-flag yielding for lapped cars.
C.BLUE_DETECT_M      = 150.0  -- faster car behind a lapped car within this distance = blue flag
C.BLUE_CLEAR_M       = 200.0  -- ...and the flag only clears beyond this (hysteresis)
C.BLUE_YIELD_M       = 100.0  -- start yielding once the faster car is this close
C.BLUE_RELEASE_M     = 100.0  -- after being passed, resume once the faster car is this far ahead
C.BLUE_THROTTLE      = 0.7
C.BLUE_THROTTLE_RAMP = 0.5    -- throttle limit change per second
C.BLUE_SPEED_FACTOR  = 0.85   -- top speed cap relative to the faster car's speed
C.BLUE_MIN_SPEED_KMH = 60.0
C.BLUE_STOPPED_KMH   = 20.0   -- slower than this the car is considered stopped / crashed
C.BLUE_LANE_METERS   = 2.0
C.BLUE_LANE_RAMP_MPS = 1.5

return C
