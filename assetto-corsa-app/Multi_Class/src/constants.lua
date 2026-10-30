-- Immutable tuning values and enums. Nothing in here changes at runtime.
local C = {}

C.DEBUG_ENABLED = true

-- ui.MediaPlayer:setVolume only accepts 0..1 (values above 1 are not amplification), so 1.0 is the loudest.
C.AUDIO_VOLUME = 1.0

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
C.AUTOPILOT_OFF_WARNING_SECONDS     = 10.0
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
-- Inside PHASE1_MIN_GAP the follower runs below the car ahead's speed, by GAP_GAIN km/h for every
-- metre it is short of the gap (capped at YIELD_KMH once alongside or past). Aiming at the ahead
-- car's speed or above let a slightly faster car creep alongside and pass, so the class reached
-- the double-file zone out of grid order. YIELD_FLOOR keeps a car that slipped past a slow/stalled
-- car rolling while it drops back.
C.ROLLING_START_PHASE1_GAP_GAIN_KMH = 2.0
C.ROLLING_START_PHASE1_YIELD_KMH    = 25.0
C.ROLLING_START_PHASE1_YIELD_FLOOR_KMH = 50.0
-- Double-file lane guard: whatever is physically ahead in a car's own lane caps its speed once the
-- centre-to-centre gap drops below GUARD_GAP, matching that car's speed at HOLD_GAP (about a car
-- length plus margin) and slowing GAIN km/h per metre closer, so a car never drives into the back
-- of another one even if the field arrives at the formation point out of order.
C.ROLLING_START_LANE_GUARD_GAP      = 8.0
C.ROLLING_START_LANE_GUARD_HOLD_GAP = 5.5
C.ROLLING_START_LANE_GUARD_GAIN_KMH = 4.0
-- Class guard: every car also watches the previous class's cars. Inside CLASS_GUARD_GAP metres it
-- is capped to that car's speed plus (gap - CLASS_HOLD_GAP) * GAIN km/h, so a class arriving at 100
-- km/h never runs into the previous class's tail after that tail slows to the double-file speed,
-- and the gap between classes is kept at roughly CLASS_HOLD_GAP metres or more.
C.ROLLING_START_CLASS_GUARD_GAP     = 70.0
C.ROLLING_START_CLASS_HOLD_GAP      = 30.0
C.ROLLING_START_CLASS_GUARD_GAIN_KMH = 2.5
-- Release cadence: the class leaves strictly in grid order, one car every CAR_SECONDS, so the
-- run-up is a true single file and the double-file rows pair P1+P2, P3+P4, ... Each class starts
-- CLASS_GAP_SECONDS after the previous class's final car, so the classes never bunch on the run-up.
C.ROLLING_START_CAR_SECONDS         = 0.7
-- A car is only released once the car ahead of it has actually started moving.
-- Rolling-start music: seconds before the end of the track that should line up with the green light
-- (0 = the very end), and the largest playback-rate trim used to stay in sync.
C.ROLLING_MUSIC_END_LEAD_SECONDS = 0.0
C.ROLLING_MUSIC_RATE_RANGE       = 0.04
C.ROLLING_START_RELEASE_MOVED_METERS     = 10
C.ROLLING_START_RELEASE_MOVED_KMH        = 20
C.ROLLING_START_RELEASE_WAIT_TIMEOUT     = 10.0
-- A reference car that has been crawling this long (stall watcher time) is skipped by the release
-- gate, so a stalled car does not hold the whole queue behind it.
C.ROLLING_START_RELEASE_STALL_SKIP_SECONDS = 1.0
-- A held car this close in front of (and in the lane of) a released car is released too.
C.ROLLING_START_BLOCKER_AHEAD_M            = 16.0
C.ROLLING_START_BLOCKER_LATERAL_M          = 2.0
-- A car just released is given this long to get going before it counts as stuck. It has to be
-- shorter than the release cadence, otherwise the gate never applies and every car is let out
-- while the one in front is still crawling.
C.ROLLING_START_RELEASE_GRACE_SECONDS    = 0.3
-- After this long the gate is ignored entirely, so the back of the grid is never left standing
-- long enough for AC to teleport it to the pits.
C.ROLLING_START_RELEASE_MAX_SECONDS      = 50.0
-- A car running this far behind its release slot gets a progressively looser gate, and none at all
-- from GATE_RELAX_END_SECONDS: the last cars in the field must leave well before AC (~70 s) sends
-- them to the pits.
C.ROLLING_START_GATE_RELAX_START_SECONDS = 4.0
C.ROLLING_START_GATE_RELAX_END_SECONDS   = 14.0
-- Launch ramp after release: throttle limit and top speed climb gradually so cold-tyre cars do not spin.
-- The start values are deliberately high: a car launched at 25 km/h with a quarter throttle creeps
-- along at 5-15 km/h, and the next car (released 0.7 s later) is then let out on top of it, which
-- makes its AI refuse to accelerate. That is how a single bad launch turns into a chain of stopped
-- cars. Starting at 45 km/h and half throttle gets every car properly rolling within a second.
C.ROLLING_START_LAUNCH_RAMP_SECONDS      = 4.0
C.ROLLING_START_LAUNCH_THROTTLE_START    = 0.45
C.ROLLING_START_LAUNCH_SPEED_START_KMH   = 45
C.ROLLING_START_LAUNCH_LANE_SCALE        = 0.5
C.ROLLING_START_CLASS_GAP_SECONDS   = 3.0
-- A released car that is still crawling STALL_GRACE_SECONDS after its release has not launched.
-- Stage 1 hands it back full throttle (and skips the launch ramp), stage 2 gives it a small
-- velocity nudge. Without this the car sits on the grid and the cars behind drive past it.
C.ROLLING_START_STALL_SPEED_KMH          = 8.0
C.ROLLING_START_STALL_GRACE_SECONDS      = 1.2
C.ROLLING_START_STALL_STAGE1_SECONDS     = 0.4
C.ROLLING_START_STALL_STAGE2_SECONDS     = 1.5
C.ROLLING_START_STALL_NUDGE_MPS          = 5.0
-- AC resets an AI to the pits once its internal "stopped for" timer (physics.setAIStopCounter)
-- exceeds a few seconds. While a car is held on the grid we must keep that counter near zero, or
-- the longest-held cars at the back get teleported to the pits. This small value matches the
-- working rolling-start reference app.
C.ROLLING_START_STOP_COUNTER_HELD   = 0.5
-- Minimum track length required before the procedure is allowed to run.
C.ROLLING_START_MIN_TRACK_LENGTH_M  = 500
-- A starting-grid measurement (which side of its row a car sits on) is only trusted while the cars
-- are standing still on the grid. AC resets an in-place restart asynchronously, so the cars can
-- still be in the aborted attempt's formation when the measurement is taken, and the very same
-- geometry there would mirror every column of the field.
C.ROLLING_START_GRID_MAX_SPEED_KMH = 5.0

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
C.YELLOW_MIN_HOLD_SECONDS      = 30.0
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
-- The limit never snaps to YELLOW_SPEED_KMH: every car gets its own limit that starts at the speed
-- it had when the yellow started and walks down to the target at RAMP_DOWN_MPS2 (a realistic GT
-- braking rate), so the field slows down gradually. The old code rewrote the whole velocity vector
-- at up to 15 m/s^2 without any tyre force: the cars kept their old yaw rate while losing speed,
-- turned tighter than their path and spun.
C.YELLOW_OVER_KMH             = 3.0
C.YELLOW_RAMP_DOWN_MPS2       = 6.0
-- The AI brakes with its own tyres: STOP_COUNTER is CSP's "AI brakes" timer (the same one the
-- rolling start uses to hold cars on the grid) and is refreshed every frame, BRAKE_HINT_SCALE caps
-- the brake pressure the AI may build up (BRAKE_HINT is the pedal-to-pressure scale in ai.ini, the
-- original value is read once per car and restored when the yellow ends). The velocity is only
-- eased down as a last resort, when the AI did not slow down in time (see NUDGE_*).
C.YELLOW_AI_STOP_COUNTER      = 0.2
-- The brake hint is the pedal-to-pressure scale, so this caps how much brake pressure the AI may
-- build up: enough to track RAMP_DOWN_MPS2 (a GT car brakes at roughly 10 m/s^2, so 0.7 leaves
-- headroom) but never a panic stop that would lock the wheels in a corner.
C.YELLOW_BRAKE_HINT_SCALE     = 0.7
C.YELLOW_NUDGE_GRACE_SECONDS  = 1.5
C.YELLOW_NUDGE_DECEL_MPS2     = 4.0
-- The gap factor (a car right behind another one gets a lower limit) is smoothed, so the target
-- limit never jumps when a car ahead comes closer.
C.YELLOW_GAP_SMOOTH_SECONDS   = 2.0
-- The human car is only braked (the velocity is never rewritten, so steering stays free): light
-- braking (MIN_BRAKE) from OVER_KMH over the limit, up to MAX_BRAKE at FULL_OVER_KMH.
C.YELLOW_PLAYER_OVER_KMH      = 1
C.YELLOW_PLAYER_FULL_OVER_KMH = 25
C.YELLOW_PLAYER_MIN_BRAKE     = 0.15
C.YELLOW_PLAYER_MAX_BRAKE     = 1.0
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
-- Lapped-car rule (blue flag). When a car that is a lap or more up has passed an AI car (it is
-- PASSED_M..ENGAGE_GAP_M ahead of it), the lapped car's top speed is capped at HOLD_FACTOR times the
-- passer's speed (never below MIN_SPEED_KMH) until the passer is RELEASE_GAP_M ahead, so it does not
-- pass back. Nothing else about the car is changed.
C.BLUE_PASSED_M              = 4
C.BLUE_ENGAGE_GAP_M          = 40
C.BLUE_RELEASE_GAP_M         = 70
C.BLUE_HOLD_FACTOR           = 0.98
C.BLUE_MIN_SPEED_KMH         = 50
C.BLUE_SCAN_SECONDS          = 0.25

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
