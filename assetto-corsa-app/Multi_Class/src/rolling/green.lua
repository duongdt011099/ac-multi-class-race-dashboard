-- Rolling start: green light for a single car.
local S = require("src/state")
local U = require("src/util")
local RCtl = require("src/rolling/control")
local Audio = require("src/audio")

local sFormat = string.format

local M = {}

function M.GiveRollingGreen(carIndex, state, now)
  if state.green then return end

  state.green = true
  state.greenAt = now
  -- Hand the AI back its racing controls now; the lane offset eases out over the next moments.
  if carIndex ~= 0 then
    RCtl.RestoreAIControl(carIndex, false)
  end
  U.Log(sFormat("ROLLING: car %d green at progress %.3f", carIndex, state.lastProgress or -1))
  if carIndex == 0 then
    Audio.FadeOut()
    S.rollingStartStatus = "GREEN â€” GO"
    ui.toast(ui.Icons.Play, "Rolling start: green light")
  end
end

return M
