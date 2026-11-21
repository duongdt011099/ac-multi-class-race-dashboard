-- Race control for the track's own flag panels: blue near the human car when it laps a car or is
-- being lapped, in the same (app) blue both ways.
--
-- AC's own blue flag (physics.overrideRacingFlag(ac.FlagType.FasterCar)) feeds FLAG_TYPE, which lights
-- every flag panel on the track and also the car's dash screen. Instead, the app finds the panels
-- itself (race_control/panels.lua), gives each one its own material and colours them one by one:
--   - blue, blinking: the panels from the human car up to just past the car it is lapping;
--   - blue, blinking: the panels just ahead of the human car while a faster car is lapping it. AC
--     shows the player its own blue flag then (dash screen included, which is right); its track-wide
--     blue on the other panels is held back to their original look;
--   - yellow / green: every panel while the full course yellow (or its green panel) is out, because
--     panels with their own material may no longer follow the track config's flag condition;
--   - otherwise the panel's original look.
-- AC's racing flag is never set here, so the cockpit display does not show blue.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local Panels = require("src/race_control/panels")

local sFormat = string.format
local getCar = ac.getCar

local M = {}

local clock = 0.0
local lastScanAt = -1000.0
local blueUntil = {}      -- panel -> clock until which it stays blue
local lappedCar = nil     -- index of the car the human car is lapping (for the log only)
local lappingCar = nil    -- index of the car lapping the human car (for the log only)
local active = false      -- true while any panel is driven by the app

local function colour(c, scale)
  local k = (scale or 1.0) / 255
  return rgb(c[1] * k, c[2] * k, c[3] * k)
end

local function inPit(car)
  return car.isInPitlane == true or car.isInPit == true
end

local function progress(car)
  if type(car.splinePosition) ~= "number" then return nil end
  return U.NormalizeTrackProgress(car.splinePosition)
end

-- The car the human car is about to lap: physically ahead within the lookahead, at least
-- C.BLUE_LAPPED_MIN_LAPS behind in race distance. Returns its index, the gap in metres and the
-- human car's lap progress.
local function findLappedCar(trackLength)
  local player = getCar(0)
  local p0 = player and progress(player)
  if not p0 or inPit(player) then return nil end
  local d0 = (player.lapCount or 0) + p0
  local best, bestGap = nil, nil
  for j = 1, S.driverCount - 1 do
    if j ~= S.safetyCarIndex then
      local car = getCar(j)
      local pj = car and progress(car)
      if pj and not inPit(car) then
        local gap = ((pj - p0) % 1.0) * trackLength
        local behindLaps = d0 - ((car.lapCount or 0) + pj)
        if gap > 1.0 and gap <= C.BLUE_LOOKAHEAD_M and behindLaps >= C.BLUE_LAPPED_MIN_LAPS
            and (not bestGap or gap < bestGap) then
          best, bestGap = j, gap
        end
      end
    end
  end
  return best, bestGap, p0
end

-- The car about to lap the human car: physically behind within C.BLUE_BEHIND_M, at least
-- C.BLUE_LAPPED_MIN_LAPS ahead in race distance. When AC itself shows the player the blue flag
-- (sim.raceFlagType), the car that caused it counts too. Returns its index, the gap behind in metres
-- and the human car's lap progress.
local function findLappingCar(sim, trackLength)
  local player = getCar(0)
  local p0 = player and progress(player)
  if not p0 or inPit(player) then return nil end
  local d0 = (player.lapCount or 0) + p0
  local best, bestGap = nil, nil
  for j = 1, S.driverCount - 1 do
    if j ~= S.safetyCarIndex then
      local car = getCar(j)
      local pj = car and progress(car)
      if pj and not inPit(car) then
        local gap = ((p0 - pj) % 1.0) * trackLength
        local aheadLaps = ((car.lapCount or 0) + pj) - d0
        if gap > 1.0 and gap <= C.BLUE_BEHIND_M and aheadLaps >= C.BLUE_LAPPED_MIN_LAPS
            and (not bestGap or gap < bestGap) then
          best, bestGap = j, gap
        end
      end
    end
  end
  if not best and sim.raceFlagType == ac.FlagType.FasterCar then
    local cause = tonumber(sim.raceFlagCause) or -1
    if cause > 0 then
      local car = getCar(cause)
      local pj = car and progress(car)
      best, bestGap = cause, pj and ((p0 - pj) % 1.0) * trackLength or 0
    end
  end
  return best, bestGap, p0
end

-- How far ahead (m) the stretch from the human car to just past the lapped car first enters a group
-- of boards, or nil. The stretch is sampled every C.BLUE_GROUP_STEP_M along the AI spline.
local function groupAhead(panel, p0, fromM, toM, trackLength)
  local m = fromM
  while m <= toM do
    local pos = ac.trackProgressToWorldCoordinate((p0 + m / trackLength) % 1.0)
    if pos and Panels.GroupContains(panel, pos) then return m end
    m = m + C.BLUE_GROUP_STEP_M
  end
  return nil
end

-- The nearest panels ahead of the human car, up to toM metres ahead (just past the lapped car, or
-- C.BLUE_BEING_LAPPED_AHEAD_M while the human car is being lapped).
local function markBluePanels(p0, toM, trackLength)
  local candidates = {}
  for _, panel in ipairs(Panels.List()) do
    local ahead
    if panel.group then
      ahead = groupAhead(panel, p0, C.BLUE_MIN_AHEAD_M, toM, trackLength)
    else
      ahead = ((panel.p - p0) % 1.0) * trackLength
      if ahead < C.BLUE_MIN_AHEAD_M or ahead > toM then ahead = nil end
    end
    if ahead then
      candidates[#candidates + 1] = { panel = panel, ahead = ahead }
    end
  end
  table.sort(candidates, function(a, b) return a.ahead < b.ahead end)
  for n = 1, math.min(#candidates, C.BLUE_MAX_PANELS) do
    blueUntil[candidates[n].panel] = clock + C.BLUE_HOLD_SECONDS
  end
  return #candidates
end

local DARK = rgb(0, 0, 0)
local drawHandle = nil
local readbackAt = nil    -- clock at which to log what a blue panel really shows (once per lapping)

-- Decides every panel's colour for this frame (panel.want / panel.wantKey); nil = original look.
local function decideColours(sim)
  local blink = math.floor(clock / C.BLUE_BLINK_SECONDS) % 2 == 0
  -- Same moments the yellow module puts AC's own track flag to yellow / green (syncTrackFlag).
  local fcyYellow = S.yellowPlayerActive == true
  local fcyGreen = (S.yellowGreenUntil or 0) > (S.yellowClock or 0)
  -- While AC shows the player its own blue flag, the track config lights every panel in the track's
  -- blue. Panels the app is not colouring are held at their original look, so only the app's blue
  -- (near the car) shows.
  local holdOriginal = sim.raceFlagType == ac.FlagType.FasterCar
  local any = false
  for _, panel in ipairs(Panels.List()) do
    local want, key, force = nil, "off", false
    if fcyYellow then
      want, key = blink and colour(C.PANEL_YELLOW_RGB, C.PANEL_BRIGHTNESS) or DARK, blink and "yellow" or "dark"
    elseif fcyGreen then
      want, key = colour(C.PANEL_GREEN_RGB, C.PANEL_BRIGHTNESS), "green"
    elseif (blueUntil[panel] or -1) > clock then
      want, key = blink and colour(C.PANEL_BLUE_RGB, C.PANEL_BLUE_BRIGHTNESS) or DARK, blink and "blue" or "dark"
    else
      blueUntil[panel] = nil
      if holdOriginal then key, force = "original", true end
    end
    panel.want, panel.wantKey, panel.force = want, key, force or want ~= nil
    if panel.force then any = true end
  end
  active = any
end

-- Writes the decided colours. Driven panels are written every time (the track config's flashing
-- flag adjustments rewrite ksEmissive each frame); the rest only when they change back.
local function pushColours()
  for _, panel in ipairs(Panels.List()) do
    Panels.Set(panel, panel.want, panel.wantKey, panel.force)
  end
end

-- Second write right before the track is drawn, after CSP's own per-frame material adjustments.
local function ensureDrawHandle()
  if drawHandle then return end
  local ok, handle = pcall(render.on, "main.track.opaque", function()
    if active then pcall(pushColours) end
  end)
  if ok and handle then
    drawHandle = handle
  else
    U.Log("BLUE: could not register the render callback: " .. tostring(handle))
  end
end

local function disposeDrawHandle()
  if not drawHandle then return end
  local handle = drawHandle
  drawHandle = nil
  if not pcall(function() handle:dispose() end) then pcall(handle) end
end

-- One log line per lapping: what a blue panel really shows half a second after it went blue.
local function logReadback()
  if not readbackAt or clock < readbackAt then return end
  readbackAt = nil
  for _, panel in ipairs(Panels.List()) do
    if panel.wantKey == "blue" or panel.wantKey == "dark" then
      local e = Panels.ReadEmissive(panel)
      local w = panel.want or DARK
      U.Log(sFormat("BLUE: panel at p=%.3f wants %s (%.2f,%.2f,%.2f), shows (%.2f,%.2f,%.2f)",
          panel.p, panel.wantKey, w.r, w.g, w.b, e.r, e.g, e.b))
      return
    end
  end
end

local function applyColours(sim)
  decideColours(sim)
  pushColours()
  if active then ensureDrawHandle() end
  logReadback()
end

function M.Reset()
  disposeDrawHandle()
  readbackAt = nil
  Panels.RestoreAll()
  blueUntil = {}
  active = false
  if lappedCar or lappingCar then U.Log("BLUE: lights off (reset)") end
  lappedCar = nil
  lappingCar = nil
  lastScanAt = -1000.0
  clock = 0.0
end

local function shouldRun(sim)
  if not S.blueFlagEnabled then return false end
  if sim.isSessionStarted ~= true or not Sess.IsRaceMode(sim) then return false end
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then return false end
  if Sess.IsSessionFinished(sim) then return false end
  return true
end

function M.Update(sim, dt)
  if not shouldRun(sim) then
    if active or lappedCar or lappingCar then M.Reset() end
    return
  end
  local trackLength = sim.trackLengthM
  if not U.IsFinite(trackLength) or trackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then return end
  if Panels.Discover() == 0 then return end

  clock = clock + (dt or 0.016)
  if clock - lastScanAt >= C.BLUE_SCAN_SECONDS then
    lastScanAt = clock
    local j, gap, p0 = nil, nil, nil
    local k, kGap, kp0 = nil, nil, nil
    local yellow = S.fcy ~= nil and S.fcy ~= "green"
    if not yellow and S.playerRetired ~= true then
      j, gap, p0 = findLappedCar(trackLength)
      k, kGap, kp0 = findLappingCar(sim, trackLength)
    end
    local lit = j and markBluePanels(p0, gap + C.BLUE_PAST_LAPPED_M, trackLength) or 0
    local kLit = k and markBluePanels(kp0, C.BLUE_BEING_LAPPED_AHEAD_M, trackLength) or 0
    if j ~= lappedCar then
      if j then
        U.Log(sFormat("BLUE: t=%.1f lapping car %d, %.0f m ahead; %d panel(s) in range", clock, j, gap, lit))
        if lit > 0 then readbackAt = clock + 0.5 end
      else
        U.Log(sFormat("BLUE: t=%.1f no car to lap ahead; blue panels off", clock))
      end
      lappedCar = j
    end
    if k ~= lappingCar then
      if k then
        U.Log(sFormat("BLUE: t=%.1f being lapped by car %d, %.0f m behind; %d panel(s) in range (AC flag %s)",
            clock, k, kGap, kLit, tostring(sim.raceFlagType)))
        if kLit > 0 then readbackAt = clock + 0.5 end
      else
        U.Log(sFormat("BLUE: t=%.1f no longer being lapped", clock))
      end
      lappingCar = k
    end
  end
  applyColours(sim)
end

return M
