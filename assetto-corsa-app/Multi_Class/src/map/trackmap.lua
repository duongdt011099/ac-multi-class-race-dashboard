-- The map window: track outline and one dot per car (class color, class or overall position).
local Roster = require("src/map/roster")
local Track = require("src/map/track")
local S = require("src/state")

local M = {}


local ROAD_EDGE = rgbm(0.02, 0.02, 0.03, 1)
local ROAD = rgbm(1, 1, 1, 1)
local WHITE = rgbm(1, 1, 1, 1)
local BLACK = rgbm(0, 0, 0, 1)
local PIT_COLOR = rgbm(0.48, 0.50, 0.55, 0.6)

local clock = 0

local function isRace()
  local ok, t = pcall(function() return ac.getSession(ac.getSim().currentSessionIndex).type end)
  return ok and t == ac.SessionType.Race
end

-- Builds the car list with overall and per-class positions.
local function collectCars(race)
  local cars = {}
  local total = ac.getSim().carsCount
  -- The safety car is the extra car after the competitors: it is never drawn.
  if S.driverCount > 0 and S.driverCount < total then total = S.driverCount end
  for i = 0, total - 1 do
    local car = ac.getCar(i)
    if car and car.isConnected and i ~= S.safetyCarIndex then
      local best = car.bestLapTimeMs
      cars[#cars + 1] = {
        index = i, car = car, info = Roster.byIndex[i],
        key = race and (car.racePosition or 999) or ((best and best > 0) and best or 1e12 + i),
      }
    end
  end
  table.sort(cars, function(a, b) return a.key < b.key end)

  local perClass = {}
  for rank, c in ipairs(cars) do
    c.overall = rank
    local cls = c.info and c.info.class or "?"
    perClass[cls] = (perClass[cls] or 0) + 1
    c.classPos = perClass[cls]
  end
  return cars
end

function M.Draw(dt)
  clock = clock + (dt or 0)
  if not S.showMap then return end
  Roster.Update(clock)

  -- Guard: like the Multi_Class app, only active for sessions started from the dashboard
  -- (no usable session file = standalone launch from Steam = nothing is drawn).
  if not Roster.loaded then return end

  local size = ui.windowSize()

  local toolbarH = 0
  local pad = 28
  local topLeft = vec2(pad, pad)
  local view = Track.View(0, topLeft, vec2(size.x - pad * 2, size.y - pad * 2 - toolbarH))
  if not view then
    ui.dwriteDrawText("Track data unavailable", 18, vec2(20, 20), WHITE)
    return
  end

  local roadW = math.clamp(view.scale * 14, 6, 26)
  view.path(ROAD_EDGE, roadW + 6)
  view.path(ROAD, roadW)
  view.startLine(WHITE, 4, roadW * 0.75)

  local race = isRace()
  local cars = collectCars(race)

  local radius = math.clamp(math.min(size.x, size.y) / 50, 9, 18)
  local fontSize = radius * 1.15
  local mouse = ui.mouseLocalPos()
  local hovered = nil

  -- Back to front: last place first, so the leaders and the player stay on top.
  for _, c in ipairs(cars) do c.inPit = c.car.isInPitlane == true or c.car.isInPit == true end
  table.sort(cars, function(a, b)
    if a.inPit ~= b.inPit then return a.inPit end
    if (a.index == 0) ~= (b.index == 0) then return b.index == 0 end
    return a.overall > b.overall
  end)

  for _, c in ipairs(cars) do
    local p = view.project(c.car.position.x, c.car.position.z)
    local color = c.info and c.info.color or Roster.fallbackColor
    if c.index == 0 and S.humanCarColor then color = S.humanCarColor end
    local label = tostring(c.classPos)
    local r = radius

    if c.inPit then
      -- In the pits: small, greyed-out dot without a number.
      r = radius * 0.55
      if c.index == 0 then ui.drawCircleFilled(p, r + 3, rgbm(1, 1, 1, 0.45), 20) end
      ui.drawCircleFilled(p, r + 1, rgbm(0, 0, 0, 0.5), 20)
      ui.drawCircleFilled(p, r, PIT_COLOR, 20)
    else
      if c.index == 0 then
        ui.drawCircleFilled(p, r + 4, WHITE, 24)
      end
      ui.drawCircleFilled(p, r + 1.5, BLACK, 24)
      ui.drawCircleFilled(p, r, color, 24)

      local lum = color.r * 0.3 + color.g * 0.59 + color.b * 0.11
      local m = ui.measureDWriteText(label, fontSize)
      ui.dwriteDrawText(label, fontSize, p - m / 2, lum > 0.6 and BLACK or WHITE)
    end

    if mouse and ui.windowHovered() and (mouse - p):length() < r + 3 then hovered = c end
  end


  if hovered then
    local info = hovered.info
    ui.beginTooltip()
    ui.text(info and info.driver or ac.getDriverName(hovered.index))
    if info and info.team and info.team ~= "" then ui.text(info.team) end
    ui.text(string.format("%s  |  P%d overall%s", info and info.class or "No class", hovered.overall,
      info and string.format("  |  P%d in class", hovered.classPos) or ""))
    ui.endTooltip()
  end

end

return M
