-- The track's own flag panels (marshal light boards).
--
-- Track configs light their panels with a MATERIAL_ADJUSTMENT whose CONDITION reads AC's racing flag
-- (INPUT=FLAG_TYPE), so all panels show the same flag at once and the same flag reaches the car's
-- dash screen. Here every such panel mesh gets its own material, so the app can colour each panel on
-- its own (blue only near the car being lapped) without touching AC's racing flag.
--
-- Files:
--   track_config.lua          which meshes are flag boards, read from the track's CSP config
--   methods/<name>.lua        how one kind of board is lit (one file per kind, see methods/base.lua)
--   panels.lua (this file)    finds the board meshes and picks each one's method
local U = require("src/util")
local TrackConfig = require("src/race_control/track_config")

local sFormat = string.format

local M = {}

-- Lighting methods, most specific first: the first whose Matches() accepts a board's filter lights
-- it. "plain" accepts everything, so it stays last. A new kind of board = a new file + an entry here.
local METHODS = {
  require("src/race_control/methods/custom_emissive"),
  require("src/race_control/methods/plain"),
}

-- A single mesh bigger than this (AABB diagonal) is several boards merged into one (COTA builds its
-- boards that way). It is kept as a "group": lit as a whole when the stretch of track that needs a
-- blue flag passes through its area (its AABB grown by GROUP_MARGIN_M). A mesh bigger than
-- MAX_GROUP_SIZE_M covers too much of the lap to mean anything and is left alone.
local MAX_PANEL_SIZE_M = 25
local MAX_GROUP_SIZE_M = 1500
local GROUP_MARGIN_M = 30

local discovered = false
local panels = {}   -- { { ref, method, state, center, p, size, group, min, max, applied }, ... }

local function methodFor(filter, config)
  for _, method in ipairs(METHODS) do
    if method.Matches(filter, config) then return method end
  end
  return METHODS[#METHODS]
end

local function meshBounds(ref)
  local okS, mn, mx, n = pcall(function() return ref:getStaticAABB() end)
  if okS and mn and mx and (n or 0) > 0 then return mn, mx end
  local okL, lmn, lmx, ln = pcall(function() return ref:getLocalAABB() end)
  if okL and lmn and lmx and (ln or 0) > 0 then return lmn, lmx end
  return nil
end

-- Finds the panels once per game launch (the scene does not change while the track is loaded).
-- Gives each panel mesh its own material, so only call this when the app is going to drive them.
function M.Discover()
  if discovered then return #panels end
  discovered = true
  local config = TrackConfig.Read()
  if #config.filters == 0 then
    U.Log("PANELS: this track has no flag panels in its CSP config; no blue flag lights")
    return 0
  end

  local merged, groups, taken, perMethod = 0, 0, {}, {}
  for _, filter in ipairs(config.filters) do
    local method = methodFor(filter, config)
    local okFind, meshes = pcall(ac.findMeshes, filter)
    local count = okFind and meshes and meshes:size() or 0
    U.Log(sFormat("PANELS: filter '%s' matched %d mesh(es), lit as '%s'", filter, count, method.name))
    for i = 1, count do
      local ref = meshes:at(i)
      local mn, mx = meshBounds(ref)
      if mn then
        local center = (mn + mx) * 0.5
        local key = sFormat("%.1f:%.1f:%.1f", center.x, center.y, center.z)
        if not taken[key] then
          taken[key] = true
          local size = (mx - mn):length()
          if size > MAX_GROUP_SIZE_M then
            merged = merged + 1
            U.Log(sFormat("PANELS: mesh %d of '%s' skipped, %.0f m across", i, filter, size))
          else
            pcall(function() ref:ensureUniqueMaterials() end)
            local p = ac.worldCoordinateToTrackProgress(center)
            if type(p) == "number" and p >= 0 then
              local panel = { ref = ref, method = method, state = method.Prepare(ref), center = center, p = p,
                              size = size }
              if size > MAX_PANEL_SIZE_M then
                local grow = vec3(GROUP_MARGIN_M, GROUP_MARGIN_M, GROUP_MARGIN_M)
                panel.group = true
                panel.min, panel.max = mn - grow, mx + grow
                groups = groups + 1
                U.Log(sFormat("PANELS: mesh %d of '%s' is a group of boards, %.0f m across", i, filter, size))
              end
              perMethod[method.name] = (perMethod[method.name] or 0) + 1
              panels[#panels + 1] = panel
            end
          end
        end
      end
    end
  end
  local byMethod = {}
  for name, n in pairs(perMethod) do byMethod[#byMethod + 1] = sFormat("%d %s", n, name) end
  U.Log(sFormat("PANELS: %d flag panel(s) ready (%d groups of boards; %s), %d oversized mesh(es) skipped",
      #panels, groups, #byMethod > 0 and table.concat(byMethod, ", ") or "none", merged))
  return #panels
end

-- True when a world point is inside a group's (grown) area.
function M.GroupContains(panel, pos)
  local a, b = panel.min, panel.max
  return pos.x >= a.x and pos.x <= b.x and pos.y >= a.y and pos.y <= b.y and pos.z >= a.z and pos.z <= b.z
end

function M.List()
  return panels
end

-- colour: rgb glow, or nil for the panel's original look. Written only on a change unless `force`
-- is set: the track config's own flag adjustments rewrite the glow every frame, so a panel the app
-- drives has to be written every frame too.
function M.Set(panel, colour, key, force)
  key = key or "off"
  if panel.applied == key and not force then return end
  panel.applied = key
  pcall(panel.method.Apply, panel, colour)
end

-- Current glow of a panel (diagnostics: tells whether something overwrote the app's colour).
function M.ReadEmissive(panel)
  return require("src/race_control/methods/base").ReadEmissive(panel.ref)
end

function M.RestoreAll()
  for _, panel in ipairs(panels) do M.Set(panel, nil, "off", true) end
end

return M
