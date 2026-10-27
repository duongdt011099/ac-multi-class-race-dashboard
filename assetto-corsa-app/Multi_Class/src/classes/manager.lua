-- Release class management: add/remove, class info for the leaderboard, storage loading.
local S = require("src/state")
local U = require("src/util")
local Detect = require("src/classes/detection")
local ClsStore = require("src/classes/storage")
local Settings = require("src/settings")

local Classes = {}

local sFormat = string.format
local getCar = ac.getCar

function Classes.NormalizeClassName(name)
  local n = U.Trim(name):gsub("[=\n]", " ")
  if n:sub(1, 1) == "#" then
    n = U.Trim(n:sub(2))
  end
  return n
end

local function carMatches(carIndex, lower)
  local m
  local ok = pcall(function()
    m = Detect.CarMatchesClass(carIndex, lower)
  end)
  if not ok then m = false end
  return m
end

local function isAlreadyAdded(lower)
  for i = 1, #S.releaseClasses do
    if string.lower(S.releaseClasses[i]) == lower then
      return true
    end
  end
  return false
end

local function assignMatchingCars(n, lower)
  local matched = 0
  for i = 0, S.driverCount - 1 do
    if carMatches(i, lower) then
      S.manualDriverClass[i] = n
      local saved = ClsStore.SaveCarClass(U.SafeCarID(i), n, i)
      if not saved then
        U.Log(sFormat("AddReleaseClass WARN: car %d class '%s' assigned in memory but NOT persisted", i, n))
      end
      matched = matched + 1
    end
  end
  return matched
end

local function closestClassName(avail, lower)
  local suggestion = nil
  local bestDist = 3
  for _, an in ipairs(avail) do
    local dist = U.EditDistance(string.lower(an), lower)
    if dist < bestDist then
      bestDist = dist
      suggestion = an
    end
  end
  return suggestion
end

local function reportNoMatch(n, lower)
  local avail = Detect.GetAvailableClassNames()
  local availText = (#avail > 0) and table.concat(avail, ", ") or "(none)"
  local suggestion = closestClassName(avail, lower)
  U.Log(sFormat("AddReleaseClass FAIL: matched=0 driverCount=%d avail=%d tags_sample={}", S.driverCount, #avail))
  S.classAddError = "No cars match '" .. n .. "'. Available tags/classes: " .. availText .. " (case is ignored). [debug: driverCount=" .. S.driverCount .. "]"
  if suggestion then
    S.classAddError = S.classAddError .. " Did you mean '" .. suggestion .. "'?"
  end
end

function Classes.AddReleaseClass(name)
  local n = Classes.NormalizeClassName(name)
  U.Log(sFormat("AddReleaseClass: name='%s' normalized='%s' driverCount=%d", tostring(name), n, S.driverCount))
  if n == "" then
    S.classAddError = "Please choose the class name."
    return
  end

  local lower = string.lower(n)
  if isAlreadyAdded(lower) then
    S.classAddError = "Class '" .. n .. "' is already added."
    return
  end

  local matched = assignMatchingCars(n, lower)
  if matched == 0 then
    reportNoMatch(n, lower)
    return
  end

  S.releaseClasses[#S.releaseClasses + 1] = n
  S.classAddError = nil
  S.classAddInput = ""
  ClsStore.SaveClassNames()
  Classes.BuildClassInfo()
  U.Log(sFormat("AddReleaseClass DONE: '%s' matched=%d releaseClasses={%s}", n, matched, table.concat(S.releaseClasses, ",")))
  Classes.VerifyClassAssignments()
end

function Classes.VerifyClassAssignments()
  local cars = {}
  local tlist = {}
  for i = 0, S.driverCount - 1 do
    local cls = S.manualDriverClass[i]
    if not cls or cls == "" then
      local carName = ac.getCarName and ac.getCarName(i)
      local carID = ac.getCarID and ac.getCarID(i)
      cars[#cars + 1] = carName or carID or sFormat("Car %d", i + 1)
      local tags = Detect.GetCarTags(i)
      tlist[#tlist + 1] = (#tags > 0) and table.concat(tags, ", ") or "(no tag)"
    end
  end

  if #cars == 0 then
    ui.toast(ui.Icons.Play, sFormat("All %d car(s) on the grid belong to an added class.", S.driverCount))
    return
  end
  local details = {}
  for j = 1, #cars do
    details[#details + 1] = cars[j] .. " (" .. tlist[j] .. ")"
  end
  ui.toast(ui.Icons.Warning, sFormat("%d car(s) are not assigned to any class: %s", #cars, table.concat(details, ", ")))
end

function Classes.RemoveReleaseClass(idx)
  local n = S.releaseClasses[idx]
  if not n then return end
  table.remove(S.releaseClasses, idx)
  for i = 0, S.driverCount - 1 do
    if S.manualDriverClass[i] == n then
      S.manualDriverClass[i] = ""
      ClsStore.SaveCarClass(U.SafeCarID(i), "", i)
    end
  end
  S.classAddError = nil
  ClsStore.SaveClassNames()
  Classes.BuildClassInfo()
end

-- Display-only fallback: used exclusively by the leaderboard CLASS panel so that
-- unassigned cars still get a sensible class position. This map must never feed
-- VerifyClassAssignments or ReorderGridByClass, otherwise unadded classes would
-- be reported as added ones.
local function displayKeyFor(i, key)
  if key then return key end
  local detected = Detect.GetCarClassCached(i)
  if detected and detected ~= "" then
    return "class" .. string.lower(detected)
  end
  return nil
end

-- Fills driverClass/displayClass and returns the lowest starting position per class key.
local function assignDriverClasses()
  local classMinPos = {}
  for i = 0, S.driverCount - 1 do
    local raw = S.manualDriverClass[i]
    local key
    if raw and raw ~= "" then
      key = "class" .. string.lower(raw)
    end

    S.driverClass[i] = key
    S.displayClass[i] = displayKeyFor(i, key)

    if key then
      local pos = S.allDriversStartingPos[i] or (i + 1)
      if not classMinPos[key] or pos < classMinPos[key] then
        classMinPos[key] = pos
      end
    end
  end
  return classMinPos
end

local function bestLapOfClass(key)
  local best = nil
  for i = 0, S.driverCount - 1 do
    if S.driverClass[i] == key then
      local ok, car = pcall(getCar, i)
      if ok and car then
        local okBt, btMs = pcall(function() return car.bestLapTimeMs end)
        if okBt and btMs and type(btMs) == "number" and btMs > 0 then
          if best == nil or btMs < best then
            best = btMs
          end
        end
      end
    end
  end
  return best
end

local function sortClassOrder(classMinPos)
  local classBestLapMs = {}
  for _, key in ipairs(S.classOrder) do
    classBestLapMs[key] = bestLapOfClass(key)
  end

  table.sort(S.classOrder, function(a, b)
    if classBestLapMs[a] and classBestLapMs[b] then
      return classBestLapMs[a] < classBestLapMs[b]
    end
    if classBestLapMs[a] then return true end
    if classBestLapMs[b] then return false end
    return classMinPos[a] < classMinPos[b]
  end)
end

function Classes.BuildClassInfo()
  U.Log(sFormat("BuildClassInfo: dc=%d entries=%d", S.driverCount, #S.manualDriverClass))
  S.driverClass = {}
  S.displayClass = {}
  S.classOrder = {}

  if S.driverCount == 0 then return end

  local classMinPos = assignDriverClasses()
  for key, _ in pairs(classMinPos) do
    S.classOrder[#S.classOrder + 1] = key
  end
  sortClassOrder(classMinPos)

  local okPos, posMod = pcall(require, "src/leaderboard/positions")
  if okPos and posMod and posMod.InvalidateLeaderboardCache then
    pcall(function() posMod.InvalidateLeaderboardCache() end)
  end

  U.Log(sFormat("BuildClassInfo DONE: %d classes order={%s}", #S.classOrder, table.concat(S.classOrder, ",")))
end

local function loadPerCarClasses()
  local anyLoaded = false
  local seen = {}
  for i = 0, S.driverCount - 1 do
    local cls = ClsStore.LoadCarClass(U.SafeCarID(i), i)
    if type(cls) == "string" and cls ~= "" then
      S.manualDriverClass[i] = cls
      anyLoaded = true
      if not seen[cls] then
        seen[cls] = true
        S.releaseClasses[#S.releaseClasses + 1] = cls
      end
    else
      S.manualDriverClass[i] = ""
    end
  end
  return anyLoaded
end

local function redetectClass(name)
  local lower = string.lower(name)
  local matched = 0
  for i = 0, S.driverCount - 1 do
    if carMatches(i, lower) then
      S.manualDriverClass[i] = name
      ClsStore.SaveCarClass(U.SafeCarID(i), name, i)
      matched = matched + 1
    end
  end
  if matched > 0 then
    S.releaseClasses[#S.releaseClasses + 1] = name
  end
  U.Log(sFormat("LoadManualClassesFromStorage: re-detected class '%s' matched %d cars", name, matched))
end

local function redetectFromSavedNames()
  local savedNames = ClsStore.LoadClassNames()
  if savedNames and savedNames ~= "" then
    U.Log(sFormat("LoadManualClassesFromStorage: per-car storage empty, re-detecting from saved class names: %s", savedNames))
    for name in savedNames:gmatch("[^|]+") do
      redetectClass(name)
    end
  end
end

function Classes.LoadManualClassesFromStorage()
  U.Log(sFormat("LoadManualClassesFromStorage: START driverCount=%d", S.driverCount))
  S.releaseClasses = {}
  local anyLoaded = loadPerCarClasses()
  U.Log(sFormat("LoadManualClassesFromStorage: DONE anyLoaded=%s releaseClasses=%d classes={%s}", tostring(anyLoaded), #S.releaseClasses, table.concat(S.releaseClasses, ",")))
  if not anyLoaded then
    redetectFromSavedNames()
  end
  Settings.RestoreStoredSettings()

  local okBuild, buildErr = pcall(Classes.BuildClassInfo)
  if not okBuild then
    U.Log("LoadManualClassesFromStorage: BuildClassInfo FAILED err=" .. tostring(buildErr))
  end
end

return Classes
