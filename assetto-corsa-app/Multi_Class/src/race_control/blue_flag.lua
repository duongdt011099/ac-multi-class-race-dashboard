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
local lastLapCount = {}   -- car index -> lap count at the last scan
local lapChangedAt = {}   -- car index -> clock of its last lap count change
local pairSince = {}      -- "lapper>lapped" -> clock the pair was first seen (unbroken)
local preStart = {}       -- car index -> true while it is on the grid behind the line (lap 0)

local function colour(c, scale)
  local k = (scale or 1.0) / 255
  return rgb(c[1] * k, c[2] * k, c[3] * k)
end

local function progress(car)
  if type(car.splinePosition) ~= "number" then return nil end
  return U.NormalizeTrackProgress(car.splinePosition)
end

-- True when the car is not out on the racing track: in the pit lane or its pit box. AC's own pit
-- flags miss the pit lane on some tracks (its "pits area" is not always marked), so the pit-lane
-- surface under the wheels and the distance from the racing line count too: a pit lane running
-- beside the main straight is further from the AI spline than the track is wide. Cars in the pits
-- take no part in lapping, either way round.
local function inPit(car, p)
  if car.isInPitlane == true or car.isInPit == true then return true end
  local wheels = U.SafeGet(car, "wheels")
  if wheels then
    for w = 0, 3 do
      local wheel = wheels[w]
      if wheel and U.SafeGet(wheel, "surfacePitlane") == true then return true end
    end
  end
  if p and car.position and ac.getTrackAISplineSides and ac.trackProgressToWorldCoordinate then
    local ok, off = pcall(function()
      local sides = ac.getTrackAISplineSides(p)
      if math.max(sides.x, sides.y) < 1.0 then return nil end   -- no track width data
      local centre = ac.trackProgressToWorldCoordinate(p)
      local dx, dz = car.position.x - centre.x, car.position.z - centre.z
      return math.sqrt(dx * dx + dz * dz) - math.max(sides.x, sides.y)
    end)
    if ok and U.IsFinite(off) and off > C.BLUE_PIT_LANE_OFFSET_M then return true end
  end
  return false
end

-- Every car about to be lapped, with the nearest car lapping it: the lapper is physically behind
-- within C.BLUE_LOOKAHEAD_M and at least C.BLUE_LAPPED_MIN_LAPS ahead in race distance. Covers the
-- human car on either side and AI cars lapping each other. When AC itself shows the player the blue
-- flag (sim.raceFlagType), the car that caused it counts too.
-- Around the start/finish line AC's lap count and spline position are briefly out of step, which
-- puts a car a whole lap up or down for a moment; a car is left out until its lap count has been
-- steady for C.BLUE_LINE_SETTLE_SECONDS.
-- Returns { { lapped, lapper, gap (m), pLapper }, ... }.
local function findLappingPairs(sim, trackLength)
  local cars = {}
  for i = 0, S.driverCount - 1 do
    if i ~= S.safetyCarIndex and not (i == 0 and S.playerRetired == true and not inReplay) then
      local car = getCar(i)
      local p = car and progress(car)
      if p and car.isRetired ~= true and car.isActive ~= false and not inPit(car, p) then
        local laps = car.lapCount or 0
        if lastLapCount[i] ~= nil and lastLapCount[i] ~= laps then lapChangedAt[i] = clock end
        lastLapCount[i] = laps
        -- A car first seen on the grid (lap count 0, just behind the line) has not started its lap:
        -- its race distance is p - 1 until it crosses the line, or it would look a lap ahead of the
        -- cars already past it.
        if preStart[i] == nil then preStart[i] = laps == 0 and p > C.BLUE_GRID_PROGRESS end
        if preStart[i] and (laps > 0 or p < 0.5) then preStart[i] = false end
        local settled = clock - (lapChangedAt[i] or -1000.0) >= C.BLUE_LINE_SETTLE_SECONDS
        local nearLine = p < C.BLUE_LINE_MARGIN or p > 1.0 - C.BLUE_LINE_MARGIN
        if settled and not nearLine then
          cars[#cars + 1] = { i = i, p = p, d = laps + p - (preStart[i] and 1 or 0) }
        end
      end
    end
  end
  local pairs_ = {}
  for _, a in ipairs(cars) do
    local best, bestGap = nil, nil
    local player, playerGap = nil, nil   -- the human car, when it is lapping `a` too (not the nearest)
    for _, b in ipairs(cars) do
      if b ~= a then
        local gap = ((a.p - b.p) % 1.0) * trackLength
        if gap > 1.0 and gap <= C.BLUE_LOOKAHEAD_M and b.d - a.d >= C.BLUE_LAPPED_MIN_LAPS then
          if not bestGap or gap < bestGap then best, bestGap = b, gap end
          if b.i == 0 then player, playerGap = b, gap end
        end
      end
    end
    if best then
      pairs_[#pairs_ + 1] = { lapped = a.i, lapper = best.i, gap = bestGap, pLapper = best.p }
      -- An AI car between the human car and the lapped car: the human car laps it as well, so its
      -- stretch (from further back, wide groups of boards allowed) gets blue too.
      if player and player ~= best then
        pairs_[#pairs_ + 1] = { lapped = a.i, lapper = 0, gap = playerGap, pLapper = player.p }
      end
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
-- the lapped car). Groups of boards wider than C.BLUE_WIDE_GROUP_M (Le Mans merges boards over a
-- kilometre into one mesh) light only for a lapping that involves the human car (allowWide):
-- lighting them for AI cars would put blue all over the track.
local function markBluePanels(p0, toM, trackLength, allowWide)
  local candidates = {}
  for _, panel in ipairs(Panels.List()) do
    local ahead
    if panel.group and not allowWide and (panel.size or 0) > C.BLUE_WIDE_GROUP_M then
      ahead = nil
    elseif panel.group then
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

-- Diagnostics: how often the track config rewrote a driven panel between the app's update and the
-- scene-ready write (logged every few seconds while panels are driven).
local overwriteFrames, checkedFrames, overwriteLogAt = 0, 0, 0.0

local function checkOverwritten()
  for _, panel in ipairs(Panels.List()) do
    if panel.want then
      local e = Panels.ReadEmissive(panel)
      local w = panel.want
      checkedFrames = checkedFrames + 1
      if math.abs(e.r - w.r) + math.abs(e.g - w.g) + math.abs(e.b - w.b) > 0.05 then
        overwriteFrames = overwriteFrames + 1
      end
      break
    end
  end
  if clock >= overwriteLogAt and checkedFrames > 0 then
    U.Log(sFormat("BLUE: track config rewrote a driven panel in %d of %d frames before the scene-ready write%s",
        overwriteFrames, checkedFrames, inReplay and " (replay)" or ""))
    overwriteFrames, checkedFrames, overwriteLogAt = 0, 0, clock + 5.0
  end
end

-- Redraws every driven panel's own mesh in its colour, over the track's drawing of it. The track
-- config's flag adjustments can rewrite the panel material after every write the app makes (seen in
-- replays: the green "start" flag flashing through the blue); a redraw does not depend on the
-- material, so the colour always shows. The shader stays trivial: one that fails to compile halts
-- the whole script.
local overlayParams = {
  mesh = nil,
  transform = "original",
  textures = {},
  values = { gColor = rgbm(0, 0, 0, 1) },
  -- gWhiteRefPoint: the scene's white level (as CSP's own debug app uses it), so 0..1 colours show
  -- as themselves whatever the exposure.
  shader = "float4 main(PS_IN pin) { return float4(gColor.rgb * gWhiteRefPoint, 1); }",
}
local overlayFailed = false

-- The plain 0..1 colour of a panel state (the material glow values are scaled for the track's
-- lighting and look dark when drawn directly).
local OVERLAY_RGB = { blue = C.PANEL_BLUE_RGB, yellow = C.PANEL_YELLOW_RGB, green = C.PANEL_GREEN_RGB }

local function overlayColour(key)
  local c = OVERLAY_RGB[key]
  if not c then return rgbm(0, 0, 0, 1) end
  local k = C.PANEL_OVERLAY_BRIGHTNESS / 255
  return rgbm(c[1] * k, c[2] * k, c[3] * k, 1)
end

local function drawOverlays()
  if overlayFailed then return end
  render.setBlendMode(render.BlendMode.Opaque)
  render.setDepthMode(render.DepthMode.ReadOnlyLessEqual)
  render.setCullMode(render.CullMode.None)
  for _, panel in ipairs(Panels.List()) do
    if panel.want then
      overlayParams.mesh = panel.ref
      overlayParams.values.gColor = overlayColour(panel.wantKey)
      local ok, err = pcall(render.mesh, overlayParams)
      if not ok then
        overlayFailed = true
        U.Log("BLUE: board redraw failed, using material colours only: " .. tostring(err))
        return
      end
    end
  end
end

-- Extra writes after CSP's own per-frame material adjustments (which run after script.update):
-- when the scene is ready (before the main pass is drawn) and again while the track is drawn, plus
-- the redraw of the driven panels.
local function ensureDrawHandle()
  if drawHandle then return end
  local handles = {}
  local okReady, ready = pcall(render.onSceneReady, function()
    if active then
      pcall(checkOverwritten)
      pcall(pushColours)
    end
  end)
  if okReady and ready then handles[#handles + 1] = ready end
  local okDraw, draw = pcall(render.on, "main.track.opaque", function()
    if active then
      pcall(pushColours)
      pcall(drawOverlays)
    end
  end)
  if okDraw and draw then handles[#handles + 1] = draw end
  if #handles == 0 then
    U.Log("BLUE: could not register the render callbacks: " .. tostring(ready) .. " / " .. tostring(draw))
    return
  end
  drawHandle = handles
end

local function disposeDrawHandle()
  if not drawHandle then return end
  local handles = drawHandle
  drawHandle = nil
  for _, handle in ipairs(handles) do
    if not pcall(function() handle:dispose() end) then pcall(handle) end
  end
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
  lastLapCount, lapChangedAt, pairSince, preStart = {}, {}, {}, {}
  lastScanAt = -1000.0
  clock = 0.0
end

-- A race session, by name or (a replay opened from Content Manager may have no session name) by
-- AC's session type. Practice and qualifying are left out: lap counts there mean nothing for lapping.
local function isRace(sim)
  if Sess.IsRaceMode(sim) then return true end
  local ok, raceType = pcall(function() return sim.raceSessionType end)
  return ok and ac.SessionType ~= nil and raceType == ac.SessionType.Race
end

local function shouldRun(sim, replay)
  if not S.blueFlagEnabled then return false end
  -- A replay is usually watched once the race is over: the live session state (finished, formation
  -- lap paused by the replay) says nothing about the moment being replayed.
  if replay then return isRace(sim) end
  if not Sess.IsRaceMode(sim) then return false end
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
    if replay then
      local okType, raceType = pcall(function() return sim.raceSessionType end)
      U.Log(sFormat("BLUE: replay started (session '%s', type %s, %d cars); blue flag lights %s",
          Sess.GetSessionNameLower(sim), tostring(okType and raceType), S.driverCount or 0,
          shouldRun(sim, true) and "follow the replay" or "off: not a race, or the blue flag option is off"))
    else
      U.Log("BLUE: replay ended, blue flag lights follow the live race")
    end
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
    local parts, lit, seen = {}, 0, {}
    for _, pair in ipairs(found) do
      local pairKey = sFormat("%d>%d", pair.lapper, pair.lapped)
      seen[pairKey] = true
      pairSince[pairKey] = pairSince[pairKey] or clock
      -- Only a lapping that holds for a moment counts (no blue for a one-scan glitch).
      if clock - pairSince[pairKey] >= C.BLUE_CONFIRM_SECONDS then
        local involvesPlayer = pair.lapper == 0 or pair.lapped == 0
        local n = markBluePanels(pair.pLapper, pair.gap + C.BLUE_PAST_LAPPED_M, trackLength, involvesPlayer)
        lit = lit + math.min(n, C.BLUE_MAX_PANELS)
        parts[#parts + 1] = pairKey
      end
    end
    for pairKey in pairs(pairSince) do
      if not seen[pairKey] then pairSince[pairKey] = nil end
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
