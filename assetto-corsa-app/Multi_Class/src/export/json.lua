-- JSON bodies for exported results, built from the live session snapshot.
local S = require("src/state")
local U = require("src/util")

local Json = {}

local sFormat = string.format

-- Practice / Qualifying: a bare array of best laps, read by
-- RaceService.ImportSessionResultAsync.
function Json.BuildSessionResultJson()
  local entries = {}

  for _, carData in ipairs(S.liveSessionCars) do
    if carData.bestMs > 0 then
      entries[#entries + 1] = {
        driver = U.JsonEscape(carData.driver),
        car = U.JsonEscape(carData.car),
        skin = U.JsonEscape(carData.skin),
        best = U.FormatLapMs(carData.bestMs)
      }
    end
  end

  local out = { "[" }
  for k, e in ipairs(entries) do
    out[#out + 1] = "    {"
    out[#out + 1] = sFormat('        "driver": "%s",', e.driver)
    out[#out + 1] = sFormat('        "car": "%s",', e.car)
    out[#out + 1] = sFormat('        "skin": "%s",', e.skin)
    out[#out + 1] = sFormat('        "bestLapTimeMs": "%s"', e.best)
    out[#out + 1] = (k < #entries) and "    }," or "    }"
  end
  out[#out + 1] = "]"
  return table.concat(out, "\n")
end

local function playerLine(carData)
  return sFormat(
    '    { "name": "%s", "car": "%s", "skin": "%s" }',
    U.JsonEscape(carData.driver),
    U.JsonEscape(carData.car),
    U.JsonEscape(carData.skin))
end

local function collectRaceEntries()
  local players = {}
  local lapsTotal = {}
  local bestLaps = {}
  local order = {}

  for _, carData in ipairs(S.liveSessionCars) do
    local officialLapCount = math.max(0, carData.lapCount - S.liveRollingStartLapOffset)

    players[#players + 1] = playerLine(carData)
    lapsTotal[#lapsTotal + 1] = tostring(officialLapCount)

    if carData.bestMs > 0 and officialLapCount > 0 then
      bestLaps[#bestLaps + 1] = sFormat(
        '    { "car": %d, "lap": %d, "time": %d }',
        carData.index,
        officialLapCount,
        carData.bestMs)
    end

    order[#order + 1] = {
      index = carData.index,
      position = carData.position > 0 and carData.position or (carData.index + 1),
    }
  end
  return players, lapsTotal, bestLaps, order
end

local function buildRaceResultOrder(order)
  table.sort(order, function(a, b)
    if a.position == b.position then return a.index < b.index end
    return a.position < b.position
  end)

  local raceResult = {}
  for k, entry in ipairs(order) do
    raceResult[k] = tostring(entry.index)
  end
  return raceResult
end

-- Race: a Content Manager compatible session file, read by
-- RaceService.ImportRaceResultAsync. Emitting the same shape means the existing
-- importer, class classification and points award all work unchanged.
function Json.BuildRaceResultJson(trackName, totalLaps)
  local players, lapsTotal, bestLaps, order = collectRaceEntries()
  local raceResult = buildRaceResultOrder(order)

  local out = { "{" }
  out[#out + 1] = sFormat('  "track": "%s",', U.JsonEscape(trackName or ""))
  out[#out + 1] = '  "numberOfSessions": 1,'
  out[#out + 1] = '  "players": ['
  out[#out + 1] = table.concat(players, ",\n")
  out[#out + 1] = '  ],'
  out[#out + 1] = '  "sessions": ['
  out[#out + 1] = '    {'
  out[#out + 1] = '      "event": 0,'
  out[#out + 1] = '      "name": "Race",'
  out[#out + 1] = '      "type": 3,'
  out[#out + 1] = sFormat('      "lapsCount": %d,', math.max(0, totalLaps - S.liveRollingStartLapOffset))
  out[#out + 1] = '      "duration": 0,'
  out[#out + 1] = '      "laps": [],'
  out[#out + 1] = '      "lapsTotal": [' .. table.concat(lapsTotal, ", ") .. '],'
  out[#out + 1] = '      "bestLaps": ['
  out[#out + 1] = table.concat(bestLaps, ",\n")
  out[#out + 1] = '      ],'
  out[#out + 1] = '      "raceResult": [' .. table.concat(raceResult, ", ") .. ']'
  out[#out + 1] = '    }'
  out[#out + 1] = '  ]'
  out[#out + 1] = "}"
  return table.concat(out, "\n")
end

return Json
