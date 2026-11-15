-- Release class management: class info for the leaderboard, loaded from the dashboard's session file.
local S = require("src/state")
local U = require("src/util")
local SessionFile = require("src/classes/session_file")
local Settings = require("src/settings")

local Classes = {}

local sFormat = string.format
local getCar = ac.getCar

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
    S.displayClass[i] = key

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

-- The dashboard writes the cars and their classes when it starts the session, so nothing is
-- detected here: every car on the grid takes the class the file lists for its grid index.
function Classes.LoadClassesFromSessionFile()
  U.Log(sFormat("LoadClassesFromSessionFile: START driverCount=%d", S.driverCount))
  S.releaseClasses = {}
  S.manualDriverClass = {}

  local session, err = SessionFile.Read()
  if session then
    S.releaseClasses, S.manualDriverClass = SessionFile.AssignClasses(session)
  else
    U.Log("LoadClassesFromSessionFile: no usable session file: " .. tostring(err))
  end

  U.Log(sFormat("LoadClassesFromSessionFile: DONE classes=%d {%s}", #S.releaseClasses, table.concat(S.releaseClasses, ",")))
  Settings.RestoreStoredSettings()

  local okBuild, buildErr = pcall(Classes.BuildClassInfo)
  if not okBuild then
    U.Log("LoadClassesFromSessionFile: BuildClassInfo FAILED err=" .. tostring(buildErr))
  end
end

return Classes
