-- Field snapshot for the AI layer (a light track monitor). A few times a
-- second every car in the race gets: track progress and lateral position (-1..1 across the track),
-- the next turn ahead, the nearest car ahead / behind on the same line with its distance, the
-- nearest car of any kind, and whether it is side by side with another car.
local S = require("src/state")
local C = require("src/constants")

local getCar = ac.getCar
local sqrt, abs = math.sqrt, math.abs

local F = {}

local data = {}        -- [carIndex] = per-car table, reused between scans
local lastScanAt = -1000.0
local clock = 0.0

local function inPit(car)
  return car.isInPitlane == true or car.isInPit == true
end

function F.Get(i)
  local d = data[i]
  if d and d.valid then return d end
  return nil
end

function F.Reset()
  data = {}
  lastScanAt = -1000.0
  clock = 0.0
end

local function fresh(d)
  d.carAhead, d.gapAhead = -1, 999.0
  d.carBehind, d.gapBehind = -1, 999.0
  d.nearest = 999.0
  d.sbs, d.sbsRole = nil, nil
end

-- Refreshes the snapshot when it is older than FIELD_SCAN_SECONDS. Returns true when it rescanned.
function F.Update(dt)
  clock = clock + dt
  if clock - lastScanAt < C.AI_FIELD_SCAN_SECONDS then return false end
  lastScanAt = clock

  local n = S.driverCount
  for i = 0, n - 1 do
    local d = data[i]
    if not d then d = {}; data[i] = d end
    fresh(d)
    d.valid = false
    local car = getCar(i)
    if car and car.position and i ~= S.safetyCarIndex and not inPit(car) then
      local ok, coords = pcall(ac.worldCoordinateToTrack, car.position)
      if ok and coords then
        d.valid = true
        d.prog, d.lat = coords.z, coords.x
        d.px, d.py, d.pz = car.position.x, car.position.y, car.position.z
        d.speed = car.speedKmh or 0
        d.accel = (car.acceleration and car.acceleration.z) or 0
        d.turnDist, d.turnAngle = 999.0, 0.0
        local okT, turn = pcall(ac.getTrackUpcomingTurn, i)
        if okT and turn then d.turnDist, d.turnAngle = turn.x or 999.0, turn.y or 0.0 end
      end
    end
  end

  for a = 0, n - 2 do
    local da = data[a]
    if da.valid then
      for b = a + 1, n - 1 do
        local db = data[b]
        if db.valid then
          local dp = db.prog - da.prog
          if dp > 0.5 then dp = dp - 1.0 elseif dp < -0.5 then dp = dp + 1.0 end
          if dp < 0.1 and dp > -0.1 then
            local dx, dy, dz = da.px - db.px, da.py - db.py, da.pz - db.pz
            local dist = sqrt(dx * dx + dy * dy + dz * dz)
            local latDiff = abs(da.lat - db.lat)
            if dist < da.nearest then da.nearest = dist end
            if dist < db.nearest then db.nearest = dist end
            if latDiff < 0.3 then
              if dp > 0 then   -- b is ahead of a
                if dist < da.gapAhead then da.carAhead, da.gapAhead = b, dist end
                if dist < db.gapBehind then db.carBehind, db.gapBehind = a, dist end
              elseif dp < 0 then
                if dist < da.gapBehind then da.carBehind, da.gapBehind = b, dist end
                if dist < db.gapAhead then db.carAhead, db.gapAhead = a, dist end
              end
            end
            -- Side by side: the same spot along the track on different lines, within 8 m.
            if abs(dp) < 0.01 and latDiff > 0.3 and dist < 8 then
              local aAhead = da.prog > db.prog
              da.sbs, da.sbsRole = b, aAhead and "defense" or "attack"
              db.sbs, db.sbsRole = a, aAhead and "attack" or "defense"
            end
          end
        end
      end
    end
  end
  return true
end

return F
