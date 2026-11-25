-- Rolling-start music. The track is timed so it ends as the class leader crosses the line (green):
-- it starts once the estimated time to green drops to the track length, and is seeked into when
-- the player enters double file late (then it fades in instead of cutting in mid-song). Once playing
-- it is left alone (no rate or volume changes except those fades), because touching the media
-- player every frame makes it stutter. The music is a 3D source standing at the start/finish line
-- (C.ROLLING_MUSIC_3D), so it is heard faintly from afar and grows louder as the pack approaches.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")

local sFormat = string.format

local M = {}

local FADE_SECONDS = 2.0
local FINISH_NATURALLY_SECONDS = 3.0
local DEFAULT_DURATION = 48.0
local FILE_NAME = "sounds/rolling.mp3"

local player = nil
local is3D = false       -- the player is a positional source at the start line
local armed = false      -- the human car has entered double file
local started = false
local fadeLeft = nil
local fadeInLeft = nil   -- seconds of fade-in left when the track was started part-way through
local finishing = false  -- green arrived near the end: let the track finish on its own
local doubleFilePlayed = false   -- the callouts play once per attempt (reset in M.Reset)
local autopilotOffPlayed = false

local function filePath()
  return ac.getFolder(ac.FolderID.ACApps) .. "/lua/Multi_Class/" .. FILE_NAME
end

local function dispose()
  if player then
    pcall(function() player:pause() end)
    if is3D then pcall(function() player:dispose() end) end
  end
  player = nil
  is3D = false
  started = false
  fadeLeft = nil
  fadeInLeft = nil
  finishing = false
end

-- Immediate stop and re-arm for the next race.
function M.Reset()
  dispose()
  armed = false
  doubleFilePlayed = false
  autopilotOffPlayed = false
end

-- World position of the start/finish line speaker and the track direction there, or nil.
local function startLineSpeaker()
  if not ac.trackProgressToWorldCoordinate then return nil end
  local okA, here = pcall(ac.trackProgressToWorldCoordinate, 0.0)
  local okB, ahead = pcall(ac.trackProgressToWorldCoordinate, 0.002)
  if not (okA and here and U.IsFinite(here.x) and U.IsFinite(here.y) and U.IsFinite(here.z)) then return nil end
  local pos = vec3(here.x, here.y + C.ROLLING_MUSIC_HEIGHT_M, here.z)
  local dir = vec3(0, 0, 1)
  if okB and ahead then
    local d = vec3(ahead.x - here.x, 0, ahead.z - here.z)
    if d:length() > 0.01 then dir = d:normalize() end
  end
  return pos, dir
end

-- A ui.MediaPlayer cannot be positioned from an app ("FMOD integration is not available"), so the
-- 3D music is a file-based FMOD audio event standing at the start line, wrapped in the same small
-- interface the media player offers (play/pause/currentTime/duration/ended/...) so the timing logic
-- below works with either. FMOD events have no pause, so pause = stop and remember the position.
-- The event loops (a non-looping file event becomes invalid after one play); the wrapper stops it
-- itself when the position wraps back to the start, which is reported as ended().
local function newSpeakerPlayer(pos, dir)
  local ev = ac.AudioEvent.fromFile({
    filename = filePath(),
    use3D = true,
    loop = true,
    minDistance = C.ROLLING_MUSIC_MIN_DISTANCE_M,
    maxDistance = C.ROLLING_MUSIC_MAX_DISTANCE_M,
    dopplerEffect = 0,
  }, true)
  ev.cameraInteriorMultiplier = 1
  ev.cameraExteriorMultiplier = 1
  ev.cameraTrackMultiplier = 1
  ev:setPosition(pos, dir, vec3(0, 1, 0), vec3())

  local w = { playingNow = false, endedNow = false, heldAt = 0.0, lastPos = 0.0 }

  local function position()
    local t = ev:getTimelinePosition()
    if type(t) ~= "number" or t < 0 then return w.lastPos end
    return t
  end

  -- Detect the loop wrapping to the start: that is the end of the song.
  local function poll()
    if not w.playingNow then return end
    local t = position()
    local d = ev:getDuration()
    if t < w.lastPos - 1.0 or (type(d) == "number" and d > 1.0 and t >= d - 0.05) then
      ev:stop()
      w.playingNow = false
      w.endedNow = true
      w.heldAt = 0.0
      w.lastPos = 0.0
      return
    end
    w.lastPos = t
  end

  function w:setAutoPlay() return self end
  function w:setLooping() return self end
  function w:setVolume(v) ev.volume = v return self end
  function w:hasAudio() return ev:isValid() end
  function w:duration() local d = ev:getDuration() return (type(d) == "number" and d > 0) and d or -1 end
  function w:currentTime() poll() return self.playingNow and position() or self.heldAt end
  function w:playing() poll() return self.playingNow end
  function w:ended() poll() return self.endedNow end
  function w:setCurrentTime(t)
    self.heldAt = t
    self.lastPos = t
    if self.playingNow then ev:seek(t) end
    return self
  end
  function w:play()
    if self.playingNow then return self end
    ev:start()
    if self.heldAt > 0.05 then ev:seek(self.heldAt) end
    self.lastPos = self.heldAt
    self.playingNow = true
    self.endedNow = false
    return self
  end
  function w:pause()
    if not self.playingNow then return self end
    self.heldAt = position()
    ev:stop()
    self.playingNow = false
    return self
  end
  function w:dispose() pcall(function() ev:dispose() end) end
  return w
end

-- Load the track paused at the start of the formation so it is ready when needed.
function M.Prepare()
  if not S.rollingMusicEnabled or player then return end
  local ok, err = pcall(function()
    -- Fall back to 2D music when the start line position is unknown (no AI spline) or the FMOD
    -- event cannot be created.
    local pos, dir
    if C.ROLLING_MUSIC_3D then pos, dir = startLineSpeaker() end
    if pos then
      local ok3D, p = pcall(newSpeakerPlayer, pos, dir)
      if ok3D and p then
        player = p
        is3D = true
        U.Log(sFormat("AUDIO: rolling music is a 3D source at the start line (%.0f, %.0f, %.0f)",
            pos.x, pos.y, pos.z))
      else
        U.Log("AUDIO: 3D rolling music unavailable, using 2D: " .. tostring(p))
      end
    end
    if not player then
      player = ui.MediaPlayer(filePath(), { use3D = false })
      is3D = false
    end
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
  if doubleFilePlayed then return end
  doubleFilePlayed = true
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

-- One-shot warning played shortly before the autopilot hands the car back to the player.
local autopilotOffPlayer = nil

function M.PlayAutopilotOff()
  if S.developerMode or autopilotOffPlayed then return end
  autopilotOffPlayed = true
  local ok, err = pcall(function()
    if not autopilotOffPlayer then
      autopilotOffPlayer = ui.MediaPlayer(ac.getFolder(ac.FolderID.ACApps) .. "/lua/Multi_Class/sounds/auto_pilot_off.mp3",
          { use3D = false })
      autopilotOffPlayer:setAutoPlay(false)
      autopilotOffPlayer:setLooping(false)
    end
    autopilotOffPlayer:setVolume(C.AUDIO_VOLUME)
    autopilotOffPlayer:setCurrentTime(0)
    autopilotOffPlayer:play()
  end)
  if not ok then U.Log("AUDIO: could not play auto_pilot_off: " .. tostring(err)) end
end

-- True while the "autopilot off" warning clip is audibly playing.
function M.IsAutopilotOffPlaying()
  if not autopilotOffPlayer then return false end
  local ok, playing = pcall(function() return autopilotOffPlayer:playing() end)
  return ok and playing == true
end

-- Called when the player's class goes green.
function M.FadeOut()
  if not (player and started) or fadeLeft or finishing then return end
  local ok, remaining = pcall(function() return player:duration() - player:currentTime() end)
  if ok and type(remaining) == "number" and remaining < FINISH_NATURALLY_SECONDS and not fadeInLeft then
    finishing = true
  else
    -- Still fading in: fade out from the current level, not from full volume.
    local level = 1.0
    if fadeInLeft then level = 1.0 - fadeInLeft / C.ROLLING_MUSIC_FADE_IN_SECONDS end
    fadeInLeft = nil
    fadeLeft = FADE_SECONDS * math.max(0.05, level)
  end
end

-- Estimated metres until the player's class leader crosses the line, or nil when unknown.
local function EstimateTimeToGreen(dt)
  -- The music follows the first class: when its leader crosses the line every class goes green.
  local group = S.rollingStartGroups and S.rollingStartGroups[1]
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

  -- Distance left to the line, in metres. Compared against S = v * t (see M.Update).
  return (1.0 - p) * trackLength
end

local function StartPlayback(duration, position)
  -- Starting part-way through the song (a short double file) sounds like a cut-in, so fade it in.
  local fadeIn = position > C.ROLLING_MUSIC_FADE_IN_MIN_POSITION and C.ROLLING_MUSIC_FADE_IN_SECONDS > 0
  fadeInLeft = fadeIn and C.ROLLING_MUSIC_FADE_IN_SECONDS or nil
  player:setVolume(fadeIn and 0.0 or S.rollingMusicVolume)
  if position > 0.2 then player:setCurrentTime(position) end
  player:play()
  started = true
  U.Log(sFormat("AUDIO: rolling music started at %.1fs of %.1fs%s", position, duration,
      fadeIn and sFormat(" (fading in over %.1fs)", C.ROLLING_MUSIC_FADE_IN_SECONDS) or ""))
end

-- Yellow / green flag callouts: short one-shot clips. A new yellow replaces a playing green; a green
-- never cuts a yellow off, it waits until the yellow clip has finished.
local FLAG_CLIP_SECONDS = 8.8
local FLAG_END_TAIL_SECONDS = 0.6
local flagPlayers = {}
local pendingFlags = { yellow_flag = false, green_flag = false }
local flagClock = 0.0
local yellowStartedAt = nil
local greenStartedAt = nil
local pendingSince = {}

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
  -- Requested but the player is not ready yet: the clip has not played, so it is not done.
  if not startedAt then
    if pendingFlags[name] then return false end
    return nil
  end
  local p = flagPlayers[name]
  if not p then return nil end
  local elapsed = flagClock - startedAt
  -- Right after play() the player can still report a stale "ended" / time; never trust it early.
  if elapsed < 2.0 then return false end
  local okDur, duration = pcall(function() return p:duration() end)
  local hasDuration = okDur and type(duration) == "number" and duration > 1.0
  -- The clip is only done once the whole of it has been heard. The player position runs a little
  -- ahead of what is audible (output buffering), so never trust "almost at the end" or a stale
  -- ended() flag before the real duration plus a small tail has passed on the wall clock.
  local minElapsed = (hasDuration and duration or FLAG_CLIP_SECONDS) + FLAG_END_TAIL_SECONDS
  if elapsed < minElapsed then return false end
  return true
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
  pendingSince = {}
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
    pendingSince.yellow_flag = pendingSince.yellow_flag or flagClock
    if tryStartFlag("yellow_flag") then
      yellowStartedAt = flagClock
      pendingSince.yellow_flag = nil
    elseif flagClock - pendingSince.yellow_flag > 4.0 then
      -- The clip never became ready: give up so the state machine falls back to its timer.
      pendingFlags.yellow_flag = false
      pendingSince.yellow_flag = nil
      U.Log("AUDIO: yellow_flag never became ready; giving up")
    end
    return
  end
  if pendingFlags.green_flag then
    if yellowStartedAt and flagClock < yellowStartedAt + FLAG_CLIP_SECONDS then return end
    if tryStartFlag("green_flag") then greenStartedAt = flagClock end
  end
end

-- Game pause: every clip that is playing is held and carried on from the same spot on resume, so
-- the callouts neither keep sounding behind the pause menu nor lose their place.
local suspended = {}

local function allPlayers()
  local list = { player, doubleFilePlayer, autopilotOffPlayer }
  for _, p in pairs(flagPlayers) do list[#list + 1] = p end
  return list
end

function M.Suspend()
  if next(suspended) ~= nil then return end
  for _, p in ipairs(allPlayers()) do
    local ok, playing = pcall(function() return p:playing() end)
    -- A clip that has already finished can still report "playing"; resuming it would play it again.
    local okEnd, ended = pcall(function() return p:ended() end)
    if ok and playing == true and not (okEnd and ended == true) then
      pcall(function() p:pause() end)
      suspended[#suspended + 1] = p
    end
  end
end

function M.Resume()
  if #suspended == 0 then return end
  for _, p in ipairs(suspended) do pcall(function() p:play() end) end
  suspended = {}
end

-- Full reset for a restarted or ended attempt: every clip stops, including the one-shot callouts,
-- and the paused-clip bookkeeping is dropped. Without this the "autopilot off" clip (which drives the
-- "Hold your steering wheel" banner) could still be playing into the next attempt.
function M.ResetAll()
  M.Reset()
  M.ResetFlags()
  flagClock = 0.0
  flagLastTime = {}
  suspended = {}
  for _, p in ipairs({ doubleFilePlayer, autopilotOffPlayer }) do
    pcall(function()
      p:pause()
      p:setCurrentTime(0)
    end)
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

    if fadeInLeft then
      fadeInLeft = fadeInLeft - dt
      if fadeInLeft <= 0 then
        fadeInLeft = nil
        player:setVolume(S.rollingMusicVolume)
      else
        player:setVolume(S.rollingMusicVolume * (1.0 - fadeInLeft / C.ROLLING_MUSIC_FADE_IN_SECONDS))
      end
    end

    if not armed then return end

    local duration = player:duration()
    if type(duration) ~= "number" or duration <= 1.0 then
      if started then return end
      duration = DEFAULT_DURATION
      if not player:hasAudio() then return end  -- still loading
    end
    local marker = math.max(1.0, duration - C.ROLLING_MUSIC_END_LEAD_SECONDS)

    local distance = EstimateTimeToGreen(dt)
    if not distance then return end

    if not started then
      -- S = v * t: the distance the pack covers at the double-file speed while the track plays.
      local speed = C.ROLLING_START_SPEED_KMH / 3.6
      if distance <= speed * marker then StartPlayback(duration, marker - distance / speed) end
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