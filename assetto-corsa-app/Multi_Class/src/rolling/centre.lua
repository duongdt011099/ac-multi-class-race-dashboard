-- Rolling start: centre the double file on the track instead of on the AI racing line.
--
-- CSP drives the AI along fast_lane.ai with a lateral offset in metres from that line, so two
-- columns at +/-LANE_METERS sit symmetric around the racing line, which on the approach to a corner
-- is close to one edge. ac.getTrackAISplineSides(progress) gives the distance from the racing line
-- to the left (x) and right (y) track edges; the track centre therefore lies (left - right) / 2
-- metres to the left of the line. That shift is added to both columns, sampled over a short
-- stretch ahead of the car so the columns move smoothly as the racing line swings across.
--
-- The direction of a positive setAISplineAbsoluteOffset (world left or right) is not assumed: it is
-- learned once from a side-by-side double-file pair (which world side the car with the larger
-- offset is on). Until it is known the columns stay around the racing line as before.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local PoleSide = require("src/rolling/pole_side")

local sFormat = string.format
local abs, min, max = math.abs, math.min, math.max
local getCar = ac.getCar
local getSides = ac.getTrackAISplineSides

local M = {}

-- +1: a positive offset moves the car to its world left, -1: to its right, nil: not learned yet.
-- It is a property of CSP, not of the session, so it is learned once per game launch.
local offsetToLeft = nil
local votes = 0

local function sidesAt(progress)
  local ok, sides = pcall(getSides, progress % 1.0)
  if not ok or not sides then return nil end
  local l, r = sides.x, sides.y
  if not (U.IsFinite(l) and U.IsFinite(r)) or l <= 0 or r <= 0 then return nil end
  return l, r
end

-- Average track-centre shift (metres to the world left of the racing line) and half width over the
-- stretch from the car to CENTRE_LOOKAHEAD_M ahead. Returns nil when the track has no side data.
local function centreAhead(progress, trackLength)
  local samples = C.ROLLING_START_CENTRE_SAMPLES
  local shift, half, n = 0.0, 0.0, 0
  for i = 0, samples - 1 do
    local p = progress + (C.ROLLING_START_CENTRE_LOOKAHEAD_M * i / max(1, samples - 1)) / trackLength
    local l, r = sidesAt(p)
    if l then
      shift = shift + (l - r) * 0.5
      half = half + (l + r) * 0.5
      n = n + 1
    end
  end
  if n == 0 then return nil end
  return shift / n, half / n
end

-- Target lane offset for a double-file car: the track centre plus its column, with the column
-- pulled in on a narrow track so neither column runs off the edge. Falls back to the plain
-- racing-line lane when the offset direction is not learned yet or the track has no side data.
function M.LaneOffset(state, car, trackLength)
  local side = state.startSide or 1
  local lane = C.ROLLING_START_LANE_METERS
  if not (C.ROLLING_START_CENTRE_ENABLED and offsetToLeft and getSides) then return side * lane end
  if not (car and type(car.splinePosition) == "number" and U.IsFinite(trackLength) and trackLength > 0) then
    return side * lane
  end
  local shiftLeft, half = centreAhead(car.splinePosition, trackLength)
  if not shiftLeft then return side * lane end
  lane = max(C.ROLLING_START_CENTRE_MIN_LANE_M, min(lane, half - C.ROLLING_START_CENTRE_EDGE_MARGIN_M))
  local shift = max(-C.ROLLING_START_CENTRE_MAX_SHIFT_M, min(C.ROLLING_START_CENTRE_MAX_SHIFT_M, shiftLeft))
  return offsetToLeft * shift + side * lane
end

-- Learns the offset direction from two double-file cars that are side by side in opposite columns.
-- Needs CENTRE_CALIBRATION_VOTES agreeing measurements in a row before it locks.
function M.Calibrate()
  if offsetToLeft or not getSides then return end
  local queue = S.rollingStartQueue or {}
  for a = 1, #queue do
    local sa = S.rollingStartCars[queue[a]]
    local ca = sa and sa.enteredPhase2 and not sa.dropped and getCar(queue[a])
    local lx, ly, lz = PoleSide.GetCarLeftAxis(ca)
    if ca and lx and ca.position and (ca.speedKmh or 0) > 20 then
      for b = a + 1, #queue do
        local sb = S.rollingStartCars[queue[b]]
        local cb = sb and sb.enteredPhase2 and not sb.dropped and getCar(queue[b])
        if cb and cb.position and ca.look and (sb.startSide or 1) ~= (sa.startSide or 1) then
          local dOff = (sa.currentSplineOffset or 0) - (sb.currentSplineOffset or 0)
          local dx, dy, dz = ca.position.x - cb.position.x, ca.position.y - cb.position.y, ca.position.z - cb.position.z
          local lat = dx * lx + dy * ly + dz * lz          -- > 0: car a is on the left of car b
          local along = abs(dx * ca.look.x + dy * ca.look.y + dz * ca.look.z)
          if abs(dOff) > 4.0 and abs(lat) > 2.5 and along < 12.0 then
            local sign = (dOff * lat > 0) and 1 or -1
            if votes ~= 0 and (votes > 0) ~= (sign > 0) then votes = 0 end
            votes = votes + sign
            if abs(votes) >= C.ROLLING_START_CENTRE_CALIBRATION_VOTES then
              offsetToLeft = sign
              U.Log(sFormat("ROLLING: centred double file on; a positive lane offset moves a car to its %s",
                  sign > 0 and "LEFT" or "RIGHT"))
            end
            return
          end
        end
      end
    end
  end
end

return M
