-- Multiple Class Race: entry point. All logic lives in src/; this file only wires the
-- functions named in manifest.ini and the game callbacks to the modules.
local U = require("src/util")
local Frame = require("src/frame")
local Draw = require("src/leaderboard/draw")
local Config = require("src/config_window")
local RCtl = require("src/rolling/control")
local Exporter = require("src/export/exporter")

function script.update(dt)
  Frame.Update(dt)
end

function script.clLeaderboard(dt)
  Draw.Leaderboard(dt)
end

function script.clConfig()
  Config.Draw()
end

ac.onSessionStart(function()
  Frame.OnSessionStart()
end)

local function registerReleaseCallbacks()
  local okRelease, releaseErr = pcall(ac.onRelease, Exporter.ExportExpiredSessionOnRelease)
  if not okRelease then
    U.Log("EXPORT: failed to register release fallback: " .. tostring(releaseErr))
  end

  local okRolling, rollingErr = pcall(ac.onRelease, function()
    RCtl.RestoreRollingStartControls(false)
    pcall(require("src/yellow/yellow").Reset)
  end)
  if not okRolling then
    U.Log("ROLLING: failed to register cleanup callback: " .. tostring(rollingErr))
  end
end

if type(ac.onRelease) == "function" then
  registerReleaseCallbacks()
end

U.Log("=== APP LOADED ===")
