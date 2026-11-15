-- Reads the session file the dashboard writes next to race.ini when it starts a session.
-- Contract with Builders/SessionManifestBuilder.cs:
--   { "version": 1, "classes": [ { "name": "GT3", "order": 0, "color": "#f00" } ],
--     "cars": [ { "index": 0, "carId": "ks_...", "driverName": "...", "team": "...", "class": "GT3" } ] }
-- "index" is the car's grid index in the game (CAR_n of race.ini); the safety car is not listed.
local S = require("src/state")
local U = require("src/util")

local SessionFile = {}

local sFormat = string.format

SessionFile.FILE_NAME = "mcr_session.json"

function SessionFile.Path()
  local ok, docs = pcall(function() return ac.getFolder(ac.FolderID.Documents) end)
  if not ok or type(docs) ~= "string" or docs == "" then return nil end
  return U.NormalizePath(docs .. "/Assetto Corsa/cfg/" .. SessionFile.FILE_NAME)
end

local function unescape(s)
  return (s:gsub('\\(["\\/])', "%1"):gsub("\\u(%x%x%x%x)", function(h)
    local cp = tonumber(h, 16)
    return cp < 128 and string.char(cp) or "?"
  end))
end

-- Fallback when JSON.parse is unavailable: classes and cars are flat objects without nesting.
local function stringField(obj, key)
  local v = obj:match('"' .. key .. '"%s*:%s*"(.-)"%s*[,}]')
  return v and unescape(v) or nil
end

local function parseFallback(text)
  local out = { classes = {}, cars = {} }
  local carsAt = text:find('"cars"%s*:')
  local head = carsAt and text:sub(1, carsAt - 1) or text
  for obj in head:gmatch("{[^{}]*}") do
    local name = stringField(obj, "name")
    if name then out.classes[#out.classes + 1] = { name = name, order = tonumber(obj:match('"order"%s*:%s*(%d+)')) } end
  end
  for obj in (carsAt and text:sub(carsAt) or ""):gmatch("{[^{}]*}") do
    local index = tonumber(obj:match('"index"%s*:%s*(%d+)'))
    if index then
      out.cars[#out.cars + 1] = { index = index, carId = stringField(obj, "carId"), class = stringField(obj, "class") }
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

-- Returns the decoded file, or nil and the reason.
function SessionFile.Read()
  local path = SessionFile.Path()
  if not path then return nil, "documents folder unavailable" end

  local ok, text = pcall(function() return io.load and io.load(path) end)
  if not ok or type(text) ~= "string" or text == "" then
    return nil, "cannot read " .. path
  end

  local data = parse(text)
  if type(data.cars) ~= "table" or #data.cars == 0 then
    return nil, "file lists no cars: " .. path
  end

  U.Log(sFormat("SessionFile: read %d car(s) from %s", #data.cars, path))
  return data
end

-- Maps the file onto the grid. Returns the class names in dashboard order (only classes that have
-- a car on the grid) and a table of grid index -> class name. A car whose id differs from the file's
-- entry is left unassigned: the file then belongs to some other session.
function SessionFile.AssignClasses(data)
  local classByIndex = {}
  local used = {}

  for _, car in ipairs(data.cars) do
    local index = tonumber(car.index)
    local cls = type(car.class) == "string" and U.Trim(car.class) or ""
    if index and index >= 0 and index < S.driverCount and cls ~= "" then
      local actual = U.SafeCarID(index)
      if car.carId and actual and string.lower(actual) ~= string.lower(car.carId) then
        U.Log(sFormat("SessionFile: car %d is '%s' but the file says '%s'; ignored", index, tostring(actual), tostring(car.carId)))
      else
        classByIndex[index] = cls
        used[string.lower(cls)] = true
      end
    end
  end

  local names = {}
  local seen = {}
  local function add(name)
    local k = string.lower(name)
    if used[k] and not seen[k] then
      seen[k] = true
      names[#names + 1] = name
    end
  end

  local ordered = {}
  for _, c in ipairs(type(data.classes) == "table" and data.classes or {}) do
    if type(c) == "table" and type(c.name) == "string" then ordered[#ordered + 1] = c end
  end
  table.sort(ordered, function(a, b) return (tonumber(a.order) or 0) < (tonumber(b.order) or 0) end)
  for _, c in ipairs(ordered) do add(U.Trim(c.name)) end
  for index = 0, S.driverCount - 1 do
    if classByIndex[index] then add(classByIndex[index]) end
  end

  -- Use the spelling from the class list for every car so the same class never splits in two.
  local canonical = {}
  for _, n in ipairs(names) do canonical[string.lower(n)] = n end
  local manual = {}
  for index = 0, S.driverCount - 1 do
    manual[index] = classByIndex[index] and canonical[string.lower(classByIndex[index])] or ""
  end

  return names, manual
end

return SessionFile
