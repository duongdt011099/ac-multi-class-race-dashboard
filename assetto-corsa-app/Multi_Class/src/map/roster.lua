-- Who is in which class (and which color), loaded from the dashboard's session file.
local SessionFile = require("src/map/session_file")

local Roster = {}

local FALLBACK = rgbm(0.62, 0.64, 0.7, 1)
local PALETTE = {
  rgbm(0.95, 0.26, 0.21, 1), rgbm(0.13, 0.59, 0.95, 1), rgbm(0.3, 0.69, 0.31, 1),
  rgbm(1.0, 0.76, 0.03, 1), rgbm(0.61, 0.15, 0.69, 1), rgbm(0.0, 0.74, 0.83, 1),
}

Roster.classes = {}      -- ordered: { name, color, count }
Roster.byIndex = {}      -- car index -> { class, driver, team, color }
Roster.loaded = false
Roster.source = "none"
Roster.fallbackColor = FALLBACK

local lastTry = -100
local lastSession = nil

local function carCount()
  local ok, n = pcall(function() return ac.getSim().carsCount end)
  return ok and n or 0
end

function Roster.Reload()
  Roster.classes, Roster.byIndex, Roster.loaded = {}, {}, false
  Roster.source = "none"
  local data = SessionFile.Read()
  if not data then return end

  local colorOf, classRow = {}, {}
  local listed = {}
  for _, c in ipairs(type(data.classes) == "table" and data.classes or {}) do
    if type(c) == "table" and type(c.name) == "string" then listed[#listed + 1] = c end
  end
  table.sort(listed, function(a, b) return (tonumber(a.order) or 0) < (tonumber(b.order) or 0) end)
  for i, c in ipairs(listed) do
    local key = c.name:lower()
    colorOf[key] = SessionFile.ParseColor(c.color) or PALETTE[(i - 1) % #PALETTE + 1]
  end

  local total = carCount()
  for _, car in ipairs(data.cars) do
    local index = tonumber(car.index)
    local name = type(car.class) == "string" and car.class:match("^%s*(.-)%s*$") or ""
    if index and index >= 0 and index < total and name ~= "" then
      local okId, actual = pcall(function() return ac.getCarID(index) end)
      if not (car.carId and okId and actual and actual:lower() ~= tostring(car.carId):lower()) then
        local key = name:lower()
        if not classRow[key] then
          classRow[key] = { name = name, color = colorOf[key] or PALETTE[(#Roster.classes) % #PALETTE + 1], count = 0 }
          Roster.classes[#Roster.classes + 1] = classRow[key]
        end
        classRow[key].count = classRow[key].count + 1
        Roster.byIndex[index] = { class = classRow[key].name, color = classRow[key].color,
                                  driver = car.driverName, team = car.team }
      end
    end
  end

  -- Keep dashboard order for the legend.
  local rank = {}
  for i, c in ipairs(listed) do rank[c.name:lower()] = i end
  table.sort(Roster.classes, function(a, b) return (rank[a.name:lower()] or 999) < (rank[b.name:lower()] or 999) end)
  Roster.loaded = #Roster.classes > 0
  if Roster.loaded then Roster.source = "dashboard" end
end

-- Class colour of the human car for the config window, which also runs in the main menu where the grid
-- (and so the roster above) does not exist yet: falls back to reading the session file directly.
local playerColor, playerColorAt = nil, -100
function Roster.PlayerColor(now)
  local info = Roster.byIndex[0]
  if info then return info.color end
  if now - playerColorAt > 2 then
    playerColorAt = now
    playerColor = nil
    local data = SessionFile.Read()
    if data then
      local className
      for _, car in ipairs(data.cars) do
        if tonumber(car.index) == 0 and type(car.class) == "string" then className = car.class:lower() break end
      end
      if className then
        local listed = type(data.classes) == "table" and data.classes or {}
        for i, c in ipairs(listed) do
          if type(c) == "table" and type(c.name) == "string" and c.name:lower() == className then
            playerColor = SessionFile.ParseColor(c.color) or PALETTE[(i - 1) % #PALETTE + 1]
          end
        end
      end
    end
  end
  return playerColor
end

-- Call every frame: loads on first use, after a session change, and keeps retrying until the file is found.
function Roster.Update(now)
  local okSim, sim = pcall(ac.getSim)
  local session = okSim and sim and sim.currentSessionIndex or nil
  if session ~= lastSession then
    lastSession = session
    lastTry = -100
    Roster.source = "none"
  end
  if Roster.source ~= "dashboard" and now - lastTry > 3 then
    lastTry = now
    Roster.Reload()
  end
end

return Roster
