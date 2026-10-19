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

function M.Update(dt)
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