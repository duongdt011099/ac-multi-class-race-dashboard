-- Developer-mode live log window (cl_devlog): the tail of everything U.Log writes. Draws nothing
-- unless developer mode is on in config.json.
local S = require("src/state")

local M = {}

local COLOR = rgbm(0.82, 0.9, 0.85, 1)
local WARN = rgbm(1.0, 0.75, 0.35, 1)

function M.Draw(dt)
  if not S.developerMode then return end
  local buffer = S.logBuffer
  if not buffer or #buffer == 0 then
    ui.text("Developer mode: waiting for log output...")
    return
  end
  local size = ui.windowSize()
  local lineH = math.max(8, ui.measureText("A").y)
  local charW = math.max(4, ui.measureText("M").x * 0.8)
  local maxLines = math.max(1, math.floor((size.y - 16) / lineH))
  local maxChars = math.max(10, math.floor((size.x - 16) / charW))

  local first = math.max(1, #buffer - maxLines + 1)
  for i = first, #buffer do
    local line = buffer[i]
    if #line > maxChars then line = line:sub(1, maxChars - 1) .. "…" end
    local lower = line:lower()
    local warn = lower:find("fail", 1, true) or lower:find("error", 1, true)
    ui.textColored(line, warn and WARN or COLOR)
  end
end

return M
