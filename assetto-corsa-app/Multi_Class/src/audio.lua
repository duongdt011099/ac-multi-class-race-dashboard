-- Rolling-start music. The track is timed so it ends as the class leader crosses the line (green):
-- it starts once the estimated time to green drops to the track length, and is seeked into when
-- the player enters double file late. Once playing it is left alone (no rate or volume changes
-- except the fade at green), because touching the media player every frame makes it stutter.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")

local sFormat = string.format

local M = {}

local FADE_SECONDS = 2.0
local FINISH_NATURALLY_SECONDS = 3.0
local DEFAULT_DURATION = 53.0
local FILE_NAME = "sounds/rolling.mp3"

local player = nil
local armed = false      -- the human car has entered double file
local started = false
local fadeLeft = nil
local finishing = false  -- green arrived near the end: let the track finish on its own
local smoothSpeed = nil

local function filePath()
  return ac.getFolder(ac.FolderID.ACApps) .. "/lua/Multi_Class/" .. FILE_NAME
end

local function dispose()
  if player then pcall(function() player:pause() end) end
  player = nil
  started = false
  fadeLeft = nil
  finishing = false
end

-- Immediate stop and re-arm for the next race.
function M.Reset()
  dispose()
  armed = false
  smoothSpeed = nil
end

-- Load the track paused at the start of the formation so it is ready when needed.
function M.Prepare()
  if not S.rollingMusicEnabled or player then return end
  local ok, err = pcall(function()
    player = ui.MediaPlayer(filePath(), { use3D = false })
    player:setAutoPlay(false)
    player:setLooping(false)
    player:setVolume(S.rollingMusicVolume)
  end)
  if not ok then
    player = nil
    U.Log("AUDIO: could not load rolling music: " .. tostring(err))
  end
end

function M.PlayRolling()
  if not S.rollingMusicEnabled then return end
  if not player then M.Prepare() end
  armed = true
end

-- One-shot callout played when the player enters double file.
local doubleFilePlayer = nil

function M.PlayDoubleFile()
  if not S.rollingMusicEnabled then return end
  local ok, err = pcall(function()
    if not doubleFilePlayer then
      doubleFilePlayer = ui.MediaPlayer(ac.getFolder(ac.FolderID.ACApps) .. "/lua/Multi_Class/sounds/double_file.mp3",
          { use3D = false })
      doubleFilePlayer:setAutoPlay(false)
      doubleFilePlayer:setLooping(false)
    end
    doubleFilePlayer:setVolume(C.AUDIO_VOLUME)
    doubleFilePlayer:setCurrentTime(0)
    doubleFilePlayer:play()
  end)
  if not ok then U.Log("AUDIO: could not play double_file: " .. tostring(err)) end
end

-- Called when the player's class goes green.
function M.FadeOut()
  if not (player and started) or fadeLeft or finishing then return end
  local ok, remaining = pcall(function() return player:duration() - player:currentTime() end)
  if ok and type(remaining) == "number" and remaining < FINISH_NATURALLY_SECONDS then
    finishing = true
  else
    fadeLeft = FADE_SECONDS
  end
end

-- Estimated seconds until the player's class leader crosses the line, or nil when unknown.
local function EstimateTimeToGreen(dt)
  local playerState = S.rollingStartCars and S.rollingStartCars[0]
  local group = playerState and S.rollingStartGroups and S.rollingStartGroups[playerState.groupIndex]
  if not group or group.green then return nil end

  local leaderIndex
  for _, carIndex in ipairs(group.cars) do
    local st = S.rollingStartCars[carIndex]
    if st and not st.dropped then leaderIndex = carIndex break end
  end
  local leader = leaderIndex and ac.getCar(leaderIndex)
  local sim = ac.getSim()
  local trackLength = sim and sim.trackLengthM
  if not (leader and U.IsFinite(trackLength)) then return nil end

  local p = U.NormalizeTrackProgress(leader.splinePosition or 0)
  if p < 0.3 then return nil end

  local speed = math.max((leader.speedKmh or 0) / 3.6, 1.0)
  smoothSpeed = smoothSpeed and (smoothSpeed + (speed - smoothSpeed) * math.min(1.0, dt * 2.0)) or speed
  return (1.0 - p) * trackLength / math.max(smoothSpeed, 8.0)
end

local function StartPlayback(duration, position)
  player:setVolume(S.rollingMusicVolume)
  if position > 0.2 then player:setCurrentTime(position) end
  player:play()
  started = true
  U.Log(sFormat("AUDIO: rolling music started at %.1fs of %.1fs", position, duration))
end

-- Yellow / green flag callouts: short one-shot clips. A new yellow replaces a playing green; a green
-- never cuts a yellow off, it waits until the yellow clip has finished.
local FLAG_CLIP_SECONDS = 8.8
local flagPlayers = {}
local pendingFlags = { yellow_flag = false, green_flag = false }
local flagClock = 0.0
local yellowStartedAt = nil
local greenStartedAt = nil

local function flagPath(name)
  return ac.getFolder(ac.FolderID.ACApps) .. "/lua/Multi_Class/sounds/" .. name .. ".mp3"
end

local function stopFlag(name)
  local p = flagPlayers[name]
  if p then pcall(function() p:pause() end) end
end

local flagLastTime = {}

local function flagTime(p)
  local ok, t = pcall(function() return p:currentTime() end)
  return ok and type(t) == "number" and t or nil
end

local function startFlag(name)
  local p = flagPlayers[name]
  if not p then return end
  p:setVolume(S.flagSoundVolume)
  -- A fresh player already sits at 0; seeking it again can make some builds restart the clip.
  local before = flagTime(p)
  if before and before > 0.1 then p:setCurrentTime(0) end
  p:play()
  flagLastTime[name] = 0
  local okDur, duration = pcall(function() return p:duration() end)
  U.Log(sFormat("AUDIO: playing %s at flag clock %.1f (was at %.2fs, duration %s)", name, flagClock,
      before or -1, okDur and tostring(duration) or "n/a"))
end

-- Log if a playing clip jumps back (a restart / second play).
local function watchFlags()
  for name, last in pairs(flagLastTime) do
    local p = flagPlayers[name]
    local t = p and flagTime(p)
    if t then
      if t < last - 0.5 then
        U.Log(sFormat("AUDIO: %s restarted (%.2fs -> %.2fs)", name, last, t))
      end
      flagLastTime[name] = t
      if t >= FLAG_CLIP_SECONDS then flagLastTime[name] = nil end
    end
  end
end

local function requestFlag(name)
  if not S.flagSoundEnabled then return end
  if name == "yellow_flag" and (pendingFlags.yellow_flag
      or (yellowStartedAt and flagClock - yellowStartedAt < FLAG_CLIP_SECONDS)) then
    U.Log("AUDIO: ignored duplicate yellow_flag request")
    return
  end
  U.Log("AUDIO: requested " .. name)
  local ok, err = pcall(function()
    if not flagPlayers[name] then
      local p = ui.MediaPlayer(flagPath(name), { use3D = false })
      p:setAutoPlay(false)
      p:setLooping(false)
      flagPlayers[name] = p
    end
  end)
  if not ok then
    U.Log("AUDIO: could not load " .. name .. ": " .. tostring(err))
    return
  end
  if name == "yellow_flag" then
    stopFlag("green_flag")
    pendingFlags.green_flag = false
    yellowStartedAt = nil
    greenStartedAt = nil
  else
    greenStartedAt = nil
  end
  pendingFlags[name] = true
end

function M.PlayYellowFlag() requestFlag("yellow_flag") end
function M.PlayGreenFlag() requestFlag("green_flag") end

-- Seconds since the yellow clip actually started playing, or nil while it has not.
function M.YellowElapsed()
  return yellowStartedAt and (flagClock - yellowStartedAt) or nil
end

-- true once the clip has really finished, false while it is still playing, nil when it has not
-- started (pending, disabled or never requested).
local function flagDone(name, startedAt)
  if not startedAt then return nil end
  local p = flagPlayers[name]
  if not p then return nil end
  local okEnded, ended = pcall(function() return p:ended() end)
  if okEnded and ended == true then return true end
  local t = flagTime(p)
  local okDur, duration = pcall(function() return p:duration() end)
  if t and okDur and type(duration) == "number" and duration > 1.0 then
    return t >= duration - 0.15
  end
  return (flagClock - startedAt) >= FLAG_CLIP_SECONDS
end

function M.YellowDone() return flagDone("yellow_flag", yellowStartedAt) end
function M.GreenDone() return flagDone("green_flag", greenStartedAt) end

-- Silence a green countdown that is no longer true (a new hazard appeared).
function M.StopGreenFlag()
  stopFlag("green_flag")
  pendingFlags.green_flag = false
  greenStartedAt = nil
end

-- Load both clips ahead of time so the countdown starts the moment it is requested.
function M.PreloadFlags()
  if not S.flagSoundEnabled then return end
  pcall(function()
    for _, name in ipairs({ "yellow_flag", "green_flag" }) do
      if not flagPlayers[name] then
        local p = ui.MediaPlayer(flagPath(name), { use3D = false })
        p:setAutoPlay(false)
        p:setLooping(false)
        flagPlayers[name] = p
      end
    end
  end)
end

function M.ResetFlags()
  stopFlag("yellow_flag")
  stopFlag("green_flag")
  pendingFlags.yellow_flag = false
  pendingFlags.green_flag = false
  yellowStartedAt = nil
  greenStartedAt = nil
end

local function tryStartFlag(name)
  local p = flagPlayers[name]
  if not p then pendingFlags[name] = false return false end
  local ok, started = pcall(function()
    if not p:hasAudio() then return false end
    startFlag(name)
    return true
  end)
  if not ok then
    U.Log("AUDIO: flag playback failed: " .. tostring(started))
    pendingFlags[name] = false
    return false
  end
  if started then pendingFlags[name] = false end
  return started
end

local function updateFlagSounds(dt)
  flagClock = flagClock + (dt or 0)
  pcall(watchFlags)
  if pendingFlags.yellow_flag then
    if tryStartFlag("yellow_flag") then yellowStartedAt = flagClock end
    return
  end
  if pendingFlags.green_flag then
    if yellowStartedAt and flagClock < yellowStartedAt + FLAG_CLIP_SECONDS then return end
    if tryStartFlag("green_flag") then greenStartedAt = flagClock end
  end
end

function M.Update(dt)
  updateFlagSounds(dt)
  if not player then return end
  local ok, err = pcall(function()
    if fadeLeft then
      fadeLeft = fadeLeft - dt
      if fadeLeft <= 0 then dispose() return end
      player:setVolume(S.rollingMusicVolume * (fadeLeft / FADE_SECONDS))
      return
    end

    if finishing then
      if player:ended() then dispose() end
      return
    end

    if not armed then return end

    local duration = player:duration()
    if type(duration) ~= "number" or duration <= 1.0 then
      if started then return end
      duration = DEFAULT_DURATION
      if not player:hasAudio() then return end  -- still loading
    end
    local marker = math.max(1.0, duration - C.ROLLING_MUSIC_END_LEAD_SECONDS)

    local timeToGreen = EstimateTimeToGreen(dt)
    if not timeToGreen then return end

    if not started then
      if timeToGreen <= marker then StartPlayback(duration, marker - timeToGreen) end
      return
    end

    if player:ended() then dispose() end
  end)
  if not ok then
    U.Log("AUDIO: update failed: " .. tostring(err))
    dispose()
  end
end

return M