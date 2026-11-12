-- Timed race mode. Assetto Corsa only knows lap-limited races, so the dashboard launches the race
-- with a lap count of 999 and passes the race length in race.ini ([HEADER] __MCR_RACE_TIME_MINUTES).
-- This module runs the race clock and decides when the race ends:
--   * the clock starts at the green flag and only advances while the sim is not paused;
--   * when the time is up, the overall leader is on the lap he is driving; he finishes the race
--     when he crosses the line, so that lap count becomes the race distance (S.timedFinishLaps);
--   * from then on the rest of the app treats it like an ordinary lap-limited race (last-lap
--     notice, frozen positions) because S.liveTotalLaps carries that distance;
--   * AC itself never ends the session (it thinks the race is 999 laps), so the result is armed
--     here once every car has taken the flag (see TimedRace.IsComplete).
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")

local T = {}

local getCar = ac.getCar
local sFormat = string.format

-- Per-session state.
local sessionKey = nil
local leaderFinishedLaps = nil   -- per-car lap counts recorded when the leader took the flag
local leaderFinishedAt = nil     -- race clock when the leader took the flag

local lapCountOf
local finishedAt = {}
local teleported = {}
local finishClock = 0.0
local pitDone, pitHoldTeleportAt = {}, {}
local pitHoldClock, nextPitHoldAt = 0.0, 0.0

local function resetRace()
  finishedAt, teleported, finishClock = {}, {}, 0.0
  pitDone, pitHoldTeleportAt = {}, {}
  pitHoldClock, nextPitHoldAt = 0.0, 0.0
  S.raceLeaderFinished = false
  S.timedRaceSeconds = 0.0
  S.timedRaceTimeUp = false
  S.timedFinishLaps = nil
  S.timedRaceComplete = false
  leaderFinishedLaps = nil
  leaderFinishedAt = nil
end

local function readConfiguredMinutes()
  if not ac.INIConfig or type(ac.INIConfig.raceConfig) ~= "function" then return 0 end
  local okConfig, config = pcall(ac.INIConfig.raceConfig)
  if not okConfig or not config then return 0 end
  local okValue, value = pcall(function()
    return config:get("HEADER", "__MCR_RACE_TIME_MINUTES", 0)
  end)
  value = okValue and tonumber(value) or 0
  if not value or value <= 0 then return 0 end
  return math.max(C.TIMED_RACE_MIN_MINUTES, math.min(C.TIMED_RACE_MAX_MINUTES, math.floor(value)))
end

-- True while this race is a timed race (the dashboard asked for one).
function T.IsTimedRace()
  return (S.timedRaceMinutes or 0) > 0
end

-- Seconds left on the race clock (0 once the time is up), or nil when this is not a timed race.
function T.SecondsLeft()
  if not T.IsTimedRace() then return nil end
  return math.max(0, S.timedRaceMinutes * 60 - (S.timedRaceSeconds or 0))
end

function T.FormatClock(seconds)
  seconds = math.max(0, math.ceil(seconds or 0))
  local h = math.floor(seconds / 3600)
  local m = math.floor((seconds % 3600) / 60)
  local s = seconds % 60
  return sFormat("%02d:%02d:%02d", h, m, s)
end

-- Cars that took the flag are sent to the pits a few seconds later.

local function updateFinishTeleports(dt)
  finishClock = finishClock + dt
  for i = 0, S.driverCount - 1 do
    if not teleported[i] then
      local lc = lapCountOf(i)
      if finishedAt[i] == nil then
        local crossedAfterLeader = leaderFinishedLaps ~= nil and lc > (leaderFinishedLaps[i] or lc)
        if lc >= S.timedFinishLaps or crossedAfterLeader then
          finishedAt[i] = finishClock
          -- An AI car that has taken the flag slows down instead of racing on.
          if i ~= 0 then pcall(physics.setGentleStop, i, true) end
        end
      elseif finishClock - finishedAt[i] >= C.TIMED_RACE_PIT_DELAY_SECONDS then
        teleported[i] = true
        local ok, err = pcall(physics.teleportCarTo, i, ac.SpawnSet.Pits)
        -- The AI would otherwise drive straight out of the pits again; keep it stopped there.
        if i ~= 0 then pcall(physics.setGentleStop, i, true) end
        U.Log(sFormat("TIMED: car %d finished, teleport to pits ok=%s %s", i, tostring(ok), ok and "" or tostring(err)))
      end
    end
  end
end

-- Lap distance the race is decided over: the real session lap limit for an ordinary race, the
-- leader's final lap for a timed race (0 while that is not known yet, which every consumer
-- already reads as "no lap limit").
function T.TotalLaps(sessionLaps)
  if not T.IsTimedRace() then return sessionLaps or 0 end
  return S.timedFinishLaps or 0
end

local function raceClockRunning(sim)
  if sim.isSessionStarted ~= true or not Sess.IsRaceMode(sim) then return false end
  local state = S.rollingStartState
  if state == "complete" then return true end
  -- The clock starts with the first green light of the first class that is released.
  if state == "staged" or state == "rolling" then
    for _, car in pairs(S.rollingStartCars or {}) do
      if car.green then return true end
    end
    return false
  end
  -- Without a rolling start (unsupported track, staging failed) the race is a plain standing start.
  if state == "unavailable" then
    local untilStart = sim.timeToSessionStart
    return type(untilStart) ~= "number" or untilStart <= 0
  end
  return false
end

-- The car that leads the race right now: highest race progress.
local function findLeader()
  local best, bestProgress = nil, -1
  for i = 0, S.driverCount - 1 do
    local ok, car = pcall(getCar, i)
    if ok and car then
      local progress = (car.lapCount or 0) + (car.splinePosition or 0)
      if progress > bestProgress then best, bestProgress = i, progress end
    end
  end
  return best
end

function lapCountOf(i)
  local ok, lc = pcall(function() return getCar(i).lapCount end)
  return (ok and type(lc) == "number") and lc or 0
end

local function onTimeUp()
  S.timedRaceTimeUp = true
  local leader = findLeader()
  if leader == nil then return end
  -- The leader is on the lap he is driving now; completing it ends the race.
  S.timedFinishLaps = lapCountOf(leader) + 1
  S.liveTotalLaps = S.timedFinishLaps
  U.Log(sFormat("TIMED: time up, leader car %d on lap %d; race ends at lap count %d",
      leader, lapCountOf(leader) + 1, S.timedFinishLaps))
end

local function onLeaderFinished()
  leaderFinishedLaps = {}
  for i = 0, S.driverCount - 1 do leaderFinishedLaps[i] = lapCountOf(i) end
  leaderFinishedAt = S.timedRaceSeconds
  S.raceLeaderFinished = true
  U.Log("TIMED: leader took the flag")
end

local function leaderHasFinished()
  if not S.timedFinishLaps then return false end
  for i = 0, S.driverCount - 1 do
    if lapCountOf(i) >= S.timedFinishLaps then return true end
  end
  return false
end

local function carInPits(car)
  return U.SafeBool(car, "isInPit") == true or U.SafeBool(car, "isInPitlane") == true
end

-- Once the race leader has taken the flag, an AI car that is in the pits (CSP's damage teleport, a
-- crash-guard lock, the finish teleport) is out of the race for good: it counts as done and is kept there. Without
-- this a repaired car drives back out, is no longer "done", and the race result is never saved.
local function holdPitCars(dt)
  pitHoldClock = pitHoldClock + dt
  if pitHoldClock < nextPitHoldAt then return end
  nextPitHoldAt = pitHoldClock + 0.5
  for i = 1, S.driverCount - 1 do
    local okCar, car = pcall(getCar, i)
    if okCar and car then
      if not pitDone[i] and carInPits(car) then
        pitDone[i] = true
        U.Log(sFormat("TIMED: car %d is in the pits after the leader finished; it is out of the race", i))
      end
      if pitDone[i] then
        pcall(physics.setGentleStop, i, true)
        pcall(physics.setAITopSpeed, i, 0.0)
        pcall(physics.setAIThrottleLimit, i, 0.0)
        pcall(physics.setAIStopCounter, i, C.ROLLING_START_STOP_COUNTER_HELD)
        if not carInPits(car) and physics.teleportCarTo and ac.SpawnSet
            and pitHoldClock - (pitHoldTeleportAt[i] or -1000.0) >= 2.0 then
          pitHoldTeleportAt[i] = pitHoldClock
          pcall(physics.teleportCarTo, i, ac.SpawnSet.Pits)
          U.Log(sFormat("TIMED: car %d left the pits after the leader finished; sent back", i))
        end
      end
    end
  end
end

local function carDone(i)
  if pitDone[i] then return true end
  local okCar, car = pcall(getCar, i)
  if not okCar or not car then return true end
  local lc = lapCountOf(i)
  if lc >= S.timedFinishLaps then return true end
  -- A lapped car takes the flag the next time it crosses the line after the leader.
  if leaderFinishedLaps and lc > (leaderFinishedLaps[i] or lc) then return true end
  local okPit, inPit = pcall(function() return car.isInPit end)
  if okPit and inPit == true then return true end
  local okLane, inPitlane = pcall(function() return car.isInPitlane end)
  return okLane and inPitlane == true
end

local function allCarsDone()
  local done = true
  for i = 0, S.driverCount - 1 do
    if not carDone(i) then done = false end
  end
  return done
end

-- A new or restarted race session starts the clock over.
function T.OnSessionStart()
  resetRace()
  S.timedRaceMinutes = 0
  sessionKey = nil
end

-- Runs every frame while the sim is live (never while paused, so the clock stops with the game).
function T.Update(sim, dt)
  local key = Sess.GetCurrentSessionKey(sim)
  if key ~= sessionKey then
    sessionKey = key
    resetRace()
    S.timedRaceMinutes = Sess.IsRaceMode(sim) and readConfiguredMinutes() or 0
    if S.timedRaceMinutes > 0 then
      U.Log(sFormat("TIMED: timed race of %d minutes", S.timedRaceMinutes))
    end
  end
  if not T.IsTimedRace() then
    -- Lap race: the leader has finished once any car has completed the race distance.
    local laps = S.liveTotalLaps or 0
    if not S.raceLeaderFinished and laps > 0 and sim.isSessionStarted == true and Sess.IsRaceMode(sim) then
      for i = 0, S.driverCount - 1 do
        if lapCountOf(i) >= laps then
          S.raceLeaderFinished = true
          U.Log(sFormat("RACE: car %d took the flag; the pits are closed", i))
          break
        end
      end
    end
    if S.raceLeaderFinished then holdPitCars(dt) end
    -- Lap race: the same clock simply counts up from the green flag (shown in the time panel).
    if raceClockRunning(sim) and not Sess.IsSessionFinished(sim) then
      S.timedRaceSeconds = S.timedRaceSeconds + dt
    end
    return
  end
  if S.timedFinishLaps then updateFinishTeleports(dt) end
  if S.raceLeaderFinished then holdPitCars(dt) end
  if S.timedRaceComplete then return end

  if not S.timedRaceTimeUp then
    if raceClockRunning(sim) then
      S.timedRaceSeconds = S.timedRaceSeconds + dt
      if S.timedRaceSeconds >= S.timedRaceMinutes * 60 then onTimeUp() end
    end
    return
  end

  if not S.timedFinishLaps then
    -- Time ran out before any car could be picked as leader; try again.
    onTimeUp()
    return
  end

  if leaderFinishedLaps == nil then
    if leaderHasFinished() then onLeaderFinished() end
    return
  end

  if allCarsDone() or S.timedRaceSeconds - leaderFinishedAt > C.TIMED_RACE_FINISH_TIMEOUT_SECONDS then
    S.timedRaceComplete = true
    U.Log("TIMED: every car has taken the flag; race complete")
  end
  S.timedRaceSeconds = S.timedRaceSeconds + dt
end

-- True once the timed race is over and its result should be written.
function T.IsComplete()
  return S.timedRaceComplete == true
end

return T
