-- Starting grid recording and class-based grid ordering.
local S = require("src/state")
local U = require("src/util")
local Classes = require("src/classes/manager")
local Pos = require("src/leaderboard/positions")

local Grid = {}

local sFormat = string.format
local getCar = ac.getCar

function Grid.RecordStartingPositions()
  S.allDriversStartingPos = {}
  S.rollingStartGridPositions = {}
  for i = 0, S.driverCount - 1 do
    local car = getCar(i)
    if car then
      local position = car.racePosition
      if type(position) ~= "number" or position <= 0 then
        position = i + 1
      end
      S.allDriversStartingPos[i] = position
      S.rollingStartGridPositions[i] = position
    end
  end
  Classes.BuildClassInfo()
end

local function getBestLap(idx)
  local ok, car = pcall(getCar, idx)
  if ok and car then
    local okBt, bt = pcall(function() return car.bestLapTimeMs end)
    if okBt and bt and type(bt) == "number" and bt > 0 then return bt end
  end
  return 999999999
end

local function byBestLap(a, b)
  return getBestLap(a) < getBestLap(b)
end

local function groupCarsByClass()
  local classSlots = {}
  for _, key in ipairs(S.classOrder) do
    classSlots[key] = {}
  end
  local noClass = {}
  for i = 0, S.driverCount - 1 do
    local cls = S.driverClass[i]
    if cls and classSlots[cls] then
      classSlots[cls][#classSlots[cls] + 1] = i
    else
      noClass[#noClass + 1] = i
    end
  end
  return classSlots, noClass
end

local function buildOrder(classSlots, noClass)
  local ordered = {}
  for _, key in ipairs(S.classOrder) do
    for _, idx in ipairs(classSlots[key]) do
      ordered[#ordered + 1] = idx
    end
  end
  for _, idx in ipairs(noClass) do
    ordered[#ordered + 1] = idx
  end
  return ordered
end

function Grid.ReorderGridByClass()
  if #S.classOrder == 0 or S.driverCount == 0 then return end

  local classSlots, noClass = groupCarsByClass()
  for _, key in ipairs(S.classOrder) do
    table.sort(classSlots[key], byBestLap)
  end
  table.sort(noClass, byBestLap)

  local pos = 1
  for _, idx in ipairs(buildOrder(classSlots, noClass)) do
    S.allDriversStartingPos[idx] = pos
    pos = pos + 1
  end

  pcall(function() Pos.InvalidateLeaderboardCache() end)
  U.Log(sFormat("ReorderGridByClass: reordered=%d drivers, classes={%s}", S.driverCount, table.concat(S.classOrder, ",")))
end

return Grid
