-- Reads the current track's CSP config for its flag boards.
--
-- Sources, in order: the layout's and the track's extension/ext_config.ini, then CSP's
-- extension/config/tracks/[loaded/]<track>.ini. Follows "[INCLUDE: file]" sections and
-- "[INCLUDE] INCLUDE = a, b" lists relative to the including file (missing ones, like CSP's own
-- common files, are skipped), and substitutes "$Name" with values from [DEFAULTS] sections
-- (Barcelona does both).
--
-- A flag board is a MATERIAL_ADJUSTMENT driven by a flag condition ([CONDITION_n] with
-- INPUT = FLAG_TYPE; a condition defined somewhere the app does not read, e.g. CSP's common
-- conditions.ini, counts when its name mentions FLAG) that the track itself would light for AC's blue
-- flag: the condition's LUT has a non-zero entry 12 (FasterCar). That keeps out single-purpose boards
-- such as Le Mans' safety-car boards and Daytona's yellow caution lights. A LUT the app cannot read
-- (a .lut file) is assumed to include blue.
--
-- Read() returns:
--   filters  mesh filters of the flag boards ("material:<name>" for MATERIALS, the raw filter for MESHES)
--   custom   set of filters (upper case) listed in a [CustomEmissive] section
local U = require("src/util")

local sFormat = string.format

local M = {}

local MAX_INCLUDE_DEPTH = 6

local function trim(s)
  return (s:gsub("^%s+", ""):gsub("%s+$", ""))
end

local function dirOf(path)
  return path:match("^(.*)[/\\]") or "."
end

local function readText(path)
  local ok, text = pcall(function() return io.load and io.load(path) end)
  if ok and type(text) == "string" and text ~= "" then return text end
  local okOpen, file = pcall(io.open, path, "r")
  if okOpen and file then
    local content = file:read("*a")
    file:close()
    if type(content) == "string" and content ~= "" then return content end
  end
  return nil
end

local readFile

-- ctx = { adjustments, conds, custom, vars, visited }
local function parseText(text, ctx, dir, depth)
  local section, props, items = nil, {}, {}
  local function include(rel)
    rel = trim(rel)
    if rel ~= "" and depth < MAX_INCLUDE_DEPTH then readFile(dir .. "/" .. rel, ctx, depth + 1) end
  end
  local function flush()
    local upper = section and section:upper() or ""
    if upper:find("^CONDITION") and props.NAME then
      ctx.conds[trim(props.NAME):upper()] = { input = props.INPUT or "", lut = props.LUT }
    elseif upper:find("^MATERIAL_ADJUSTMENT") and props.CONDITION and #items > 0 then
      ctx.adjustments[#ctx.adjustments + 1] = { items = items, cond = trim(props.CONDITION):upper() }
    elseif upper:find("^CUSTOMEMISSIVE") then
      for _, item in ipairs(items) do ctx.custom[item:upper()] = true end
    end
    section, props, items = nil, {}, {}
  end
  text = text:gsub("^\239\187\191", "")   -- UTF-8 BOM
  for line in text:gmatch("[^\r\n]+") do
    line = trim((line:gsub(";.*$", "")))
    -- "[NAME]" and the broken "NAME]" some configs have both start a new section ("[MIXIN: X]" too).
    local name = line:match("^%[?([^%[%]=]+)%]$")
    if name then
      flush()
      section = trim(name)
      local inc = section:match("^[Ii][Nn][Cc][Ll][Uu][Dd][Ee]%s*:%s*(.+)$")
      if inc then include(inc) end
    elseif section then
      local rawKey, value = line:match("^([%w_]+)%s*=%s*(.-)$")
      if rawKey then
        local key = rawKey:upper()
        value = value:gsub("%$([%w_]+)", ctx.vars)   -- unknown names stay as they are
        if section:upper() == "DEFAULTS" then
          ctx.vars[rawKey] = value
        elseif section:upper() == "INCLUDE" and key == "INCLUDE" then
          for part in value:gmatch("[^,]+") do include(part) end
        elseif key == "CONDITION" or key == "NAME" or key == "INPUT" or key == "LUT" then
          props[key] = value
        elseif key == "MATERIALS" or key == "MESHES" then
          for part in value:gmatch("[^,]+") do
            part = trim(part)
            if part ~= "" then
              items[#items + 1] = key == "MATERIALS" and ("material:" .. part) or part
            end
          end
        end
      end
    end
  end
  flush()
end

readFile = function(path, ctx, depth)
  local norm = path:gsub("\\", "/"):lower()
  if ctx.visited[norm] then return end
  ctx.visited[norm] = true
  local text = readText(path)
  if not text then return end
  local before = #ctx.adjustments
  parseText(text, ctx, dirOf(path), depth)
  U.Log(sFormat("PANELS: %s%s -> %d conditional material adjustment(s)", string.rep("  ", depth), path,
      #ctx.adjustments - before))
end

-- Whether a condition reads AC's racing flag, and whether it lights for the blue flag (LUT entry 12).
-- Returns isFlag, showsBlue.
local function flagCondition(name, conds)
  local def = conds[name]
  if not def then
    -- Defined somewhere the app does not read: go by the name, assume it covers blue.
    return name:find("FLAG") ~= nil, true
  end
  if not def.input:upper():find("FLAG_TYPE") then return false, false end
  local lut = def.lut
  if not lut or not lut:find("|") then return true, true end   -- a .lut file: cannot tell
  local entry = lut:match("|%s*12%s*=%s*([^|]*)")
  if not entry then return true, false end
  for n in entry:gmatch("[%d%.]+") do
    if (tonumber(n) or 0) > 0 then return true, true end
  end
  return true, false
end

local function configPaths()
  local paths = {}
  local okId, trackId = pcall(ac.getTrackID)
  if not okId or type(trackId) ~= "string" or trackId == "" then return paths end
  local okTracks, tracks = pcall(ac.getFolder, ac.FolderID.ContentTracks)
  if okTracks and tracks then
    local okLayout, layout = pcall(ac.getTrackLayout)
    if okLayout and type(layout) == "string" and layout ~= "" then
      paths[#paths + 1] = tracks .. "/" .. trackId .. "/" .. layout .. "/extension/ext_config.ini"
    end
    paths[#paths + 1] = tracks .. "/" .. trackId .. "/extension/ext_config.ini"
  end
  local okCfg, cfg = pcall(ac.getFolder, ac.FolderID.ExtCfgSys)
  if okCfg and cfg then
    paths[#paths + 1] = cfg .. "/tracks/" .. trackId .. ".ini"
    paths[#paths + 1] = cfg .. "/tracks/loaded/" .. trackId .. ".ini"
  end
  return paths
end

function M.Read()
  local ctx = { adjustments = {}, conds = {}, custom = {}, vars = {}, visited = {} }
  for _, path in ipairs(configPaths()) do readFile(path, ctx, 0) end

  -- A filter is a flag board when any of its adjustments has a flag condition that shows blue.
  local filters, seen, rejected = {}, {}, {}
  for _, adj in ipairs(ctx.adjustments) do
    local isFlag, showsBlue = flagCondition(adj.cond, ctx.conds)
    for _, item in ipairs(adj.items) do
      if isFlag and showsBlue then
        if not seen[item] then
          seen[item] = true
          filters[#filters + 1] = item
        end
      elseif isFlag then
        rejected[item] = adj.cond
      end
    end
  end
  for item, cond in pairs(rejected) do
    if not seen[item] then
      U.Log(sFormat("PANELS: '%s' skipped: its flag condition %s never shows blue", item, cond))
    end
  end
  return { filters = filters, custom = ctx.custom }
end

return M
