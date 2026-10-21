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
-- Rolling-start music: seconds before the end of the track that should line up with the green light
-- (0 = the very end), and the largest playback-rate trim used to stay in sync.
C.ROLLING_MUSIC_END_LEAD_SECONDS = 0.0
C.ROLLING_MUSIC_RATE_RANGE       = 0.04
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

-- Full course yellow. A hazard (a car that was racing and then stops, crashes or crawls) puts the
-- whole track under yellow: a countdown clip plays, then every car is held to YELLOW_SPEED_KMH with
-- no closing on the car ahead until every hazard is gone, followed by a green countdown clip.
C.YELLOW_SPEED_KMH            = 80
-- Hazard detection. A car is "slow" below SLOW_KMH for SLOW_SECONDS, but that low-confidence rule is
-- skipped for SLOW_COOLDOWN_SECONDS after a green and when another car within CORNER_RADIUS_M is
-- also slow (under CORNER_FACTOR times its speed + 10): a queue in a slow corner. Under STOPPED_KMH
-- for STOPPED_SECONDS it is a hazard at once; a crash is a drop from DROP_FROM_KMH to under
-- DROP_TO_KMH within DROP_SECONDS. A hazard is released once above SLOW_KMH times RECOVER_FACTOR
-- for RECOVER_SECONDS, and lives at least MIN_ZONE_SECONDS. Detection is armed once the race leader
-- has completed MIN_LAP_COUNT laps (AC counts the formation lap as 1, so 2 means the first racing
-- lap is done).
C.YELLOW_SLOW_KMH             = 45
C.YELLOW_SLOW_SECONDS         = 3.0
C.YELLOW_SLOW_COOLDOWN_SECONDS = 30.0
C.YELLOW_CORNER_RADIUS_M      = 150
C.YELLOW_CORNER_FACTOR        = 1.5
C.YELLOW_RECOVER_FACTOR       = 1.15
C.YELLOW_RECOVER_SECONDS      = 1.5
C.YELLOW_MIN_ZONE_SECONDS     = 15.0
C.YELLOW_STOPPED_KMH          = 20
C.YELLOW_STOPPED_SECONDS      = 0.15
C.YELLOW_DROP_FROM_KMH        = 110
C.YELLOW_DROP_TO_KMH          = 30
C.YELLOW_DROP_SECONDS         = 1.5
C.YELLOW_MOVING_KMH           = 60
C.YELLOW_MIN_LAP_COUNT        = 2
-- Once yellow, the field stays under it for at least MIN_HOLD_SECONDS before the green countdown.
C.YELLOW_MIN_HOLD_SECONDS      = 5.0
-- A hazard AI car still on track after the leader has covered one more lap is sent to the pits by
-- raising its stop counter (AC moves a long-stopped AI car there); the human car's own hazard is
-- ignored after PLAYER_HAZARD_MAX_S.
C.YELLOW_TOW_COUNTER          = 30.0
C.YELLOW_TOW_RETRY_SECONDS    = 5.0
C.YELLOW_PLAYER_HAZARD_MAX_S  = 90.0
-- Released AI cars raise their top speed by RELEASE_KMH_PER_S up to RELEASE_MAX_KMH before getting
-- full control back; the slow rule is not applied to them for RELEASE_GRACE_SECONDS after green.
C.YELLOW_RELEASE_KMH_PER_S    = 40
C.YELLOW_RELEASE_MAX_KMH      = 400
C.YELLOW_RELEASE_GRACE_SECONDS = 8.0
-- setAITopSpeed only limits throttle (the AI does not brake down to it), so cars above the limit are
-- slowed by easing their velocity down: DECEL_MPS2 plus DECEL_PER_KMH for each km/h over it, up to
-- DECEL_MAX_MPS2. The human car is eased down at PLAYER_DECEL_MPS2 once over it by PLAYER_OVER_KMH;
-- if setCarVelocity is unavailable, a brake assist is used instead (light braking from OVER_KMH over
-- the limit, MAX_BRAKE at FULL_OVER_KMH).
C.YELLOW_OVER_KMH             = 3.0
C.YELLOW_DECEL_MPS2           = 7.0
C.YELLOW_DECEL_PER_KMH        = 0.1
C.YELLOW_DECEL_MAX_MPS2       = 15.0
C.YELLOW_PLAYER_DECEL_MPS2    = 10.0
C.YELLOW_PLAYER_OVER_KMH      = 1
C.YELLOW_PLAYER_FULL_OVER_KMH = 40
C.YELLOW_PLAYER_MIN_BRAKE     = 0.1
C.YELLOW_PLAYER_MAX_BRAKE     = 0.8
C.YELLOW_MIN_GAP_M            = 10
C.YELLOW_FULL_GAP_M           = 40
C.YELLOW_MIN_GAP_FACTOR       = 0.6
C.YELLOW_CAUTION              = 1.0
C.YELLOW_LOG_SECONDS          = 1.0
-- Length of yellow_flag.mp3 / green_flag.mp3. If a clip cannot play, a timer of this length (plus
-- AUDIO_FALLBACK_MARGIN) stands in for it. The green panel shows for GREEN_PANEL_SECONDS.
C.YELLOW_AUDIO_SECONDS        = 8.7
C.YELLOW_GREEN_AUDIO_SECONDS  = 8.1
C.YELLOW_AUDIO_FALLBACK_MARGIN = 1.0
C.YELLOW_GREEN_PANEL_SECONDS  = 4.0
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
