-- Live session snapshot: keeps a recent copy of identity/result data for the export.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")

local Snap = {}

local getCar = ac.getCar

function Snap.GetCarIdentity(i)
  local identity = { driver = "", car = "", skin = "" }

  local okD, d = pcall(function() return ac.getDriverName and ac.getDriverName(i) end)
  if okD and type(d) == "string" then identity.driver = d end

  local okC, c = pcall(function() return ac.getCarID and ac.getCarID(i) end)
  if okC and type(c) == "string" then identity.car = c end

  local okS, s = pcall(function() return ac.getCarSkinID and ac.getCarSkinID(i) end)
  if okS and type(s) == "string" then identity.skin = s end

  return identity
end

function Snap.GetCarLapData(i)
  local data = {
    available = false,
    lapCount = 0,
    bestMs = 0,
    position = 0,
    inPit = nil,
    inPitlane = nil,
  }
  local okCar, car = pcall(getCar, i)
  if not okCar or not car then return data end
  data.available = true

  local okL, lc = pcall(function() return car.lapCount end)
  if okL and type(lc) == "number" and lc > 0 then data.lapCount = lc end

  local okB, bt = pcall(function() return car.bestLapTimeMs end)
  if okB and type(bt) == "number" and bt > 0 then data.bestMs = bt end

  local okP, p = pcall(function() return car.racePosition end)
  if okP and type(p) == "number" and p > 0 then data.position = p end

  local okPit, inPit = pcall(function() return car.isInPit end)
  if okPit and type(inPit) == "boolean" then data.inPit = inPit end

  local okPitlane, inPitlane = pcall(function() return car.isInPitlane end)
  if okPitlane and type(inPitlane) == "boolean" then data.inPitlane = inPitlane end

  return data
end

-- Records the first usable pre-finish position for car i, or returns nil if none is known.
local function freezePosition(i, position, raceFinished)
  local previous = S.liveSessionCars[i + 1]
  local frozenPosition
  if previous and previous.position > 0 and (raceFinished or previous.lapCount < S.liveTotalLaps) then
    frozenPosition = previous.position
  else
    frozenPosition = position
  end

  if frozenPosition and frozenPosition > 0 then
    S.finishedRacePositions[i] = frozenPosition
    return frozenPosition
  end
  return nil
end

-- Assetto Corsa can rewrite the live racePosition after a car crosses the finish line
-- (for example, promoting the player to P1 while the remaining field is still on track).
-- Preserve the last pre-finish overall position for result export and the OVERALL panel. If AC
-- ends the session before every car reaches the configured lap count, freeze the last observed
-- classification at that transition as well.
local function resolvePosition(i, lapData, raceFinished)
  local position = lapData.position
  local finalLapReached = S.liveSessionType == C.SUBFOLDER_RACE
      and S.liveTotalLaps > 0
      and lapData.lapCount >= S.liveTotalLaps

  if finalLapReached or raceFinished then
    local frozenPosition = S.finishedRacePositions[i]
    if not frozenPosition then
      frozenPosition = freezePosition(i, position, raceFinished)
    end
    if frozenPosition then
      position = frozenPosition
    end
  elseif S.finishedRacePositions[i] then
    position = S.finishedRacePositions[i]
  end
  return position
end

local function buildCarEntry(i, raceFinished)
  local identity = Snap.GetCarIdentity(i)
  local lapData = Snap.GetCarLapData(i)
  local entry = {
    index = i,
    driver = identity.driver,
    car = identity.car,
    skin = identity.skin,
    lapCount = lapData.lapCount,
    bestMs = lapData.bestMs,
    position = resolvePosition(i, lapData, raceFinished),
    inPit = lapData.inPit,
    inPitlane = lapData.inPitlane,
  }
  local available = lapData.available or identity.driver ~= "" or identity.car ~= ""
  return entry, available
end

-- Keep a recent copy of every car's identity and result data while AC still exposes the
-- grid. `sim.carsCount` can become zero as soon as a timed session ends, so export must
-- never depend on live car objects after that transition.
function Snap.CaptureLiveCarResults(forceFreeze)
  if S.driverCount <= 0 then return end

  local raceFinished = S.liveSessionType == C.SUBFOLDER_RACE
      and (forceFreeze == true or Sess.IsSessionFinished())
  local cars = {}
  local availableCars = 0
  for i = 0, S.driverCount - 1 do
    local entry, available = buildCarEntry(i, raceFinished)
    cars[#cars + 1] = entry
    if available then availableCars = availableCars + 1 end
  end

  -- Keep the last good copy if AC briefly reports a grid before populating its car data.
  if availableCars > 0 then
    S.liveSessionCars = cars
    S.liveSessionCarCount = #cars
  end
end

local function readSessionLaps(sim)
  local laps = 0
  if ac.getSession then
    local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
    if okSession and session then
      local okLaps, sessionLaps = pcall(function() return session.laps end)
      if okLaps and type(sessionLaps) == "number" and sessionLaps > 0 then laps = sessionLaps end
    end
  end
  return laps
end

local function captureTimeLeft(sim)
  local okTimeLeft, timeLeft = pcall(function() return sim.sessionTimeLeft end)
  if okTimeLeft and type(timeLeft) == "number" then
    S.liveSessionTimeLeft = timeLeft
    if timeLeft > 0 then S.liveSessionHadCountdown = true end
  end
end

-- Snapshots everything the result needs while the session is still alive. Called every
-- frame while sim.isSessionStarted is true, so the last successful call before the
-- session ends holds metadata and car results even after AC reports zero cars.
function Snap.CaptureLiveSession(sim)
  sim = sim or ac.getSim()
  if sim.isSessionStarted ~= true then return end

  local folder = Sess.SessionTypeToFolder(Sess.ReadLiveSessionType(sim))
  if folder == nil then return end

  local laps = readSessionLaps(sim)

  local sessionKey = Sess.GetCurrentSessionKey(sim)
  if S.liveSessionKey ~= sessionKey then
    S.liveRollingStartLapOffset = folder == C.SUBFOLDER_RACE and Sess.ReadRollingStartLapOffset() or 0
  end

  S.liveSessionType    = folder
  S.liveSessionKey     = sessionKey
  S.liveTrackName      = Sess.GetTrackNameSafe()
  S.liveTotalLaps      = laps
  captureTimeLeft(sim)
  Snap.CaptureLiveCarResults()
end

-- Called when a session ends, to arm the export. The result is written from the snapshot
-- on the next frame, outside the session-validity check and without requiring live cars.
function Snap.ArmSessionEnded()
  if S.liveSessionType == nil then return end
  if S.exportedSessionKey ~= nil and S.exportedSessionKey == S.liveSessionKey then return end
  local wasPending = S.liveSessionPending
  S.liveSessionPending = true
  if not wasPending then
    U.Log("EXPORT: session ended, pending export for " .. tostring(S.liveSessionType) ..
        " key=" .. tostring(S.liveSessionKey) .. " cars=" .. tostring(S.liveSessionCarCount))
  end
end

-- Called when a new session starts, so a previous session's snapshot cannot leak into it.
function Snap.ClearLiveSession()
  S.liveSessionType     = nil
  S.liveSessionKey      = nil
  S.liveTrackName       = ""
  S.liveTotalLaps       = 0
  S.liveSessionPending  = false
  S.liveSessionCars     = {}
  S.liveSessionCarCount = 0
  S.liveSessionTimeLeft = nil
  S.liveSessionHadCountdown = false
  S.finishedRacePositions = {}
  S.liveRollingStartLapOffset = 0
end

local function snapshotPitState()
  local playerSnapshot = S.liveSessionCars[1]
  if playerSnapshot then
    if playerSnapshot.inPit == true or playerSnapshot.inPitlane == true then return true end
    if playerSnapshot.inPit == false and playerSnapshot.inPitlane == false then return false end
  end
  return nil
end

function Snap.IsPlayerInPit()
  local okCar, car = pcall(getCar, 0)
  if okCar and car then
    local observed = false
    local okPit, inPit = pcall(function() return car.isInPit end)
    if okPit and type(inPit) == "boolean" then
      observed = true
      if inPit then return true end
    end
    local okPitlane, inPitlane = pcall(function() return car.isInPitlane end)
    if okPitlane and type(inPitlane) == "boolean" then
      observed = true
      if inPitlane then return true end
    end
    if observed then return false end
  end

  -- AC can remove car objects at session teardown. Use the last live observation in that
  -- case; the live session snapshot is refreshed every frame until the grid disappears.
  return snapshotPitState()
end

return Snap
