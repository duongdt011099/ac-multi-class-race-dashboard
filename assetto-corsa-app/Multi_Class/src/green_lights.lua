-- "Five green lights" banner shown at the top of the screen whenever the player is given green
-- (rolling start green, or the green after a yellow flag).
local Draw = require("src/leaderboard/draw")

local M = {}

local IMAGE = "green_lights.png"   -- relative to the app folder
local IMAGE_ASPECT = 120 / 500     -- height / width of the PNG
local FADE_SECONDS = 0.4

local alpha = 0.0

function M.Draw(dt)
  local sim = ac.getSim()
  local text = Draw.GetRollingStartDisplay(sim)
  local showing = type(text) == "string" and text:sub(1, 5) == "GREEN"

  local step = (dt or 0.016) / FADE_SECONDS
  if showing then alpha = math.min(1.0, alpha + step) else alpha = math.max(0.0, alpha - step) end
  if alpha <= 0.0 then return end

  local size = ui.windowSize()
  local width = size.x
  local height = width * IMAGE_ASPECT
  ui.drawImage(IMAGE, vec2(0, 0), vec2(width, height), rgbm(1, 1, 1, alpha))
end

return M
