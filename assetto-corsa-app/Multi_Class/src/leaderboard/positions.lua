-- Leaderboard data: overall/class positions, gaps, lap info and best lap times.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")

local M = {}

local getCar = ac.getCar

local raceCache = {
  lastTime = -1000000.0,
  interval = 7.0,
  valid = false,
  overallPos = 1,
  totalCars = 0,
  classPos = 1,
  classTotal = 0,
  gapFront = nil,
  gapBehind = nil,
}

local qualiCache = {
  lastTime = -1000000.0,
  interval = 7.0,
  valid = false,
  overallPos = 1,
  totalCars = 0,
  classPos = 1,
  classTotal = 0,
  classFastestMs = nil,
  playerBestMs = nil,
}

function M.LeaderboardProgress(carIndex)
  local car = getCar(carIndex)
  if not car then return 0 end
  return (car.sessionLapCount or 0) + (car.splinePosition or 0)
end

function M.GetCarBestLapMs(idx)
  local ok, car = pcall(getCar, idx)
  if ok and car then
    local okBt, bt = pcall(function() return car.bestLapTimeMs end)
    if okBt and type(bt) == "number" and bt > 0 then return bt end
  end
  return nil
end

function M.InvalidateLeaderboardCache()
  raceCache.valid = false
  raceCache.lastTime = -1000000.0
  qualiCache.valid = false
  qualiCache.lastTime = -1000000.0
end

-- The app runs a rolling start in every race session, so AC counts the formation lap as lap 1
-- and the racing laps are AC's laps 2..N+1. The formation lap is ignored for the display: it is
-- not counted at all (the LAP panel reads "-" until the player crosses the line for the first
-- time) and the racing laps are numbered from 1.
local function RollingStartFormationLap()
  if not Sess.IsRaceMode() then return false end
  local state = S.rollingStartState
  return state == "staged" or state == "rolling" or state == "complete"
end

function M.GetCurrentLapInfo()
  local sim = ac.getSim()
  local playerCar = getCar(0)

  local totalLaps = 0
  local session = ac.getSession(sim.currentSessionIndex)
  if session then
    totalLaps = session.laps or 0
  end

  -- lapOffset is how many laps AC counts before the racing laps begin: the formation lap when
  -- the app runs a rolling start, none otherwise. It is applied to the lap number and to the
  -- total, so the panel counts racing laps only.
  local lapOffset = 0
  if RollingStartFormationLap() then
    lapOffset = 1
    if totalLaps > 0 then totalLaps = totalLaps - 1 end
  end

  local playerLapCount = 0
  if playerCar then
    playerLapCount = playerCar.lapCount or 0
  end
  -- AC's lap count plus one is the lap being driven; the formation lap is taken off it, so the
  -- first racing lap is 1. The rolling-start lap offset is applied only to the exported
  -- classification, never to the display.
  local lapRaw = playerLapCount + 1 - lapOffset
  -- The formation lap is not a racing lap, so it is never shown.
  local formationLap = lapRaw < 1
  local currentLap = totalLaps > 0 and math.min(totalLaps, lapRaw) or lapRaw

  local speed = playerCar and (playerCar.speedKmh or 0) or 0
  if S.playerStoppedState then
    if speed > 8.0 then S.playerStoppedState = false end
  else
    if speed < 2.0 then S.playerStoppedState = true end
  end

  return currentLap, totalLaps, S.playerStoppedState, formationLap
end

-- Builds the overall and same-class lists as { idx, [key] = valueFn(i) } entries.
local function collectCars(key, valueFn)
  local playerClass = S.displayClass[0]
  local allCars, classCars = {}, {}
  local totalCars, classTotal = 0, 0

  for i = 0, S.driverCount - 1 do
    local car = getCar(i)
    if car then
      local value = valueFn(i)
      totalCars = totalCars + 1
      allCars[#allCars + 1] = { idx = i, [key] = value }

      -- playerClass can be nil when no class could be resolved; without this guard
      -- 'nil == nil' would match every car and inflate classTotal up to totalCars.
      if playerClass and S.displayClass[i] == playerClass then
        classTotal = classTotal + 1
        classCars[#classCars + 1] = { idx = i, [key] = value }
      end
    end
  end
  return allCars, classCars, totalCars, classTotal
end

-- progSorter tolerates a missing prog (nil) so a sort can never abort clLeaderboard.
-- Mirrors bestSorter: cars with a value sort ahead of those without, ties broken by idx.
local function progSorter(a, b)
  if a.prog and b.prog then return a.prog > b.prog end
  if a.prog then return true end
  if b.prog then return false end
  return a.idx < b.idx
end

local function bestSorter(a, b)
  if a.best and b.best then return a.best < b.best end
  if a.best then return true end
  if b.best then return false end
  return a.idx < b.idx
end

local function findPlayerRank(list)
  for i, car in ipairs(list) do
    if car.idx == 0 then return i end
  end
  return nil
end

-- A car that is in the pits (or retired there) is no real neighbour on track: skip it so the gap
-- is measured to the nearest car that is actually racing.
local function isRacingNeighbour(idx)
  local car = getCar(idx)
  return car ~= nil and not (car.isInPitlane == true or car.isInPit == true)
end

local function classGapTo(classCars, from, step)
  for i = from, step > 0 and #classCars or 1, step do
    local idx = classCars[i].idx
    if isRacingNeighbour(idx) then
      local g = ac.getGapBetweenCars(0, idx)
      return U.IsFinite(g) and g or nil
    end
  end
  return nil
end

local function classGaps(classCars, classPos)
  return classGapTo(classCars, classPos - 1, -1), classGapTo(classCars, classPos + 1, 1)
end

-- CSP computes car.racePosition every frame; ac.getCarLeaderboardPosition() is the
-- legacy Python leaderboard and returns a frozen classification, so it must not be used here.
local function enginePosition(idx)
  local frozenPosition = S.finishedRacePositions[idx]
  if frozenPosition and frozenPosition > 0 then
    return frozenPosition
  end

  local ok, p = pcall(function() return getCar(idx).racePosition end)
  if ok and type(p) == "number" and p > 0 then
    return p
  end
  return nil
end

local function debounceEnginePosition(enginePos)
  local pendingPos = S.raceDisplayPending
  if pendingPos and pendingPos.value == enginePos then
    pendingPos.frames = pendingPos.frames + 1
  else
    pendingPos = { value = enginePos, frames = 1 }
    S.raceDisplayPending = pendingPos
  end

  local stable = enginePos
  if pendingPos.frames >= C.RACE_POS_STABLE_FRAMES then
    S.raceDisplayStable = enginePos
  elseif S.raceDisplayStable and S.raceDisplayStable > 0 then
    stable = S.raceDisplayStable
  end
  return stable
end

local function engineClassPosition(classCars, stable)
  local classPos = 1
  for _, car in ipairs(classCars) do
    if car.idx ~= 0 then
      local rp = enginePosition(car.idx)
      if rp and rp < stable then
        classPos = classPos + 1
      end
    end
  end
  return classPos
end

-- Prefers the engine classification (debounced) over the progress-derived ranks.
local function applyEnginePosition(classCars, overallPos, classPos)
  local enginePos = enginePosition(0)
  if enginePos then
    local stable = debounceEnginePosition(enginePos)
    return stable, engineClassPosition(classCars, stable)
  end
  if S.raceDisplayStable and S.raceDisplayStable > 0 then
    -- Engine has no classification for this car (pre-start, pits, retired): keep showing
    -- the last known good position instead of snapping back to a progress-derived guess.
    overallPos = S.raceDisplayStable
  end
  return overallPos, classPos
end

function M.GetPlayerPositions()
  local sim = ac.getSim()
  local currentLap, totalLaps, stopped, formationLap = M.GetCurrentLapInfo()
  local now = 0
  if sim and type(sim.time) == "number" then
    now = sim.time / 1000
  end

  if raceCache.valid and now - raceCache.lastTime < raceCache.interval then
    local cachedGapsFront = raceCache.gapFront
    local cachedGapsBehind = raceCache.gapBehind
    if not sim.isSessionStarted or stopped then
      cachedGapsFront = nil
      cachedGapsBehind = nil
    end
    return raceCache.overallPos, raceCache.totalCars, raceCache.classPos, raceCache.classTotal,
      cachedGapsFront, cachedGapsBehind, currentLap, totalLaps, stopped, formationLap
  end

  local allCars, classCars, totalCars, classTotal = collectCars("prog", M.LeaderboardProgress)
  table.sort(allCars, progSorter)
  table.sort(classCars, progSorter)

  local overallPos = findPlayerRank(allCars) or 1
  local classPos = 1
  local gapFront, gapBehind = nil, nil
  local classRank = findPlayerRank(classCars)
  if classRank then
    classPos = classRank
    gapFront, gapBehind = classGaps(classCars, classRank)
  end

  if not sim.isSessionStarted or stopped then
    gapFront, gapBehind = nil, nil
  end

  overallPos, classPos = applyEnginePosition(classCars, overallPos, classPos)

  raceCache.overallPos = overallPos
  raceCache.totalCars = totalCars
  raceCache.classPos = classPos
  raceCache.classTotal = classTotal
  raceCache.gapFront = gapFront
  raceCache.gapBehind = gapBehind
  raceCache.lastTime = now
  raceCache.valid = true

  return overallPos, totalCars, classPos, classTotal, gapFront, gapBehind, currentLap, totalLaps, stopped,
    formationLap
end

function M.GetPlayerPositionsQuali()
  local sim = ac.getSim()
  local now = 0
  if sim and type(sim.time) == "number" then
    now = sim.time / 1000
  end

  if qualiCache.valid and now - qualiCache.lastTime < qualiCache.interval then
    return qualiCache.overallPos, qualiCache.totalCars, qualiCache.classPos, qualiCache.classTotal,
      qualiCache.classFastestMs, qualiCache.playerBestMs
  end

  local allCars, classCars, totalCars, classTotal = collectCars("best", M.GetCarBestLapMs)
  table.sort(allCars, bestSorter)
  table.sort(classCars, bestSorter)

  local overallPos = findPlayerRank(allCars) or 1
  local classPos = findPlayerRank(classCars) or 1

  local classFastestMs = nil
  for _, c in ipairs(classCars) do
    if c.best then
      classFastestMs = c.best
      break
    end
  end

  local playerBestMs = M.GetCarBestLapMs(0)

  qualiCache.overallPos = overallPos
  qualiCache.totalCars = totalCars
  qualiCache.classPos = classPos
  qualiCache.classTotal = classTotal
  qualiCache.classFastestMs = classFastestMs
  qualiCache.playerBestMs = playerBestMs
  qualiCache.lastTime = now
  qualiCache.valid = true

  return overallPos, totalCars, classPos, classTotal, classFastestMs, playerBestMs
end

return M
