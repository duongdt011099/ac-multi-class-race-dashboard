-- Track outline sampled from the sim's spline, fitted to a rectangle of the window.
local Track = {}

local pts = {}          -- raw world points { x, z }
local rotCache = nil    -- { rot, xs, zs, minX, minZ, w, h }
local startLine = nil   -- { cx, cz, dx, dz } world-space start/finish marker
local built = false

local function build()
  built = true
  local okLen, len = pcall(function() return ac.getSim().trackLengthM end)
  local n = math.max(400, math.min(1600, math.floor((okLen and len or 5000) / 8)))
  local v = vec3()
  for i = 0, n - 1 do
    v:set(0, 0, i / n)
    local p = ac.trackCoordinateToWorld(v)
    pts[#pts + 1] = { p.x, p.z }
  end
  local c = ac.trackCoordinateToWorld(vec3(0, 0, 0))
  local r = ac.trackCoordinateToWorld(vec3(1, 0, 0))
  local l = ac.trackCoordinateToWorld(vec3(-1, 0, 0))
  startLine = { c.x, c.z, r.x - l.x, r.z - l.z }
end

local function rotated(rot)
  if rotCache and rotCache.rot == rot then return rotCache end
  local a = rot * math.pi / 2
  local ca, sa = math.cos(a), math.sin(a)
  local xs, zs = {}, {}
  local minX, minZ, maxX, maxZ = math.huge, math.huge, -math.huge, -math.huge
  for i, p in ipairs(pts) do
    local x, z = p[1] * ca - p[2] * sa, p[1] * sa + p[2] * ca
    xs[i], zs[i] = x, z
    if x < minX then minX = x end
    if x > maxX then maxX = x end
    if z < minZ then minZ = z end
    if z > maxZ then maxZ = z end
  end
  rotCache = { rot = rot, ca = ca, sa = sa, xs = xs, zs = zs, minX = minX, minZ = minZ,
               w = math.max(maxX - minX, 1), h = math.max(maxZ - minZ, 1) }
  return rotCache
end

-- Returns a view: world(x, z) -> screen vec2, plus the polyline helper. nil when the track is unavailable.
function Track.View(rot, topLeft, size)
  if not built then
    local ok = pcall(build)
    if not ok then pts = {} end
  end
  if #pts < 3 then return nil end
  local r = rotated(rot)
  local scale = math.min(size.x / r.w, size.y / r.h)
  local ox = topLeft.x + (size.x - r.w * scale) / 2
  local oz = topLeft.y + (size.y - r.h * scale) / 2
  local view = { scale = scale }
  local tmp = vec2()

  function view.project(x, z)
    local rx, rz = x * r.ca - z * r.sa, x * r.sa + z * r.ca
    return vec2(ox + (rx - r.minX) * scale, oz + (rz - r.minZ) * scale)
  end

  function view.path(color, width)
    ui.pathClear()
    for i = 1, #r.xs do
      tmp.x, tmp.y = ox + (r.xs[i] - r.minX) * scale, oz + (r.zs[i] - r.minZ) * scale
      ui.pathLineTo(tmp)
    end
    ui.pathStroke(color, true, width)
  end

  function view.startLine(color, width, half)
    local s = startLine
    local a = view.project(s[1], s[2])
    local dx, dz = s[3] * r.ca - s[4] * r.sa, s[3] * r.sa + s[4] * r.ca
    local len = math.sqrt(dx * dx + dz * dz)
    if len < 1e-6 then return end
    local d = vec2(dx / len, dz / len) * half
    ui.drawLine(a - d, a + d, color, width)
  end

  return view
end

return Track
