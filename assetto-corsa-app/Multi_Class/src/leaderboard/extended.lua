-- Extended leaderboard: the player's class only, at most 6 rows. The class leader is always on top
-- and the rows below it are the ones around the human car. Each row shows
-- position - driver name - time gap - fuel level blocks (fuel percentage, coloured by how much is left).
local S = require("src/state")
local U = require("src/util")
local Positions = require("src/leaderboard/positions")
local Roster = require("src/map/roster")

local Extended = {}

local MAX_ROWS = 6
local ROW_H = 32
local FONT = "Bahnschrift;Weight=SemiBold"
local sFormat = string.format

local ROW_FILL = rgbm(0.09, 0.10, 0.12, 0.92)
local PLAYER_FILL = rgbm(0.10, 0.36, 0.78, 0.70)
local WHITE = rgbm(0.95, 0.96, 1, 1)
local DIM = rgbm(0.58, 0.62, 0.70, 1)
local ACCENT = rgbm(0.40, 0.68, 1.0, 1)
local DARK = rgbm(0.05, 0.05, 0.07, 1)
local HEADER_FILL = rgbm(1, 1, 1, 0.05)
local STRIPE_FILL = rgbm(1, 1, 1, 0.035)
local CLOCK_ACCENT = rgbm(0.95, 0.40, 0.40, 1)
local SEPARATOR = rgbm(1, 1, 1, 0.08)
local BLOCK_EMPTY = rgbm(1, 1, 1, 0.16)

local FUEL_GREEN = rgbm(0.25, 0.85, 0.40, 1)
local FUEL_YELLOW = rgbm(1.00, 0.80, 0.20, 1)
local FUEL_RED = rgbm(0.95, 0.27, 0.25, 1)

-- 70% and above is green, 30-70% yellow, below 30% red.
local function fuelColor(pct)
  if pct >= 70 then return FUEL_GREEN end
  if pct >= 30 then return FUEL_YELLOW end
  return FUEL_RED
end

-- All cars of the player's class (every car when the class is unknown), best first. The safety car is
-- not part of S.driverCount, so it never shows up.
local function collect(qualiMode)
  local playerClass = S.displayClass[0]
  local list = {}
  for i = 0, S.driverCount - 1 do
    local car = ac.getCar(i)
    if car and car.isConnected ~= false and (not playerClass or S.displayClass[i] == playerClass) then
      local name = ac.getDriverName(i)
      if name and name ~= "" then
        local key
        if qualiMode then
          local best = car.bestLapTimeMs
          key = (type(best) == "number" and best > 0) and best or (1e12 + i)
        else
          -- Once the race is finished the engine rewrites racePosition: keep the frozen one.
          local p = S.finishedRacePositions[i] or car.racePosition
          key = (type(p) == "number" and p > 0) and p or (1e6 + i)
        end
        list[#list + 1] = { idx = i, car = car, name = name, key = key }
      end
    end
  end
  table.sort(list, function(a, b) return a.key < b.key end)
  for rank, c in ipairs(list) do c.rank = rank end
  return list
end

-- Leader first, then the rows around the human car, at most maxRows in total (it sits in the middle of the
-- other rows when it can).
local function pickRows(list, maxRows)
  local n = #list
  if n <= maxRows then return list end
  local r = 1
  for i, c in ipairs(list) do
    if c.idx == 0 then r = i break end
  end
  local out = {}
  if r == 1 then
    for i = 1, maxRows do out[i] = list[i] end
    return out
  end
  out[1] = list[1]
  local s = math.max(2, math.min(r - math.floor((maxRows - 1) / 2), n - maxRows + 2))
  for i = s, s + maxRows - 2 do out[#out + 1] = list[i] end
  return out
end

local function gapText(h, sim, qualiMode, c, playerRank, playerBest, leaderKey)
  if qualiMode then
    if c.key >= 1e12 then return "--" end
    if c.rank == 1 then return U.FormatLapMs(c.key) end
    local d = (c.key - leaderKey) / 1000
    if d >= 60 then return "+" .. U.FormatLapMs(c.key - leaderKey) end
    return sFormat("+%.3f", d)
  end
  if c.idx == 0 then return "YOU" end
  if not sim.isSessionStarted then return "--" end
  -- No gap during the formation lap / double file: only after every class has gone green.
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then return "--" end
  -- Gaps are only shown to cars that are racing on track. A car that has already taken the flag keeps
  -- its gap; otherwise a car parked in the pits for good is retired ("RET") and one making a stop is "PIT".
  if not S.finishedRacePositions[c.idx] then
    if Positions.IsRetiredInPit(c.idx) then return "RET" end
    if c.car.isInPitlane == true or c.car.isInPit == true then return "PIT" end
  end
  -- With the player in the pits (e.g. back in the pit after the finish) the engine gap is garbage.
  local pcar = ac.getCar(0)
  if pcar and (pcar.isInPitlane == true or pcar.isInPit == true) then return "--" end
  local ok, g = pcall(ac.getGapBetweenCars, 0, c.idx)
  if not ok or not U.IsFinite(g) or math.abs(g) > 36000 then return "--" end
  return (c.rank < playerRank and "-" or "+") .. h.FormatGap(math.abs(g), playerBest)
end

local function fuelPercent(car)
  local max = car.maxFuel
  local fuel = car.fuel
  if type(max) ~= "number" or max <= 0 or type(fuel) ~= "number" then return nil end
  return math.max(0, math.min(100, fuel / max * 100))
end

-- Fuel as four separate level blocks (each one is 25%) with the percentage to the right of them.
-- 100% = 4 blocks, 50% = 2 blocks, 19% = 1 block. Every block keeps its own colour (red, orange,
-- yellow-green, green from the first to the last), so a low tank only lights the red one; empty blocks stay dark.
local BLOCKS = 4
local BLOCK_COLORS = {
  rgbm(0.92, 0.22, 0.22, 1),
  rgbm(1.00, 0.62, 0.12, 1),
  rgbm(0.80, 0.86, 0.18, 1),
  rgbm(0.28, 0.82, 0.32, 1),
}

local function fuelMetrics(height, textSize)
  local bw, bg = math.max(4, height * 0.48), math.max(2, height * 0.16)
  local blocksW = BLOCKS * bw + (BLOCKS - 1) * bg
  local textW = ui.measureDWriteText("100%", textSize).x
  return bw, bg, blocksW, textW, blocksW + 10 + textW
end

-- leftX: left edge of the whole fuel cell.
local function drawFuel(leftX, cy, height, textSize, pct)
  local bw, bg, blocksW, textW = fuelMetrics(height, textSize)
  local filled = pct and math.min(BLOCKS, math.ceil(pct / (100 / BLOCKS) - 0.001)) or 0
  for b = 1, BLOCKS do
    local bx = leftX + (b - 1) * (bw + bg)
    ui.drawRectFilled(vec2(bx, cy - height / 2), vec2(bx + bw, cy + height / 2),
        b <= filled and BLOCK_COLORS[b] or BLOCK_EMPTY, 2)
  end
  local label = pct and sFormat("%d%%", math.floor(pct + 0.5)) or "--"
  local m = ui.measureDWriteText(label, textSize)
  ui.dwriteDrawText(label, textSize, vec2(leftX + blocksW + 10 + textW - m.x, cy - m.y / 2), WHITE)
end

local function fitName(name, fontSize, maxW)
  local m = ui.measureDWriteText(name, fontSize)
  if m.x <= maxW then return name end
  while #name > 2 do
    name = name:sub(1, #name - 1)
    if ui.measureDWriteText(name .. "..", fontSize).x <= maxW then return name .. ".." end
  end
  return name
end

-- Title of the table: the player's class name as set in the dashboard.
local function classTitle()
  local key = S.displayClass[0]
  if key then
    for _, name in ipairs(S.releaseClasses) do
      if "class" .. string.lower(name) == key then return string.upper(name) end
    end
  end
  return "CLASS"
end

local function lapText()
  local ok, cur, total, _, formation = pcall(Positions.GetCurrentLapInfo)
  if not ok then return nil end
  local c = formation and "-" or sFormat("%d", math.max(1, cur or 1))
  if (total or 0) > 0 then return sFormat("LAP %s / %d", c, total) end
  return "LAP " .. c
end

local function textAt(text, size, x, cy, color, alignRight)
  local m = ui.measureDWriteText(text, size)
  ui.dwriteDrawText(text, size, vec2(alignRight and (x - m.x) or x, cy - m.y / 2), color)
  return m
end

-- h: shared helpers from draw.lua. top/bottom: vertical room for the whole card: race clock, table and
-- (when there is one) the flag / rolling-start message, all in a single panel.
function Extended.Draw(h, sim, qualiMode, w, top, bottom, clockText, clockColor, msg)
  Roster.Update(os.clock())

  local list = collect(qualiMode)
  if #list == 0 then return end
  local playerRank = 1
  for _, c in ipairs(list) do if c.idx == 0 then playerRank = c.rank end end
  local playerBest = Positions.GetCarBestLapMs(0)
  local leaderKey = list[1].key

  local x1, x2 = 12, w - 12
  -- Fixed sizes: resizing the window never scales the text. A smaller window shows fewer rows instead
  -- (the leader and the cars around you), down to 2; a taller one shows up to MAX_ROWS.
  local titleH, labelH = 24, 16
  local rowH, fs, fuelH, fuelFs = ROW_H, 15, 18, 14
  local clockH, msgH = 52, 28
  local fit = math.floor((bottom - top - clockH - titleH - labelH - (msg and msgH or 0)) / rowH)
  local rows = pickRows(list, math.max(2, math.min(MAX_ROWS, fit)))
  local n = #rows
  ui.pushDWriteFont(FONT)
  local _, _, _, _, fuelW = fuelMetrics(fuelH, fuelFs)
  ui.popDWriteFont()
  local tableTop = top + clockH
  local rowsEnd = tableTop + titleH + labelH + n * rowH
  local y2 = rowsEnd + (msg and msgH or 0)

  local fuelRight = x2 - 12
  local gapRight = fuelRight - fuelW - 16
  local posCx = x1 + 6 + 20
  local nameX = x1 + 6 + 46

  ui.pushDWriteFont(FONT)
  h.DrawCard(x1, top, x2, y2)

  -- Clock band on top.
  ui.drawRectFilled(vec2(x1, top), vec2(x2, top + 3), CLOCK_ACCENT, 6, ui.CornerFlags and ui.CornerFlags.Top)
  h.DrawClock(clockText or "00:00:00", (x1 + x2) / 2, top + clockH / 2 + 1, 44, clockColor or WHITE)

  -- Title band: class on the left, the human driver's lap on the right.
  local titleCy = tableTop + titleH / 2
  textAt(classTitle(), 12, x1 + 12, titleCy, ACCENT, false)
  local lt = (not qualiMode) and lapText() or nil
  if lt then textAt(lt, 12, x2 - 12, titleCy, WHITE, true) end

  -- Column labels.
  local labelY = tableTop + titleH
  ui.drawRectFilled(vec2(x1, labelY), vec2(x2, labelY + labelH), HEADER_FILL)
  local labelCy = labelY + labelH / 2
  local lfs = 10
  local pm = ui.measureDWriteText("POS", lfs)
  ui.dwriteDrawText("POS", lfs, vec2(posCx - pm.x / 2, labelCy - pm.y / 2), DIM)
  textAt("DRIVER", lfs, nameX, labelCy, DIM, false)
  textAt(qualiMode and "BEST" or "GAP", lfs, gapRight, labelCy, DIM, true)
  local fm = ui.measureDWriteText("FUEL", lfs)
  ui.dwriteDrawText("FUEL", lfs, vec2(fuelRight - fuelW / 2 - fm.x / 2, labelCy - fm.y / 2), DIM)

  for i, c in ipairs(rows) do
    local ry1 = labelY + labelH + (i - 1) * rowH
    local ry2 = ry1 + rowH
    local cy = (ry1 + ry2) / 2
    local isMe = c.idx == 0
    local info = Roster.byIndex[c.idx]
    local accent = info and info.color or ACCENT

    if isMe then
      ui.drawRectFilled(vec2(x1, ry1), vec2(x2, ry2), PLAYER_FILL)
    elseif i % 2 == 0 then
      ui.drawRectFilled(vec2(x1, ry1), vec2(x2, ry2), STRIPE_FILL)
    end
    ui.drawLine(vec2(x1, ry1), vec2(x2, ry1), SEPARATOR, 1)
    -- Class-coloured marker on the left edge.
    ui.drawRectFilled(vec2(x1, ry1 + 2), vec2(x1 + 4, ry2 - 2), accent)

    local posText = sFormat("%d", c.rank)
    local pmm = ui.measureDWriteText(posText, fs)
    ui.dwriteDrawText(posText, fs, vec2(posCx - pmm.x / 2, cy - pmm.y / 2), isMe and WHITE or WHITE)

    local gapStr = gapText(h, sim, qualiMode, c, playerRank, playerBest, leaderKey)
    local gs = math.max(11, fs * 0.92)
    local gm = textAt(gapStr, gs, gapRight, cy, isMe and WHITE or DIM, true)

    local name = fitName(c.name, fs, math.max(20, gapRight - gm.x - 12 - nameX))
    textAt(name, fs, nameX, cy, WHITE, false)

    drawFuel(fuelRight - fuelW, cy, fuelH, fuelFs, fuelPercent(c.car))
  end

  -- Flag / rolling-start message band at the bottom of the same panel.
  if msg then
    ui.drawLine(vec2(x1, rowsEnd), vec2(x2, rowsEnd), SEPARATOR, 1)
    h.DrawMessage(x1, rowsEnd, x2, y2, msg.text, msg.color, msg.flag)
  end
  ui.popDWriteFont()
end

return Extended
