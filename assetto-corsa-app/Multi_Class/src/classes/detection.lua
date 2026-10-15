-- Car class / tag detection from CSP tags, ui_car.json and car.ini.
local S = require("src/state")
local U = require("src/util")

local Detect = {}

local function parseUiCarJson(data, out)
  local cls = data:match('"class"%s*:%s*"([^"]*)"')
  if cls then
    out.class = cls
  end
  local tagsSection = data:match('"tags"%s*:%s*%[(.-)%]')
  if tagsSection then
    for q in tagsSection:gmatch('"([^"]*)"') do
      out.tags[#out.tags + 1] = q
    end
  end
end

local function readUiCar(uiDir, out)
  local data = nil
  local okLoad = pcall(function()
    data = io.load and io.load(uiDir .. "/ui_car.json")
  end)
  if okLoad and data and type(data) == "string" and #data > 0 then
    parseUiCarJson(data, out)
  end
  pcall(function()
    local files = io.scanDir and io.scanDir(uiDir, "badge*")
    if files and #files > 0 then
      out.badge = uiDir .. "/" .. files[1]
    end
  end)
end

function Detect.GetCarUISource(carIndex)
  local cached = S.uiSourceCache[carIndex]
  if cached then return cached end
  local out = { tags = {}, class = nil, badge = nil }
  local okId, carID = pcall(function()
    return ac.getCarID and ac.getCarID(carIndex)
  end)
  local folder = nil
  local okFolder = pcall(function()
    folder = ac.getFolder and ac.getFolder(ac.FolderID and ac.FolderID.ContentCars)
  end)
  if okId and carID and okFolder and folder and folder ~= "" then
    readUiCar(folder .. "/" .. carID .. "/ui", out)
  end
  S.uiSourceCache[carIndex] = out
  return out
end

local function collectTags(t, out)
  if type(t) ~= "table" then return end
  for _, val in ipairs(t) do
    if type(val) == "string" then
      local tt = U.Trim(val)
      if tt:sub(1, 1) == "#" then
        tt = tt:sub(2)
      end
      if tt ~= "" then
        out[#out + 1] = tt
      end
    end
  end
end

function Detect.GetCarTags(carIndex)
  local ok, tags = pcall(function()
    return ac.getCarTags and ac.getCarTags(carIndex)
  end)
  if not ok then tags = nil end
  local out = {}
  if tags then
    collectTags(tags, out)
  end
  collectTags(Detect.GetCarUISource(carIndex).tags, out)
  return out
end

local function classFromIni(carIndex)
  local ok, config = pcall(function()
    return ac.INIConfig and ac.INIConfig.carData(carIndex, 'car.ini')
  end)
  if not (ok and config) then return nil end
  local ok2, val = pcall(function()
    return config:get('BASIC', 'CLASS', '')
  end)
  if ok2 and type(val) == "string" then
    local v = U.Trim(val)
    if v ~= "" then return v end
  end
  return nil
end

function Detect.GetCarClassCached(carIndex)
  local cached = S.carClassCache[carIndex]
  if cached ~= nil then
    return (cached ~= "") and cached or nil
  end
  local cls = nil
  local ui = Detect.GetCarUISource(carIndex)
  if ui.class then
    local v = U.Trim(ui.class)
    if v ~= "" then
      cls = v
    end
  end
  if not cls then
    cls = classFromIni(carIndex)
  end
  S.carClassCache[carIndex] = cls or ""
  return cls
end

function Detect.TagMatchesWord(lowerTag, lowerClass)
  if lowerClass == "" then return false end
  if lowerTag == lowerClass then return true end
  local esc = lowerClass:gsub("([^%w])", "%%%1")
  if lowerTag:find("^" .. esc .. "%f[%A]") then return true end
  if lowerTag:find("%f[%w]" .. esc .. "%f[%A]") then return true end
  if lowerTag:find("%f[%w]" .. esc .. "$") then return true end
  return false
end

function Detect.CarMatchesClass(carIndex, lowerClass)
  for _, tt in ipairs(Detect.GetCarTags(carIndex)) do
    if Detect.TagMatchesWord(string.lower(tt), lowerClass) then
      return true
    end
  end
  local cc = Detect.GetCarClassCached(carIndex)
  if cc and Detect.TagMatchesWord(string.lower(cc), lowerClass) then
    return true
  end
  local carName = ac.getCarName and ac.getCarName(carIndex)
  if carName and string.lower(U.Trim(carName)) == lowerClass then
    return true
  end
  local carID = ac.getCarID and ac.getCarID(carIndex)
  if carID and string.lower(U.Trim(carID)) == lowerClass then
    return true
  end
  return false
end

local function addUnique(out, seen, value)
  local k = string.lower(value)
  if not seen[k] then
    seen[k] = true
    out[#out + 1] = value
  end
end

function Detect.GetAvailableClassNames()
  local out = {}
  local seen = {}
  for i = 0, S.driverCount - 1 do
    for _, tt in ipairs(Detect.GetCarTags(i)) do
      addUnique(out, seen, tt)
    end
    local cc = Detect.GetCarClassCached(i)
    if cc then
      addUnique(out, seen, cc)
    end
  end
  return out
end

return Detect
