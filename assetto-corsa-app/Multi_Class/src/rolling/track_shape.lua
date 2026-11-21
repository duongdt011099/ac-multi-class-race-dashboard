-- Rolling start: where the double file may start, given the shape of the track.
--
-- The double file normally starts at a fixed share of the lap (or a fixed distance before the line
-- on long tracks). On some street circuits (Long Beach) a hairpin lies between that point and the
-- line; taking a near-180-degree corner two abreast breaks every row (the outside car travels much
-- further than the inside one) and the field piles up in the corner. When the AI line turns by
-- HAIRPIN_DEG or more within HAIRPIN_WINDOW_M after the normal point, the double file starts
-- HAIRPIN_EXIT_M after the last such corner instead, as long as at least MIN_DOUBLE_FILE_M of
-- double file is left before the line.
local C = require("src/constants")
local U = require("src/util")

local sFormat = string.format
local atan2, abs, pi, floor = math.atan2 or math.atan, math.abs, math.pi, math.floor

local M = {}

local cacheKey, cachePoint = nil, nil

local function worldAt(progress)
  local ok, pos = pcall(ac.trackProgressToWorldCoordinate, progress % 1.0)
  if ok and pos and U.IsFinite(pos.x) and U.IsFinite(pos.z) then return pos.x, pos.z end
  return nil
end

local function wrapAngle(a)
  while a > pi do a = a - 2 * pi end
  while a < -pi do a = a + 2 * pi end
  return a
end

-- Progress where the last hairpin after basePoint ends, or nil when there is none.
local function lastHairpinEnd(basePoint, trackLength)
  local stepM = C.ROLLING_START_SHAPE_STEP_M
  local step = stepM / trackLength
  local headings, starts = {}, {}
  local px, pz = worldAt(basePoint)
  if not px then return nil end
  local p = basePoint
  while p + step <= 1.0 do
    local nx, nz = worldAt(p + step)
    if not nx then return nil end
    headings[#headings + 1] = atan2(nx - px, nz - pz)
    starts[#starts + 1] = p
    px, pz, p = nx, nz, p + step
  end

  local window = math.max(2, floor(C.ROLLING_START_HAIRPIN_WINDOW_M / stepM + 0.5))
  local limit = C.ROLLING_START_HAIRPIN_DEG * pi / 180
  local found, worst, worstAt = nil, 0, nil
  for k = 1, #headings - window do
    local turn = 0.0
    for j = k, k + window - 1 do turn = turn + wrapAngle(headings[j + 1] - headings[j]) end
    turn = abs(turn)
    if turn > worst then worst, worstAt = turn, starts[k] end
    if turn >= limit then found = starts[k + window] end
  end
  return found, worst * 180 / pi, worstAt
end

-- The normal double-file point before any hairpin adjustment: a share of the lap, or a fixed
-- distance before the line on long tracks. The safety car pits from here, because a pit entry
-- usually lies before the last corner and the moved double-file point can be past it.
function M.BasePoint(trackLength)
  if type(trackLength) == "number" and trackLength > C.ROLLING_START_LONG_TRACK_METERS then
    return 1.0 - C.ROLLING_START_LONG_TRACK_DOUBLE_FILE_METERS / trackLength
  end
  return C.ROLLING_START_FORMATION_POINT
end

-- Spline position where the double file starts on this track (cached per track length).
function M.DoubleFileStart(basePoint, trackLength)
  if not (C.ROLLING_START_HAIRPIN_AVOID_ENABLED and ac.trackProgressToWorldCoordinate
      and U.IsFinite(trackLength) and trackLength > 0) then
    return basePoint
  end
  local key = sFormat("%.1f|%.4f", trackLength, basePoint)
  if cacheKey == key then return cachePoint end

  local point = basePoint
  local ok, hairpinEnd, worstDeg, worstAt = pcall(lastHairpinEnd, basePoint, trackLength)
  if ok and hairpinEnd then
    local candidate = hairpinEnd + C.ROLLING_START_HAIRPIN_EXIT_M / trackLength
    local left = (1.0 - candidate) * trackLength
    if left >= C.ROLLING_START_MIN_DOUBLE_FILE_M then
      point = candidate
      U.Log(sFormat("ROLLING: hairpin (%.0f deg in %.0f m at %.3f) after the double-file point; double file starts at %.3f (%.0f m before the line)",
          worstDeg, C.ROLLING_START_HAIRPIN_WINDOW_M, worstAt, point, left))
    else
      U.Log(sFormat("ROLLING: hairpin (%.0f deg at %.3f) after the double-file point, but only %.0f m left after it; keeping %.3f",
          worstDeg, worstAt, left, basePoint))
    end
  elseif ok then
    U.Log(sFormat("ROLLING: no hairpin after the double-file point (tightest %.0f deg in %.0f m); double file starts at %.3f",
        worstDeg or 0, C.ROLLING_START_HAIRPIN_WINDOW_M, basePoint))
  else
    U.Log("ROLLING: track shape scan failed: " .. tostring(hairpinEnd))
  end

  cacheKey, cachePoint = key, point
  return point
end

-- Distance (m) from the line to the first corner of the lap: where the AI line turns by
-- GREEN_CORNER_DEG or more within HAIRPIN_WINDOW_M. nil when none is found within scanM.
local function firstCornerAfterLine(trackLength, scanM)
  local stepM = C.ROLLING_START_SHAPE_STEP_M
  local step = stepM / trackLength
  local window = math.max(2, floor(C.ROLLING_START_HAIRPIN_WINDOW_M / stepM + 0.5))
  local limit = C.ROLLING_START_GREEN_CORNER_DEG * pi / 180
  local headings = {}
  local px, pz = worldAt(0.0)
  if not px then return nil end
  local n = floor(scanM / stepM)
  for k = 1, n do
    local nx, nz = worldAt(k * step)
    if not nx then return nil end
    headings[k] = atan2(nx - px, nz - pz)
    px, pz = nx, nz
    if k > window then
      local turn = 0.0
      for j = k - window, k - 1 do turn = turn + wrapAngle(headings[j + 1] - headings[j]) end
      if abs(turn) >= limit then return (k - window) * stepM end
    end
  end
  return nil
end

local greenKey, greenPoint = nil, 0.0

-- Spline position (just past the line, 0 = on the line) where the classes take green. When a late
-- hairpin leaves less than TARGET_DOUBLE_FILE_M of double file before the line, green moves past
-- the line by the shortfall, at most GREEN_MAX_SHIFT_M and never closer than GREEN_CORNER_CLEAR_M
-- to the first corner after the line, so the class has room to form before it goes.
function M.GreenPoint(trackLength)
  if not (C.ROLLING_START_GREEN_SHIFT_ENABLED and U.IsFinite(trackLength) and trackLength > 0) then return 0.0 end
  local base = M.BasePoint(trackLength)
  local key = sFormat("%.1f|%.4f", trackLength, base)
  if greenKey == key then return greenPoint end

  local start = M.DoubleFileStart(base, trackLength)
  local point = 0.0
  if start > base then
    local doubleFileM = (1.0 - start) * trackLength
    local want = C.ROLLING_START_TARGET_DOUBLE_FILE_M - doubleFileM
    if want > 0 then
      local scanM = C.ROLLING_START_GREEN_MAX_SHIFT_M + C.ROLLING_START_GREEN_CORNER_CLEAR_M
      local ok, cornerM = pcall(firstCornerAfterLine, trackLength, scanM)
      local room = C.ROLLING_START_GREEN_MAX_SHIFT_M
      if ok and cornerM then room = math.min(room, cornerM - C.ROLLING_START_GREEN_CORNER_CLEAR_M) end
      local shiftM = math.max(0.0, math.min(want, room))
      point = shiftM / trackLength
      U.Log(sFormat("ROLLING: only %.0f m of double file before the line; green moves %.0f m past it (first corner %s)",
          doubleFileM, shiftM, (ok and cornerM) and sFormat("%.0f m after the line", cornerM) or "not within range"))
    end
  end
  greenKey, greenPoint = key, point
  return point
end

return M
