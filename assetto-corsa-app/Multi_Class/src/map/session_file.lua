-- Reads mcr_session.json (written by the dashboard next to race.ini) for class names, colors and drivers.
--   { "classes": [ { "name": "GT3", "order": 0, "color": "#ff0000" } ],
--     "cars": [ { "index": 0, "carId": "ks_...", "driverName": "...", "team": "...", "class": "GT3" } ] }
local SessionFile = {}

local FILE_NAME = "mcr_session.json"

local function path()
  local ok, docs = pcall(function() return ac.getFolder(ac.FolderID.Documents) end)
  if not ok or type(docs) ~= "string" or docs == "" then return nil end
  return (docs:gsub("\\", "/")) .. "/Assetto Corsa/cfg/" .. FILE_NAME
end

local function unescape(s)
  return (s:gsub('\\(["\\/])', "%1"):gsub("\\u(%x%x%x%x)", function(h)
    local cp = tonumber(h, 16)
    return cp < 128 and string.char(cp) or "?"
  end))
end

local function field(obj, key)
  local v = obj:match('"' .. key .. '"%s*:%s*"(.-)"%s*[,}]')
  return v and unescape(v) or nil
end

local function parseFallback(text)
  local out = { classes = {}, cars = {} }
  local carsAt = text:find('"cars"%s*:')
  local head = carsAt and text:sub(1, carsAt - 1) or text
  for obj in head:gmatch("{[^{}]*}") do
    local name = field(obj, "name")
    if name then
      out.classes[#out.classes + 1] = { name = name, order = tonumber(obj:match('"order"%s*:%s*(%d+)')), color = field(obj, "color") }
    end
  end
  for obj in (carsAt and text:sub(carsAt) or ""):gmatch("{[^{}]*}") do
    local index = tonumber(obj:match('"index"%s*:%s*(%d+)'))
    if index then
      out.cars[#out.cars + 1] = {
        index = index, carId = field(obj, "carId"), class = field(obj, "class"),
        driverName = field(obj, "driverName"), team = field(obj, "team"),
      }
    end
  end
  return out
end

local function parse(text)
  if type(JSON) == "table" and type(JSON.parse) == "function" then
    local ok, data = pcall(JSON.parse, text)
    if ok and type(data) == "table" and type(data.cars) == "table" then return data end
  end
  return parseFallback(text)
end

function SessionFile.Read()
  local p = path()
  if not p then return nil end
  local ok, text = pcall(function() return io.load and io.load(p) end)
  if not ok or type(text) ~= "string" or text == "" then return nil end
  local data = parse(text)
  if type(data.cars) ~= "table" or #data.cars == 0 then return nil end
  return data
end

-- "#rgb" / "#rrggbb" -> rgbm, or nil.
function SessionFile.ParseColor(hex)
  if type(hex) ~= "string" then return nil end
  local h = hex:match("^#?(%x+)$")
  if h and #h == 3 then h = h:sub(1, 1):rep(2) .. h:sub(2, 2):rep(2) .. h:sub(3, 3):rep(2) end
  if not h or #h ~= 6 then return nil end
  return rgbm(tonumber(h:sub(1, 2), 16) / 255, tonumber(h:sub(3, 4), 16) / 255, tonumber(h:sub(5, 6), 16) / 255, 1)
end

return SessionFile
