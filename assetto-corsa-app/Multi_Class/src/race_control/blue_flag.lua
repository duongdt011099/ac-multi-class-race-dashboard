-- Race control for the track's own flag panels: blue wherever a car is about to be lapped, for every
-- car on track (the human car lapping or being lapped, and AI cars lapping each other).
--
-- AC's own blue flag (physics.overrideRacingFlag(ac.FlagType.FasterCar)) feeds FLAG_TYPE, which lights
-- every flag panel on the track and also the car's dash screen. Instead, the app finds the panels
-- itself (race_control/panels.lua), gives each one its own material and colours them one by one:
--   - blue, blinking: for each car about to be lapped, the panels from the car lapping it up to just
--     past the lapped car. When the human car is the one being lapped, AC shows it its own blue flag
--     (dash screen included, which is right); that flag's track-wide blue on the other panels is
--     held back to their original look;
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
local lastPairs = ""      -- "lapper>lapped" pairs of the last scan (for the log only)
local active = false      -- true while any panel is driven by the app
local inReplay = false    -- the last Update was for a replay

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

-- Every car about to be lapped, with the nearest car lapping it: the lapper is physically behind
-- within C.BLUE_LOOKAHEAD_M and at least C.BLUE_LAPPED_MIN_LAPS ahead in race distance. Covers the
-- human car on either side and AI cars lapping each other. When AC itself shows the player the blue
-- flag (sim.raceFlagType), the car that caused it counts too.
-- Returns { { lapped, lapper, gap (m), pLapper }, ... }.
local function findLappingPairs(sim, trackLength)
  local cars = {}
  for i = 0, S.driverCount - 1 do
    if i ~= S.safetyCarIndex and not (i == 0 and S.playerRetired == true and not inReplay) then
      local car = getCar(i)
      local p = car and progress(car)
      if p and not inPit(car) and car.isRetired ~= true and car.isActive ~= false then
        cars[#cars + 1] = { i = i, p = p, d = (car.lapCount or 0) + p }
      end
    end
  end
  local pairs_ = {}
  for _, a in ipairs(cars) do
    local best, bestGap = nil, nil
    for _, b in ipairs(cars) do
      if b ~= a then
        local gap = ((a.p - b.p) % 1.0) * trackLength
        if gap > 1.0 and gap <= C.BLUE_LOOKAHEAD_M and b.d - a.d >= C.BLUE_LAPPED_MIN_LAPS
            and (not bestGap or gap < bestGap) then
          best, bestGap = b, gap
        end
      end
    end
    if best then
      pairs_[#pairs_ + 1] = { lapped = a.i, lapper = best.i, gap = bestGap, pLapper = best.p }
    elseif a.i == 0 and sim.raceFlagType == ac.FlagType.FasterCar then
      local cause = tonumber(sim.raceFlagCause) or -1
      local car = cause > 0 and getCar(cause) or nil
      local pc = car and progress(car)
      if pc then
        pairs_[#pairs_ + 1] = { lapped = 0, lapper = cause, gap = ((a.p - pc) % 1.0) * trackLength, pLapper = pc }
      end
    end
  end
  return pairs_
end

-- How far ahead (m) the stretch from the lapping car to just past the lapped car first enters a group
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

-- The nearest panels ahead of the lapping car (at lap progress p0), up to toM metres ahead (just past
-- the lapped car).
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
  if lastPairs ~= "" then U.Log("BLUE: lights off (reset)") end
  lastPairs = ""
  lastScanAt = -1000.0
  clock = 0.0
end

local function shouldRun(sim, replay)
  if not S.blueFlagEnabled then return false end
  if not Sess.IsRaceMode(sim) then return false end
  -- A replay is usually watched once the race is over: the live session state (finished, formation
  -- lap paused by the replay) says nothing about the moment being replayed.
  if replay then return true end
  if sim.isSessionStarted ~= true then return false end
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then return false end
  if Sess.IsSessionFinished(sim) then return false end
  return true
end

-- replay: true while a replay is being watched (the only part of the app that runs then). Switching
-- between live and replay starts the lights over.
function M.Update(sim, dt, replay)
  replay = replay == true
  if replay ~= inReplay then
    if active or lastPairs ~= "" then M.Reset() end
    inReplay = replay
    U.Log(replay and "BLUE: replay started, blue flag lights follow the replay"
        or "BLUE: replay ended, blue flag lights follow the live race")
  end
  if not shouldRun(sim, replay) then
    if active or lastPairs ~= "" then M.Reset() end
    return
  end
  local trackLength = sim.trackLengthM
  if not U.IsFinite(trackLength) or trackLength <= C.ROLLING_START_MIN_TRACK_LENGTH_M then return end
  if Panels.Discover() == 0 then return end

  clock = clock + (dt or 0.016)
  if clock - lastScanAt >= C.BLUE_SCAN_SECONDS then
    lastScanAt = clock
    local yellow = not inReplay and S.fcy ~= nil and S.fcy ~= "green"
    local found = yellow and {} or findLappingPairs(sim, trackLength)
    local parts, lit = {}, 0
    for _, pair in ipairs(found) do
      local n = markBluePanels(pair.pLapper, pair.gap + C.BLUE_PAST_LAPPED_M, trackLength)
      lit = lit + math.min(n, C.BLUE_MAX_PANELS)
      parts[#parts + 1] = sFormat("%d>%d", pair.lapper, pair.lapped)
    end
    table.sort(parts)
    local key = table.concat(parts, " ")
    if key ~= lastPairs then
      if key ~= "" then
        U.Log(sFormat("BLUE: t=%.1f lapping (lapper>lapped): %s; %d panel(s) blue (AC flag %s)",
            clock, key, lit, tostring(sim.raceFlagType)))
        if lit > 0 and lastPairs == "" then readbackAt = clock + 0.5 end
      else
        U.Log(sFormat("BLUE: t=%.1f nobody being lapped; blue panels off", clock))
      end
      lastPairs = key
    end
  end
  applyColours(sim)
end

return M
