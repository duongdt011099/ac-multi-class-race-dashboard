-- Full course yellow. A hazard (a car that was racing and then stops, crashes or crawls) puts the whole
-- track under yellow: a countdown clip plays, then every car is held to the yellow speed with no
-- closing on the car ahead until every hazard is gone, followed by a green countdown clip.
-- States (S.fcy): green -> yellow_pending -> yellow -> green_pending -> green.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local RCtl = require("src/rolling/control")
local Audio = require("src/audio")

local sFormat = string.format
local getCar = ac.getCar

local M = {}

local LOOKAHEAD_M = C.YELLOW_FULL_GAP_M + 20

-- Mirrors the leaderboard on the sim's own track flag: yellow while the yellow panel shows, green
-- while the green panel shows once the hazards are cleared, and back to AC's handling (None) after.
-- Only sent on a change so AC's flag handling is not fought every frame.
local function syncTrackFlag()
  local want = ac.FlagType.None
  if S.yellowPlayerActive then
    want = ac.FlagType.Caution
  elseif (S.yellowGreenUntil or 0) > (S.yellowClock or 0) then
    want = ac.FlagType.Start
  end
  if S.yellowTrackFlag == want then return end
  local ok, err = pcall(physics.overrideRacingFlag, want)
  if ok then
    S.yellowTrackFlag = want
    U.Log("YELLOW: track flag set to " .. tostring(want))
  else
    U.Log("YELLOW: could not set track flag: " .. tostring(err))
  end
end

local YELLOW_SCAN_INTERVAL_GREEN = 0.75
local YELLOW_SCAN_INTERVAL_YELLOW_PENDING = 1.0
local YELLOW_SCAN_INTERVAL_YELLOW = 3.5
local YELLOW_SCAN_INTERVAL_GREEN_PENDING = 1.0
local YELLOW_SCAN_INTERVAL_DEFAULT = 1.0

-- Longest stretch of time one scan may account for (a pause or a long hitch must not skip a timer).
local MAX_SCAN_DT = 5.0

local function getYellowScanInterval()
  local st = S.fcy
  if st == "green" then return YELLOW_SCAN_INTERVAL_GREEN end
  if st == "yellow_pending" then return YELLOW_SCAN_INTERVAL_YELLOW_PENDING end
  if st == "yellow" then return YELLOW_SCAN_INTERVAL_YELLOW end
  if st == "green_pending" then return YELLOW_SCAN_INTERVAL_GREEN_PENDING end
  return YELLOW_SCAN_INTERVAL_DEFAULT
end

local function carState(i)
  local st = S.yellowCars[i]
  if not st then
    st = {
      slow = 0.0, ok = 0.0, moved = false, hazard = false, limited = false,
      rampCap = nil,      -- the car's own limit, walking down to the target
      releaseCap = nil,   -- the limit walking back up after a green
      gapF = nil,         -- smoothed gap factor
      nudgeT = 0.0,       -- how long the AI was told to brake without slowing down
      brakeHintOrig = nil,
      brakeHintApplied = nil,
    }
    S.yellowCars[i] = st
  end
  return st
end

local function inPit(car)
  return car.isInPitlane == true or car.isInPit == true
end

local function progress(car)
  if type(car.splinePosition) ~= "number" then return nil end
  return U.NormalizeTrackProgress(car.splinePosition)
end

-- true when another car close by is also moving slowly: a queue in a slow corner, not a hazard.
local function cornerTraffic(i, car, trackLength)
  local p = progress(car)
  if not p then return false end
  local limit = (car.speedKmh or 0) * C.YELLOW_CORNER_FACTOR + 10
  for j = 0, S.driverCount - 1 do
    local other = j ~= i and getCar(j) or nil
    local ost = other and S.yellowCars[j]
    if other and not (ost and ost.hazard) and not inPit(other) and (other.speedKmh or 0) < limit then
      local op = progress(other)
      if op then
        local dist = math.abs((op - p + 0.5) % 1.0 - 0.5) * trackLength
        if dist <= C.YELLOW_CORNER_RADIUS_M then return true end
      end
    end
  end
  return false
end

-- Updates one car's hazard flag; returns true while the car is a hazard.
local function updateHazard(i, car, dt, trackLength)
  local st = carState(i)
  local speed = car.speedKmh or 0

  if inPit(car) then
    st.slow, st.ok, st.moved = 0.0, 0.0, false
    if st.hazard then
      st.hazard, st.ignored = false, false
      U.Log(sFormat("YELLOW: car %d reached the pits; hazard ended after %.0f s", i, S.yellowClock - (st.hazardAt or 0)))
    end
    return false
  end

  if speed >= C.YELLOW_MOVING_KMH then st.moved = true end

  -- Recent peak speed: tracks a sudden drop (a crash) before the car has fully stopped.
  if speed >= (st.peak or 0) then
    st.peak, st.peakT = speed, 0.0
  else
    st.peakT = (st.peakT or 0.0) + dt
    if st.peakT > C.YELLOW_DROP_SECONDS then st.peak, st.peakT = speed, 0.0 end
  end

  if st.hazard then
    if speed > C.YELLOW_SLOW_KMH * C.YELLOW_RECOVER_FACTOR then
      st.ok = st.ok + dt
      -- A hazard lives for at least MIN_ZONE_SECONDS so it cannot flicker on and off.
      if st.ok >= C.YELLOW_RECOVER_SECONDS and S.yellowClock - st.hazardAt >= C.YELLOW_MIN_ZONE_SECONDS then
        st.hazard, st.ignored, st.slow, st.stopT, st.ok = false, false, 0.0, 0.0, 0.0
        U.Log(sFormat("YELLOW: car %d moving again; hazard ended after %.0f s", i, S.yellowClock - st.hazardAt))
      end
    else
      st.ok = 0.0
    end
    return st.hazard
  end

  -- Under yellow (and just after it) every car is slow on purpose: only a real stop or a crash counts.
  local guarded = S.fcy ~= "green" or S.yellowClock < S.yellowGuardUntil or st.limited or st.releasing
  local slowLimit = C.YELLOW_SLOW_KMH

  local reason
  if st.moved then
    st.stopT = speed < C.YELLOW_STOPPED_KMH and (st.stopT or 0.0) + dt or 0.0
    st.slow = speed < slowLimit and st.slow + dt or 0.0
    if speed >= slowLimit then st.skipLogged = false end

    -- A merely slow car is low-confidence: not right after a green, and not when other cars nearby
    -- are slow too (a queue in a slow corner). A stop or a crash always counts.
    local slowOk = false
    if not guarded and st.slow >= C.YELLOW_SLOW_SECONDS then
      local blocked
      if S.yellowClock < S.yellowSlowCooldownUntil then
        blocked = "cooldown after green"
      elseif cornerTraffic(i, car, trackLength) then
        blocked = "corner traffic"
      end
      if blocked then
        if not st.skipLogged then
          st.skipLogged = true
          U.Log(sFormat("YELLOW: t=%.1f car %d slow rule skipped (%s), %.0f km/h", S.yellowClock, i, blocked, speed))
        end
      else
        slowOk = true
      end
    end

    if st.stopT >= C.YELLOW_STOPPED_SECONDS then
      reason = "stopped"
    elseif slowOk then
      reason = "slow"
    elseif (st.peak or 0) >= C.YELLOW_DROP_FROM_KMH and speed <= C.YELLOW_DROP_TO_KMH then
      reason = "sudden stop"
    end
  end
  if reason then
    st.hazard, st.ok = true, 0.0
    st.hazardAt = S.yellowClock
    st.leaderMark = S.yellowLeaderDist
    st.towT, st.towLogged, st.ignored = 0.0, false, false
    st.hazardP = nil
    U.Log(sFormat("YELLOW: t=%.1f car %d is %s (%.0f km/h, peak %.0f, slow limit %.0f); hazard%s",
        S.yellowClock, i, reason, speed, st.peak or 0, C.YELLOW_SLOW_KMH,
        S.yellowArmed and "" or " (not armed yet, first lap)"))
  end
  return st.hazard
end

-- The race leader by distance covered (laps + lap fraction); arms detection after the first racing lap.
local function updateLeader()
  local best = 0.0
  local bestLap = 0
  for i = 0, S.driverCount - 1 do
    local car = getCar(i)
    local p = car and progress(car)
    if p then
      local laps = car.lapCount or 0
      local dist = laps + p
      if dist > best then best, bestLap = dist, laps end
    end
  end
  S.yellowLeaderDist = best
  if not S.yellowArmed and bestLap >= C.YELLOW_MIN_LAP_COUNT then
    S.yellowArmed = true
    U.Log(sFormat("YELLOW: t=%.1f armed, the leader finished the first racing lap", S.yellowClock))
  end
end

-- Updates every car's hazard state and the list of active hazards (position where each stopped).
local function buildHazardsNow(frameDt, trackLength)
  updateLeader()
  -- Hazard state is only advanced when a scan runs, so its timers (recovery, slow, stopped, tow)
  -- have to count the time since the previous scan. Adding the single frame's dt made a hazard
  -- take minutes to clear (1.5 s of recovery at one frame per scan), so the yellow never ended.
  local dt = frameDt
  local lastScan = S.yellowHazardScanAt
  if lastScan and lastScan > -1000.0 then
    dt = math.max(frameDt, math.min(S.yellowClock - lastScan, MAX_SCAN_DT))
  end
  local hazards = {}
  for i = 0, S.driverCount - 1 do
    local car = getCar(i)
    if not car then
      S.yellowCars[i] = nil
    elseif updateHazard(i, car, dt, trackLength) then
      local st = S.yellowCars[i]
      local p = progress(car)
      -- The hazard stays where the car was slow; it does not follow it once it drives off.
      if p and (st.ok == 0.0 or not st.hazardP) then st.hazardP = p end

      -- The human car's own stop must not hold up the race for ever.
      if i == 0 and not st.ignored and S.yellowClock - st.hazardAt > C.YELLOW_PLAYER_HAZARD_MAX_S then
        st.ignored = true
        U.Log("YELLOW: the player's own stop is ignored (too long)")
      end

      -- A hazard AI car still on track a full leader lap later is sent to the pits.
      if i ~= 0 and S.fcy ~= "green" and S.yellowLeaderDist - (st.leaderMark or S.yellowLeaderDist) >= 1.0 then
        st.towT = (st.towT or 0.0) + dt
        if st.towT >= C.YELLOW_TOW_RETRY_SECONDS or not st.towLogged then
          if st.towT >= C.YELLOW_TOW_RETRY_SECONDS then st.towT = 0.0 end
          if not st.towLogged then
            st.towLogged = true
            U.Log(sFormat("YELLOW: car %d is still on track after a full lap; sending it to the pits", i))
          end
          pcall(physics.setAIStopCounter, i, C.YELLOW_TOW_COUNTER)
        end
      end

      if st.hazardP and not st.ignored then
        hazards[#hazards + 1] = { index = i, p = st.hazardP }
      end
    end
  end
  S.yellowHazards = hazards
  S.yellowHazardScanAt = S.yellowClock
end

local function buildHazards(dt, trackLength)
  if S.yellowHazardScanAt == nil then
    S.yellowHazardScanAt = -1000000.0
  end
  local interval = getYellowScanInterval()
  local force = false
  if S.fcy == "yellow" then
    if #S.yellowHazards == 0 then
      force = true
    end
  end
  if force or (S.yellowClock - S.yellowHazardScanAt) >= interval then
    buildHazardsNow(dt, trackLength)
  end
end

function M.ForceHazardScan(dt, trackLength)
  if not trackLength then
    local sim = ac.getSim()
    trackLength = sim and sim.trackLengthM
  end
  if not U.IsFinite(trackLength) or trackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then return end
  buildHazardsNow(dt or 0, trackLength)
end

local function otherHazards()
  local n = 0
  for _, h in ipairs(S.yellowHazards) do
    if h.index ~= 0 then n = n + 1 end
  end
  return n
end

local function clipDone(done, startedClock, seconds)
  if done ~= nil then return done end
  return S.yellowClock - startedClock >= seconds + C.YELLOW_AUDIO_FALLBACK_MARGIN
end

-- The whole-track state machine.
local function updateFcy(trackLength)
  local count = #S.yellowHazards
  local state = S.fcy

  if state == "green" then
    if S.yellowArmed and count > 0 then
      M.ForceHazardScan(0, trackLength)
      count = #S.yellowHazards
      if count > 0 then
        S.fcy, S.fcyAt = "yellow_pending", S.yellowClock
        if otherHazards() > 0 then Audio.PlayYellowFlag() end
        U.Log(sFormat("YELLOW: t=%.1f full course yellow pending, %d hazard(s), countdown started", S.yellowClock, count))
      end
    end
  elseif state == "yellow_pending" then
    -- The countdown always plays to the end, even if the hazard has gone by then.
    if clipDone(Audio.YellowDone(), S.fcyAt, C.YELLOW_AUDIO_SECONDS) then
      S.fcy, S.fcyAt = "yellow", S.yellowClock
      U.Log(sFormat("YELLOW: t=%.1f YELLOW, countdown finished (%d hazard(s))", S.yellowClock, count))
    end
  elseif state == "yellow" then
    if count == 0 or (S.yellowClock - S.fcyAt >= C.YELLOW_MIN_HOLD_SECONDS and S.yellowClock - (S.yellowHazardScanAt or -1000000.0) > 1.0) then
      M.ForceHazardScan(0, trackLength)
      count = #S.yellowHazards
    end
    if count == 0 and S.yellowClock - S.fcyAt >= C.YELLOW_MIN_HOLD_SECONDS then
      M.ForceHazardScan(0, trackLength)
      count = #S.yellowHazards
      if count == 0 and S.yellowClock - S.fcyAt >= C.YELLOW_MIN_HOLD_SECONDS then
        S.fcy, S.fcyAt = "green_pending", S.yellowClock
        Audio.PlayGreenFlag()
        U.Log(sFormat("YELLOW: t=%.1f every hazard is gone, green countdown started", S.yellowClock))
      end
    end
  elseif state == "green_pending" then
    if count > 0 then
      M.ForceHazardScan(0, trackLength)
      count = #S.yellowHazards
    end
    if count > 0 then
      S.fcy, S.fcyAt = "yellow", S.yellowClock
      Audio.StopGreenFlag()
      U.Log(sFormat("YELLOW: t=%.1f a new hazard appeared; back to yellow", S.yellowClock))
    elseif clipDone(Audio.GreenDone(), S.fcyAt, C.YELLOW_GREEN_AUDIO_SECONDS) then
      S.fcy = "green"
      S.yellowGuardUntil = S.yellowClock + C.YELLOW_RELEASE_GRACE_SECONDS
      S.yellowSlowCooldownUntil = S.yellowClock + C.YELLOW_SLOW_COOLDOWN_SECONDS
      if S.yellowPlayerActive then
        S.yellowGreenUntil = S.yellowClock + C.YELLOW_GREEN_PANEL_SECONDS
      end
      U.Log(sFormat("YELLOW: t=%.1f GREEN, the field is released", S.yellowClock))
    end
  end

  S.yellowPlayerActive = (S.fcy == "yellow" or S.fcy == "green_pending")
  syncTrackFlag()
end

-- The speed limit (km/h) for a car now, or nil when it may race.
local function fcyLimit(i, p, trackLength)
  -- Nobody is limited while the countdown plays: the limit starts when the clip ends.
  if S.fcy == "yellow" or S.fcy == "green_pending" then return C.YELLOW_SPEED_KMH end
  return nil
end

-- The O(N) scan for the nearest car ahead is refreshed twice a second per car; the
-- smoothing below runs every frame on the cached value.
local GAP_SCAN_SECONDS = 0.5

local function scanGapFactor(i, p, trackLength)
  local factor = 1.0
  for j = 0, S.driverCount - 1 do
    local other = j ~= i and getCar(j) or nil
    local ost = other and S.yellowCars[j]
    if other and not (ost and ost.hazard) and not inPit(other) then
      local op = progress(other)
      if op then
        local gap = ((op - p + 0.5) % 1.0 - 0.5) * trackLength
        if gap > 1.0 and gap < LOOKAHEAD_M then
          local t = (gap - C.YELLOW_MIN_GAP_M) / (C.YELLOW_FULL_GAP_M - C.YELLOW_MIN_GAP_M)
          t = math.max(0.0, math.min(1.0, t))
          factor = math.min(factor, C.YELLOW_MIN_GAP_FACTOR + (1.0 - C.YELLOW_MIN_GAP_FACTOR) * t)
        end
      end
    end
  end
  return factor
end

local function gapFactor(i, car, p, trackLength, dt)
  local st = carState(i)
  local factor = st.gapRaw
  if not factor or S.yellowClock - (st.gapRawAt or -1000.0) >= GAP_SCAN_SECONDS then
    factor = scanGapFactor(i, p, trackLength)
    st.gapRaw, st.gapRawAt = factor, S.yellowClock
  end
  -- Smooth the factor, so the target limit walks down instead of jumping when a car ahead gets
  -- closer: the limit itself is already ramped, a step here would defeat that.
  local smoothed = st.gapF or factor
  if dt and dt > 0 then
    smoothed = smoothed + (factor - smoothed) * math.min(1.0, dt / C.YELLOW_GAP_SMOOTH_SECONDS)
  else
    smoothed = factor
  end
  st.gapF = smoothed
  return smoothed
end

-- One-off log of the CSP functions the AI braking relies on.
local probed = false
local function probeAIApi()
  if probed then return end
  probed = true
  U.Log(sFormat("YELLOW: AI brake API: setAIStopCounter=%s setAIBrakeHint=%s setAITopSpeed=%s",
      tostring(physics.setAIStopCounter ~= nil),
      tostring(physics.setAIBrakeHint ~= nil),
      tostring(physics.setAITopSpeed ~= nil)))
end

-- The AI's brake hint (ai.ini [PEDALS] BRAKE_HINT) is the scale between brake pedal and brake
-- pressure, so lowering it caps how hard the AI can brake. The original value is read once per
-- car and restored when the yellow ends.
local function originalBrakeHint(i)
  local st = carState(i)
  if st.brakeHintOrig == nil then
    local value = 1.0
    local cfg
    if ac.INIConfig and ac.INIConfig.carData then
      local ok, res = pcall(ac.INIConfig.carData, i, "ai.ini")
      if ok then cfg = res end
    end
    if cfg and cfg.get then
      local ok2, v = pcall(cfg.get, cfg, "PEDALS", "BRAKE_HINT", 1.0)
      if ok2 and type(v) == "number" then value = v end
    end
    st.brakeHintOrig = value
  end
  return st.brakeHintOrig
end

-- Scale the AI's brake pressure. scale 1.0 is the car's own setting.
local function setBrakeHint(i, scale)
  local st = carState(i)
  local orig = originalBrakeHint(i)
  local value = orig * scale
  if st.brakeHintApplied == value then return end
  st.brakeHintApplied = value
  pcall(physics.setAIBrakeHint, i, value)
  if scale ~= 1.0 then
    U.Log(sFormat("YELLOW: car %d brake hint %.2f -> %.2f", i, orig, value))
  end
end

-- Ease the car's velocity toward targetKmh at no more than `decel` m/s^2. Returns true when applied.
-- Only used as a safety net, when the AI did not slow down after being told to brake: the whole
-- velocity vector is scaled, so the slip angle stays the same.
local function brakeCar(i, car, targetKmh, dt, decel)
  local vel = car.velocity
  if not (vel and physics.setCarVelocity) then return false end
  local speed = vel:length()
  if speed < 1.0 then return false end
  local newSpeed = math.max(targetKmh / 3.6, speed - (decel or C.YELLOW_NUDGE_DECEL_MPS2) * math.min(dt, 0.1))
  local ok = pcall(physics.setCarVelocity, i, vel * (newSpeed / speed))
  return ok
end

-- Holds one AI car to the yellow limit. The limit walks down from the speed the car had when the
-- yellow started, and the car brakes with its own tyres, so it slows down gradually and keeps
-- following the racing line instead of being teleported to the limit.
local function limitAI(i, car, p, trackLength, limit, dt)
  local st = carState(i)
  probeAIApi()
  local current = car.speedKmh or 0
  local target = limit * gapFactor(i, car, p, trackLength, dt)

  if not st.limited then
    st.rampCap = math.max(current, target)
    U.Log(sFormat("YELLOW: car %d limited at %.0f km/h (limit %.0f, ramping down at %.0f km/h/s)",
        i, current, limit, C.YELLOW_RAMP_DOWN_MPS2 * 3.6))
  end
  st.limited = true
  st.rampCap = math.max(target, (st.rampCap or current) - C.YELLOW_RAMP_DOWN_MPS2 * 3.6 * dt)

  local over = current - st.rampCap
  pcall(physics.setAITopSpeed, i, st.rampCap)
  pcall(physics.setAICaution, i, C.YELLOW_CAUTION)
  pcall(physics.setAIThrottleLimit, i, over > C.YELLOW_OVER_KMH and 0.0 or 1.0)

  if over > C.YELLOW_OVER_KMH then
    -- Let the AI brake with its own tyres: CSP brakes the AI while the stop counter runs, and the
    -- brake hint keeps the pressure low so the car slows down gently instead of locking up.
    pcall(physics.setAIStopCounter, i, C.YELLOW_AI_STOP_COUNTER)
    setBrakeHint(i, C.YELLOW_BRAKE_HINT_SCALE)
    -- Safety net: if the AI did not slow down in time, ease the velocity down gently.
    st.nudgeT = (st.nudgeT or 0.0) + dt
    if st.nudgeT >= C.YELLOW_NUDGE_GRACE_SECONDS then
      brakeCar(i, car, st.rampCap, dt, C.YELLOW_NUDGE_DECEL_MPS2)
    end
  else
    pcall(physics.setAIStopCounter, i, 0.0)
    setBrakeHint(i, 1.0)
    st.nudgeT = 0.0
  end

  st.logT = (st.logT or 0.0) + dt
  if st.logT >= C.YELLOW_LOG_SECONDS then
    st.logT = 0.0
    local acc = car.acceleration
    U.Log(sFormat("YELLOW: car %d v=%.0f cap=%.0f ramp=%.0f brake=%.2f gas=%.2f gz=%.1f",
        i, current, target, st.rampCap, car.brake or 0.0, car.gas or 0.0,
        (acc and acc.z) or 0.0))
  end
end

-- Green: raise the top speed gradually so the whole field does not floor it at once.
local function releaseAI(i, car, dt, immediate)
  local st = S.yellowCars[i]
  if not st then return end
  if st.limited then
    st.limited = false
    st.rampCap = nil
    st.nudgeT = 0.0
    pcall(physics.setAIStopCounter, i, 0.0)
    setBrakeHint(i, 1.0)
    if immediate then
      RCtl.RestoreAIControl(i, false)
    else
      st.releasing = true
      st.releaseCap = math.max(car.speedKmh or 0, C.YELLOW_SPEED_KMH)
    end
    U.Log(sFormat("YELLOW: car %d released%s", i, immediate and "" or " (ramping up)"))
  end
  if st.releasing then
    st.releaseCap = (st.releaseCap or 0) + C.YELLOW_RELEASE_KMH_PER_S * dt
    if st.releaseCap >= C.YELLOW_RELEASE_MAX_KMH then
      st.releasing = false
      RCtl.RestoreAIControl(i, false)
    else
      pcall(physics.setAITopSpeed, i, st.releaseCap)
      pcall(physics.setAIThrottleLimit, i, 1.0)
    end
  end
end

local function updateCars(trackLength, dt)
  for i = 1, S.driverCount - 1 do
    local car = getCar(i)
    local p = car and progress(car)
    if p then
      local st = carState(i)
      local limit = fcyLimit(i, p, trackLength)
      if limit and not st.hazard and not inPit(car) then
        st.releasing = false
        limitAI(i, car, p, trackLength, limit, dt)
      else
        releaseAI(i, car, dt, st.hazard or inPit(car))
      end
    end
  end
end

-- Speed limit for the human car: only the brakes are used (forceUserBrakesFor), proportional to how
-- far over the limit the car is. The velocity is never rewritten, so steering, tyre grip and the
-- force feedback stay natural, in corners too. Throttle and steering stay with the driver.
local function limitPlayer(player, limit, dt)
  local st = carState(0)
  local current = player.speedKmh or 0
  -- The limit walks down from the speed the car had when the yellow started, so the braking builds
  -- up gradually instead of jumping to full brake in a single frame.
  if not st.rampCap then st.rampCap = math.max(current, limit) end
  st.rampCap = math.max(limit, st.rampCap - C.YELLOW_RAMP_DOWN_MPS2 * 3.6 * dt)

  local over = current - st.rampCap
  if over <= C.YELLOW_PLAYER_OVER_KMH then return end
  local t = (over - C.YELLOW_PLAYER_OVER_KMH) / (C.YELLOW_PLAYER_FULL_OVER_KMH - C.YELLOW_PLAYER_OVER_KMH)
  t = math.max(0.0, math.min(1.0, t))
  local brake = C.YELLOW_PLAYER_MIN_BRAKE + (C.YELLOW_PLAYER_MAX_BRAKE - C.YELLOW_PLAYER_MIN_BRAKE) * t
  pcall(physics.forceUserBrakesFor, 0.1, brake)
  if not S.yellowPlayerLimited then
    S.yellowPlayerLimited = true
    U.Log(sFormat("YELLOW: t=%.1f player braking at %.0f km/h (limit %.0f, ramping to %.0f, brake %.2f)",
        S.yellowClock, current, limit, st.rampCap, brake))
  end
end
local function updatePlayer(trackLength, dt)
  local player = getCar(0)
  local p = player and progress(player)
  if not p then return end

  local st = carState(0)
  local limit = fcyLimit(0, p, trackLength)
  if limit and not st.hazard and not inPit(player) then
    limitPlayer(player, limit, dt)
  else
    S.yellowPlayerLimited = false
    st.rampCap = nil
  end
end

-- Hand every car back and forget all state.
function M.Reset()
  for i, st in pairs(S.yellowCars) do
    if (st.limited or st.releasing) and i ~= 0 then RCtl.RestoreAIControl(i, false) end
    if i ~= 0 and st.brakeHintApplied ~= nil then
      pcall(physics.setAIStopCounter, i, 0.0)
      setBrakeHint(i, 1.0)
    end
  end
  S.yellowCars = {}
  S.yellowHazards = {}
  S.yellowHazardScanAt = -1000000.0
  S.fcy = "green"
  S.fcyAt = 0.0
  S.yellowArmed = false
  S.yellowGuardUntil = 0.0
  S.yellowSlowCooldownUntil = 0.0
  S.yellowPlayerActive = false
  S.yellowPlayerLimited = false
  S.yellowGreenUntil = 0.0
  syncTrackFlag()
  Audio.ResetFlags()
end

local function shouldRun(sim)
  if not S.yellowFlagEnabled then return false end
  if sim.isSessionStarted ~= true or not Sess.IsRaceMode(sim) then return false end
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then return false end
  return not Sess.IsSessionFinished(sim)
end

function M.Update(sim, dt)
  if not shouldRun(sim) then
    if next(S.yellowCars) ~= nil or S.fcy ~= "green" or S.yellowArmed then M.Reset() end
    return
  end

  local trackLength = sim.trackLengthM
  if not U.IsFinite(trackLength) or trackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then return end

  if not S.yellowApiLogged then
    S.yellowApiLogged = true
    pcall(function()
      local names = {}
      for k, v in pairs(physics) do
        if type(k) == "string" and type(v) == "function" then names[#names + 1] = k end
      end
      table.sort(names)
      U.Log("YELLOW: physics API: " .. table.concat(names, ", "))
    end)
  end

  if not S.yellowPreloaded then
    S.yellowPreloaded = true
    Audio.PreloadFlags()
  end

  if S.yellowHazardScanAt == nil then
    S.yellowHazardScanAt = -1000000.0
  end

  S.yellowClock = S.yellowClock + dt
  buildHazards(dt, trackLength)
  updateFcy(trackLength)
  updateCars(trackLength, dt)
  updatePlayer(trackLength, dt)
end

return M
