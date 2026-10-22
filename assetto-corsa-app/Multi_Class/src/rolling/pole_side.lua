-- Rolling start: detect which grid side the pole car occupies and measure per-car sides.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")

local sFormat = string.format
local getCar = ac.getCar

local M = {}

-- A grid measurement is only meaningful while the cars are standing on their grid slots. AC resets
-- an in-place restart asynchronously, so the cars can still be in the aborted attempt's formation
-- when the measurement is taken, and the very same geometry there would mirror the whole field.
local function standingOnGrid(car)
  if not car then return false end
  return (car.speedKmh or 0) <= C.ROLLING_START_GRID_MAX_SPEED_KMH
end

-- Returns the car's left-hand axis (x, y, z), or nil when the state does not expose one. In AC/CSP
-- `car.side` is the LEFT axis; the `look x up` fallback yields the same handedness in AC's basis.
function M.GetCarLeftAxis(car)
  if not car then return nil end
  local side = car.side
  if side then
    return side.x, side.y, side.z
  end
  if car.look and car.up then
    local lx, ly, lz = car.look.x, car.look.y, car.look.z
    local ux, uy, uz = car.up.x, car.up.y, car.up.z
    return ly * uz - lz * uy, lz * ux - lx * uz, lx * uy - ly * ux
  end
  return nil
end

local function resolvePlayerSlot(player)
  local pos = S.rollingStartGridPositions[0]
  if type(pos) ~= "number" or pos <= 0 then
    pos = player.racePosition
  end
  if type(pos) ~= "number" or pos <= 0 then
    U.Log("ROLLING: pole-side detect skipped (no grid slot); default LEFT")
    return nil
  end
  return pos
end

-- The car sharing the player's two-wide row: slot+1 on an odd slot, slot-1 on an even one.
-- Their midpoint approximates the racing line, so the vector between them is purely lateral. The
-- captured grid slots are used and not the live race classification: once the session runs the
-- classification no longer matches the grid the field started from.
local function findRowPartner(partnerPos)
  for i = 0, S.driverCount - 1 do
    local slot = S.rollingStartGridPositions[i]
    if type(slot) ~= "number" or slot <= 0 then
      local car = getCar(i)
      slot = car and car.racePosition or nil
    end
    if slot == partnerPos then
      return getCar(i)
    end
  end
  return nil
end

local function lateralOffset(car, partner, sx, sy, sz)
  local pp, gp = car.position, partner.position
  local dx, dy, dz = pp.x - gp.x, pp.y - gp.y, pp.z - gp.z
  return dx * sx + dy * sy + dz * sz
end

-- Finds the player's row partner and the player's lateral offset from it. Logs and returns nil
-- whenever the geometry is unavailable, so the caller keeps the reference default.
-- Returns pos, partnerPos, lateral, sx, sy, sz.
local function measurePlayerRow(player)
  local pos = resolvePlayerSlot(player)
  if not pos then return nil end

  local partnerPos = (pos % 2 == 1) and (pos + 1) or (pos - 1)
  local partner = findRowPartner(partnerPos)
  if not partner then
    U.Log(sFormat("ROLLING: pole-side detect skipped (no row partner for slot %d); default LEFT", pos))
    return nil
  end

  if not (player.position and partner.position) then
    U.Log("ROLLING: pole-side detect skipped (no world position); default LEFT")
    return nil
  end

  -- The row is only readable while both cars still stand on the grid.
  if not (standingOnGrid(player) and standingOnGrid(partner)) then
    U.Log(sFormat("ROLLING: pole-side detect skipped (cars not standing on the grid, slot=%d); default LEFT", pos))
    return nil
  end

  -- Player's own left-hand axis (see GetCarLeftAxis). A positive dot of (player - partner) with the
  -- left axis therefore means the player sits on the LEFT of the row.
  local sx, sy, sz = M.GetCarLeftAxis(player)
  if not sx then
    U.Log("ROLLING: pole-side detect skipped (no lateral axis); default LEFT")
    return nil
  end

  return pos, partnerPos, lateralOffset(player, partner, sx, sy, sz), sx, sy, sz
end

-- Measures which side of its grid row the human car starts on and derives the lane convention for
-- the whole field from it. The player is a reliable anchor: we know the exact grid slot (odd/even)
-- and can read the world position relative to the car sharing the row. If the geometry is
-- unavailable we keep the reference default (odd positions on the left).
function M.DetectRollingStartPoleSide()
  -- A lane the player picked in the config window wins over the measurement and survives the
  -- re-staging of the field after an in-place restart.
  if S.rollingStartPoleSideSource == "manual" then
    U.Log("ROLLING: pole-side detect skipped (manual lane override kept)")
    return
  end

  S.rollingStartPoleOnRight = false
  S.rollingStartPoleSideSource = "auto"

  local player = getCar(0)
  if not player then
    U.Log("ROLLING: pole-side detect skipped (no player car); default LEFT")
    return
  end

  local pos, partnerPos, lateral, sx, sy, sz = measurePlayerRow(player)
  if not pos then return end

  -- Reject ambiguous rows: when the player and its row partner are nearly coincident laterally the
  -- sign is just noise, so keep the reference default rather than mirroring the whole field.
  if math.abs(lateral) < 1.5 then
    U.Log(sFormat("ROLLING: pole-side detect ambiguous (slot=%d partner=%d lateral=%.3f); default LEFT",
        pos, partnerPos, lateral))
    return
  end

  local playerOnRight = lateral < 0
  -- Odd slots on the right exactly when the player's grid side agrees with their slot parity.
  S.rollingStartPoleOnRight = ((pos % 2 == 1) == playerOnRight)

  U.Log(sFormat("ROLLING: pole-side detect slot=%d partner=%d lateral=%.3f side=(%.2f,%.2f,%.2f) playerRight=%s -> poleOnRight=%s",
      pos, partnerPos, lateral, sx, sy, sz, tostring(playerOnRight), tostring(S.rollingStartPoleOnRight)))
end

-- Measures which side of its two-wide grid row a car starts on from its world position relative to
-- the car sharing the row (same geometry as DetectRollingStartPoleSide). Returns 1 when the car
-- sits on the right of its partner, -1 on the left, or nil when the geometry is unavailable or the
-- row is too ambiguous to read. Used so the double-file columns reproduce the real starting grid.
function M.MeasureRollingStartSide(car, partner)
  if not (car and partner) then return nil end
  if not (car.position and partner.position) then return nil end
  -- Only a standing grid pair tells us which side of the row a car starts on.
  if not (standingOnGrid(car) and standingOnGrid(partner)) then return nil end

  local sx, sy, sz = M.GetCarLeftAxis(car)
  if not sx then return nil end

  local lateral = lateralOffset(car, partner, sx, sy, sz)
  if math.abs(lateral) < 1.5 then return nil end

  -- A positive dot of (car - partner) with the left axis means the car sits on the LEFT.
  return lateral < 0 and 1 or -1
end

return M
