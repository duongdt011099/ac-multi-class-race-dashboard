local driverCount                 = 0
local allDriversStartingPos       = {}
local rollingStartGridPositions   = {}
local firstFrame                  = true
local leaderboardOpened           = false
local lastSessionIndex            = nil
local lastSessionStarted          = nil
local playerStoppedState          = false
local raceDisplayStable           = nil
local raceDisplayPending          = nil
local RACE_POS_STABLE_FRAMES      = 2
local gridReordered               = false
local classReloadAttempted        = false
-- Armed when a started session ends (in-place restart) so the next pre-start re-stages the field
-- even though AC reuses the same session index/name and does not fire ac.onSessionStart.
local rollingPendingReinit        = false

local driverClass                 = {}
local displayClass                = {}
local classOrder                  = {}
local releaseClasses              = {}
local manualDriverClass           = {}
local classAddInput               = ""
local classAddError               = nil
local carClassCache               = {}
local uiSourceCache               = {}

local storedSettings              = ac.storage({ enduranceEnabled = true, resultExportEnabled = true, resultFolder = "" })

local enduranceEnabled           = true

local resultExportEnabled        = true
local resultFolder               = ""
local resultExportStatus         = ""
local exportedSessionKey         = nil
local nextExportRetryAt          = 0
local EXPORT_RETRY_SECONDS       = 2
local resultResetAfterExport     = false
local lastExportDiagnostic       = nil
local lastExportCaptureError     = nil
local lastExportUpdateDiagnostic = nil
local lastExportTransitionError  = nil

-- Snapshot of the session that is currently running. It is captured every frame while
-- the session is alive because by the time the session has ended Assetto Corsa has
-- already discarded the display name, the session index and the lap count, and the car
-- may have been moved back to the pits. The result is therefore written from this
-- snapshot, never from whatever the live state happens to be at the end.
local liveSessionType            = nil
local liveSessionKey             = nil
local liveTrackName              = ""
local liveTotalLaps              = 0
local liveSessionPending         = false
local liveSessionCars            = {}
local liveSessionCarCount        = 0
local liveSessionTimeLeft        = nil
local liveSessionHadCountdown    = false
local finishedRacePositions      = {}
local liveRollingStartLapOffset  = 0

-- Rolling starts are automatic in race sessions. Dashboard-launched races with a usable
-- fast_lane.ai receive one extra AC lap; the Lua result writer removes that formation segment.
--
-- The procedure mirrors the proven rapidough1s rolling-start app: the field is held on the grid,
-- released in two-car rows on a timer, runs single file up to the formation point and then stacks
-- into two columns for the start. The only addition is a per-class time gap so the classes stay
-- separated. Speeds are the reference defaults.
local ROLLING_START_FORMATION_SPEED_KMH = 100
local ROLLING_START_SPEED_KMH           = 70
-- Percentage of track progress where a car that has already crossed the line stacks into two
-- columns (double file) for the rolling start.
local ROLLING_START_FORMATION_POINT     = 0.67
-- A car is considered to have rolled over the start line once its spline position drops below this.
local ROLLING_START_ROLLED_OVER_SPLINE  = 0.3
-- Lateral distance of each column from the racing line, in metres. CSP keeps the AI on a lane with
-- the metre-based setAISplineAbsoluteOffset; single file is offset 0.
local ROLLING_START_LANE_METERS         = 3.0
local ROLLING_START_LANE_RAMP_MPS       = 1.5
-- After a class goes green its lane offset eases back to the racing line at this rate, and its
-- caution level ramps from 0 to 1 over GREEN_WINDOW_SECONDS (reference green-flag transition).
local ROLLING_START_GREEN_OFFSET_RAMP_MPS = 0.5
local ROLLING_START_GREEN_WINDOW_SECONDS  = 10.0
-- Catch-up multiplier used by the phase-two spacing (reference GetLinearMultiplier default).
local ROLLING_START_CATCHUP_LIMIT       = 1.5
-- Release cadence: two-car rows go every ROW_SECONDS; each class starts CLASS_GAP_SECONDS after
-- the previous class's final row, so the classes never bunch together on the run-up.
local ROLLING_START_ROW_SECONDS         = 1.5
local ROLLING_START_CLASS_GAP_SECONDS   = 3.0
-- AC resets an AI to the pits once its internal "stopped for" timer (physics.setAIStopCounter)
-- exceeds a few seconds. While a car is held on the grid we must keep that counter near zero, or
-- the longest-held cars at the back get teleported to the pits. This small value matches the
-- working rolling-start reference app.
local ROLLING_START_STOP_COUNTER_HELD   = 0.5
-- Minimum track length required before the procedure is allowed to run.
local ROLLING_START_MIN_TRACK_LENGTH_M  = 500

local rollingStartState               = "idle"
local rollingStartStatus              = ""
local rollingStartGroups              = {}
local rollingStartCars                = {}
-- Flat front-to-back release order (overall leader first). Cars are released in class-relative
-- two-car rows, one class after another, on the shared release timer.
local rollingStartQueue               = {}
local rollingStartRaceHasStarted      = false
local rollingStartReleaseTimer        = 0.0
local rollingStartSessionTime         = 0.0
local rollingStartLastFrame           = nil
local rollingStartSessionKey          = nil
local rollingStartDiagFrame           = 0
-- Which physical side the odd-numbered grid slots occupy during the rolling-start formation.
-- true = P1/P3/... form on the right, false = on the left (the reference app default). This is
-- re-detected from the human car's actual grid side every session and can be overridden in the
-- config window for the current session.
local rollingStartPoleOnRight         = false
-- Set once per session by DetectRollingStartPoleSide: "auto" when measured from the grid,
-- "manual" when the player toggled it afterwards.
local rollingStartPoleSideSource      = "auto"
-- One-shot guard for the lane-placement diagnostic (see MaintainRollingFormation).
local rollingStartLaneDiagDone        = false

-- The dashboard watches these subfolders and derives the session type from the
-- folder name, so every file must be named exactly yyMMdd-HHmmss.json.
local SUBFOLDER_PRACTICE         = "practice"
local SUBFOLDER_QUALIFYING       = "qualifying"
local SUBFOLDER_RACE             = "race"

-- ac.StateSession.type and ac.SimState.raceSessionType are ac.SessionType enums, which
-- survive the end of a session. The session *name* does not, which is why the export
-- used to fail whenever the car returned to the pits. Values are hardcoded as a fallback
-- for CSP builds that do not expose the enum table.
local SESSION_TYPE_PRACTICE      = (ac.SessionType and ac.SessionType.Practice) or 1
local SESSION_TYPE_QUALIFY       = (ac.SessionType and ac.SessionType.Qualify) or 2
local SESSION_TYPE_RACE          = (ac.SessionType and ac.SessionType.Race) or 3
local SESSION_TYPE_HOTLAP        = (ac.SessionType and ac.SessionType.Hotlap) or 4

local mMin = math.min
local sFormat = string.format
local getCar = ac.getCar
local setAITopSpeed = physics.setAITopSpeed
local setAICaution = physics.setAICaution
local setAIThrottleLimit = physics.setAIThrottleLimit
local setAISplineAbsoluteOffset = physics.setAISplineAbsoluteOffset

-- CSP keeps a car on a lateral lane offset from the AI racing line. The offset is in metres and is
-- applied through setAISplineAbsoluteOffset (the fraction-based setAISplineOffset is a different,
-- much smaller unit and does not hold a two-wide formation at speed).
local function SetRollingLaneOffset(carIndex, offsetMeters)
  pcall(physics.setAISplineAbsoluteOffset, carIndex, offsetMeters, false)
end

-- Belt-and-suspenders guard against AC teleporting a long-held car to the pits. The exact CSP
-- signature is not documented in the public SDK, so this is existence-checked and pcall-guarded:
-- if the function or its arguments differ on this build, it quietly does nothing and the
-- stop-counter fix alone prevents the teleport.
local function SetRollingPitBlock(carIndex, blocked)
  if physics.blockTeleportingToPits then
    pcall(physics.blockTeleportingToPits, carIndex, blocked)
  end
end

local DEBUG_ENABLED = true
local debugLogFile = nil
local function Log(msg)
  if not DEBUG_ENABLED then return end
  pcall(function()
    if not debugLogFile then
      local path = "debug.log"
      if type(ac.dirname) == "function" then
        path = ac.dirname() .. "/debug.log"
      elseif type(io.relative) == "function" then
        path = io.relative("debug.log")
      end
      debugLogFile = io.open(path, "a")
    end
    if debugLogFile then
      debugLogFile:write(msg .. "\n")
      debugLogFile:flush()
    end
  end)
end

-- Storage key resolution. Primary key is the car's AC ID; when that is unavailable we
-- fall back to the grid index so a class added in one session is not silently lost.
local function carClassKey(carID, carIndex)
  if carID and carID ~= "" then
    return "carclass_" .. tostring(carID)
  end
  if carIndex ~= nil then
    return "carclass_idx_" .. tostring(carIndex)
  end
  return nil
end

local function SaveCarClass(carID, cls, carIndex)
  local key = carClassKey(carID, carIndex)
  if not key then
    Log("SaveCarClass ABORT: no carID and no carIndex - value NOT persisted")
    return false
  end
  local ok = pcall(function()
    ac.storage[key] = cls or ""
  end)
  Log(sFormat("SaveCarClass: key=%s cls='%s' idx=%s ok=%s", key, tostring(cls), tostring(carIndex), tostring(ok)))
  return ok
end

local function LoadCarClass(carID, carIndex)
  local key = carClassKey(carID, carIndex)
  if not key then
    Log("LoadCarClass ABORT: no carID and no carIndex")
    return nil
  end
  local ok, v = pcall(function()
    return ac.storage[key]
  end)
  if ok and v and v ~= "" then
    Log(sFormat("LoadCarClass: key=%s -> '%s' idx=%s", key, tostring(v), tostring(carIndex)))
    return v
  end
  Log(sFormat("LoadCarClass: key=%s -> (empty) idx=%s", key, tostring(carIndex)))
  return nil
end

local function NormalizeClassName(name)
  local n = (name or ""):gsub("^%s+", ""):gsub("%s+$", ""):gsub("[=\n]", " ")
  if n:sub(1, 1) == "#" then
    n = n:sub(2):gsub("^%s+", ""):gsub("%s+$", "")
  end
  return n
end

local function GetCarUISource(carIndex)
  local cached = uiSourceCache[carIndex]
  if cached then return cached end
  local out = { tags = {}, class = nil, badge = nil }
  local okId, carID = pcall(function()
    return ac.getCarID and ac.getCarID(carIndex)
  end)
  local folder = nil
  local okFolder = pcall(function()
    folder = ac.getFolder and ac.getFolder(ac.FolderID and ac.FolderID.ContentCars)
  end)
  if okId and carID and okFolder and folder and folder ~= "" then
    local uiDir = folder .. "/" .. carID .. "/ui"
    local path = uiDir .. "/ui_car.json"
    local data = nil
    local okLoad = pcall(function()
      data = io.load and io.load(path)
    end)
    if okLoad and data and type(data) == "string" and #data > 0 then
      local cls = data:match('"class"%s*:%s*"([^"]*)"')
      if cls then
        out.class = cls
      end
      local tagsSection = data:match('"tags"%s*:%s*%[(.-)%]')
      if tagsSection then
        for q in tagsSection:gmatch('"([^"]*)"') do
          out.tags[#out.tags + 1] = q
        end
      end
    end
    local okBadge = pcall(function()
      local files = io.scanDir and io.scanDir(uiDir, "badge*")
      if files and #files > 0 then
        out.badge = uiDir .. "/" .. files[1]
      end
    end)
  end
  uiSourceCache[carIndex] = out
  return out
end

local function GetCarTags(carIndex)
  local ok, tags = pcall(function()
    return ac.getCarTags and ac.getCarTags(carIndex)
  end)
  if not ok then tags = nil end
  local out = {}
  local function collect(t)
    if type(t) ~= "table" then return end
    for _, val in ipairs(t) do
      if type(val) == "string" then
        local tt = val:gsub("^%s+", ""):gsub("%s+$", "")
        if tt:sub(1, 1) == "#" then
          tt = tt:sub(2)
        end
        if tt ~= "" then
          out[#out + 1] = tt
        end
      end
    end
  end
  if tags then
    collect(tags)
  end
  collect(GetCarUISource(carIndex).tags)
  return out
end

local GetCarClassCached

GetCarClassCached = function(carIndex)
  local cached = carClassCache[carIndex]
  if cached ~= nil then
    return (cached ~= "") and cached or nil
  end
  local cls = nil
  local ui = GetCarUISource(carIndex)
  if ui.class then
    local v = ui.class:gsub("^%s+", ""):gsub("%s+$", "")
    if v ~= "" then
      cls = v
    end
  end
  if not cls then
    local ok, config = pcall(function()
      return ac.INIConfig and ac.INIConfig.carData(carIndex, 'car.ini')
    end)
    if ok and config then
      local ok2, val = pcall(function()
        return config:get('BASIC', 'CLASS', '')
      end)
      if ok2 and type(val) == "string" then
        local v = val:gsub("^%s+", ""):gsub("%s+$", "")
        if v ~= "" then
          cls = v
        end
      end
    end
  end
  carClassCache[carIndex] = cls or ""
  return cls
end

local function TagMatchesWord(lowerTag, lowerClass)
  if lowerClass == "" then return false end
  if lowerTag == lowerClass then return true end
  local esc = lowerClass:gsub("([^%w])", "%%%1")
  if lowerTag:find("^" .. esc .. "%f[%A]") then return true end
  if lowerTag:find("%f[%w]" .. esc .. "%f[%A]") then return true end
  if lowerTag:find("%f[%w]" .. esc .. "$") then return true end
  return false
end

local function EditDistance(a, b)
  local m, n = #a, #b
  if m == 0 then return n end
  if n == 0 then return m end
  local d = {}
  for i = 0, m do
    d[i * (n + 1)] = i
  end
  for j = 0, n do
    d[j] = j
  end
  for i = 1, m do
    for j = 1, n do
      local cost = (a:sub(i, i) == b:sub(j, j)) and 0 or 1
      local row = i * (n + 1)
      d[row + j] = mMin(d[row - (n + 1) + j] + 1, d[row + j - 1] + 1, d[row - (n + 1) + j - 1] + cost)
    end
  end
  return d[m * (n + 1) + n]
end

local function CarMatchesClass(carIndex, lowerClass)
  for _, tt in ipairs(GetCarTags(carIndex)) do
    if TagMatchesWord(string.lower(tt), lowerClass) then
      return true
    end
  end
  local cc = GetCarClassCached(carIndex)
  if cc and TagMatchesWord(string.lower(cc), lowerClass) then
    return true
  end
  local carName = ac.getCarName and ac.getCarName(carIndex)
  if carName and string.lower(carName:gsub("^%s+", ""):gsub("%s+$", "")) == lowerClass then
    return true
  end
  local carID = ac.getCarID and ac.getCarID(carIndex)
  if carID and string.lower(carID:gsub("^%s+", ""):gsub("%s+$", "")) == lowerClass then
    return true
  end
  return false
end

local function GetAvailableClassNames()
  local out = {}
  local seen = {}
  for i = 0, driverCount - 1 do
    for _, tt in ipairs(GetCarTags(i)) do
      local k = string.lower(tt)
      if not seen[k] then
        seen[k] = true
        out[#out + 1] = tt
      end
    end
    local cc = GetCarClassCached(i)
    if cc then
      local k = string.lower(cc)
      if not seen[k] then
        seen[k] = true
        out[#out + 1] = cc
      end
    end
  end
  return out
end

local VerifyClassAssignments
local BuildClassInfo
local RemoveReleaseClass

local function SaveClassNames()
  pcall(function()
    ac.storage["releaseClassNames"] = table.concat(releaseClasses, "|")
  end)
end

local function LoadClassNames()
  local ok, v = pcall(function()
    return ac.storage["releaseClassNames"]
  end)
  if ok and type(v) == "string" and v ~= "" then
    return v
  end
  return nil
end

local function AddReleaseClass(name)
  local n = NormalizeClassName(name)
  Log(sFormat("AddReleaseClass: name='%s' normalized='%s' driverCount=%d", tostring(name), n, driverCount))
  if n == "" then
    classAddError = "Enter a class name first."
    return
  end

  local lower = string.lower(n)
  for i = 1, #releaseClasses do
    if string.lower(releaseClasses[i]) == lower then
      classAddError = "Class '" .. n .. "' is already added."
      return
    end
  end

  local matched = 0
  for i = 0, driverCount - 1 do
    local m
    local ok = pcall(function()
      m = CarMatchesClass(i, lower)
    end)
    if not ok then m = false end
    if m then
      manualDriverClass[i] = n
      local okSave, carID = pcall(function() return ac.getCarID and ac.getCarID(i) end)
      if not okSave then carID = nil end
      local saved = SaveCarClass(carID, n, i)
      if not saved then
        Log(sFormat("AddReleaseClass WARN: car %d class '%s' assigned in memory but NOT persisted", i, n))
      end
      matched = matched + 1
    end
  end

  if matched == 0 then
    local avail = GetAvailableClassNames()
    local availText = (#avail > 0) and table.concat(avail, ", ") or "(none)"
    local suggestion = nil
    local bestDist = 3
    for _, an in ipairs(avail) do
      local dist = EditDistance(string.lower(an), lower)
      if dist < bestDist then
        bestDist = dist
        suggestion = an
      end
    end
    Log(sFormat("AddReleaseClass FAIL: matched=0 driverCount=%d avail=%d tags_sample={}", driverCount, #avail))
    classAddError = "No cars match '" .. n .. "'. Available tags/classes: " .. availText .. " (case is ignored). [debug: driverCount=" .. driverCount .. "]"
    if suggestion then
      classAddError = classAddError .. " Did you mean '" .. suggestion .. "'?"
    end
    return
  end

  releaseClasses[#releaseClasses + 1] = n
  classAddError = nil
  classAddInput = ""
  SaveClassNames()
  BuildClassInfo()
  Log(sFormat("AddReleaseClass DONE: '%s' matched=%d releaseClasses={%s}", n, matched, table.concat(releaseClasses, ",")))
  VerifyClassAssignments()
end

VerifyClassAssignments = function()
  local cars = {}
  local tlist = {}
  for i = 0, driverCount - 1 do
    local cls = manualDriverClass[i]
    if not cls or cls == "" then
      local carName = ac.getCarName and ac.getCarName(i)
      local carID = ac.getCarID and ac.getCarID(i)
      cars[#cars + 1] = carName or carID or sFormat("Car %d", i + 1)
      local tags = GetCarTags(i)
      tlist[#tlist + 1] = (#tags > 0) and table.concat(tags, ", ") or "(no tag)"
    end
  end

  if #cars == 0 then
    ui.toast(ui.Icons.Play, sFormat("All %d car(s) on the grid belong to an added class.", driverCount))
  else
    local details = {}
    for j = 1, #cars do
      details[#details + 1] = cars[j] .. " (" .. tlist[j] .. ")"
    end
    ui.toast(ui.Icons.Warning, sFormat("%d car(s) are not assigned to any class: %s", #cars, table.concat(details, ", ")))
  end
end

RemoveReleaseClass = function(idx)
  local n = releaseClasses[idx]
  if not n then return end
  table.remove(releaseClasses, idx)
  for i = 0, driverCount - 1 do
    if manualDriverClass[i] == n then
      manualDriverClass[i] = ""
      local okID, carID = pcall(function() return ac.getCarID and ac.getCarID(i) end)
      if not okID then carID = nil end
      SaveCarClass(carID, "", i)
    end
  end
  classAddError = nil
  SaveClassNames()
  BuildClassInfo()
end

BuildClassInfo = function()
  Log(sFormat("BuildClassInfo: dc=%d entries=%d", driverCount, #manualDriverClass))
  driverClass = {}
  displayClass = {}
  classOrder = {}

  if driverCount == 0 then return end

  local classMinPos = {}

  for i = 0, driverCount - 1 do
    local raw = manualDriverClass[i]
    local key
    if raw and raw ~= "" then
      key = "class" .. string.lower(raw)
    end

    driverClass[i] = key

    -- Display-only fallback: used exclusively by the leaderboard CLASS panel so that
    -- unassigned cars still get a sensible class position. This map must never feed
    -- VerifyClassAssignments or ReorderGridByClass, otherwise unadded classes would
    -- be reported as added ones.
    local dkey = key
    if not dkey then
      local detected = GetCarClassCached(i)
      if detected and detected ~= "" then
        dkey = "class" .. string.lower(detected)
      end
    end
    displayClass[i] = dkey

    if key then
      local pos = allDriversStartingPos[i] or (i + 1)
      if not classMinPos[key] or pos < classMinPos[key] then
        classMinPos[key] = pos
      end
    end
  end

  for key, _ in pairs(classMinPos) do
    classOrder[#classOrder + 1] = key
  end

  local classBestLapMs = {}
  for _, key in ipairs(classOrder) do
    local best = nil
    for i = 0, driverCount - 1 do
      if driverClass[i] == key then
        local ok, car = pcall(getCar, i)
        if ok and car then
          local okBt, btMs = pcall(function() return car.bestLapTimeMs end)
          if okBt and btMs and type(btMs) == "number" and btMs > 0 then
            if best == nil or btMs < best then
              best = btMs
            end
          end
        end
      end
    end
    classBestLapMs[key] = best
  end

  table.sort(classOrder, function(a, b)
    if classBestLapMs[a] and classBestLapMs[b] then
      return classBestLapMs[a] < classBestLapMs[b]
    end
    if classBestLapMs[a] then return true end
    if classBestLapMs[b] then return false end
    return classMinPos[a] < classMinPos[b]
  end)

  Log(sFormat("BuildClassInfo DONE: %d classes order={%s}", #classOrder, table.concat(classOrder, ",")))

end

-- Defined before LoadManualClassesFromStorage uses it. Previously this lived further
-- down the file, so the call resolved to a nil global and threw every frame, which
-- aborted the rest of session setup (including BuildClassInfo).
local function NormalizePath(p)
  if type(p) ~= "string" or p == "" then return p or "" end
  return p:gsub("\\", "/")
end

local function LoadManualClassesFromStorage()
  Log(sFormat("LoadManualClassesFromStorage: START driverCount=%d", driverCount))
  releaseClasses = {}
  local seen = {}
  local anyLoaded = false
  for i = 0, driverCount - 1 do
    local okID, carID = pcall(function() return ac.getCarID and ac.getCarID(i) end)
    if not okID then carID = nil end
    local cls = LoadCarClass(carID, i)
    if type(cls) == "string" and cls ~= "" then
      manualDriverClass[i] = cls
      anyLoaded = true
      if not seen[cls] then
        seen[cls] = true
        releaseClasses[#releaseClasses + 1] = cls
      end
    else
      manualDriverClass[i] = ""
    end
  end
  Log(sFormat("LoadManualClassesFromStorage: DONE anyLoaded=%s releaseClasses=%d classes={%s}", tostring(anyLoaded), #releaseClasses, table.concat(releaseClasses, ",")))
  if not anyLoaded then
    local savedNames = LoadClassNames()
    if savedNames and savedNames ~= "" then
      Log(sFormat("LoadManualClassesFromStorage: per-car storage empty, re-detecting from saved class names: %s", savedNames))
      for name in savedNames:gmatch("[^|]+") do
        local lower = string.lower(name)
        local matched = 0
        for i = 0, driverCount - 1 do
          local m
          local ok = pcall(function() m = CarMatchesClass(i, lower) end)
          if not ok then m = false end
          if m then
            manualDriverClass[i] = name
            local okID2, carID2 = pcall(function() return ac.getCarID and ac.getCarID(i) end)
            if not okID2 then carID2 = nil end
            SaveCarClass(carID2, name, i)
            matched = matched + 1
          end
        end
        if matched > 0 then
          releaseClasses[#releaseClasses + 1] = name
        end
        Log(sFormat("LoadManualClassesFromStorage: re-detected class '%s' matched %d cars", name, matched))
      end
    end
  end
  -- Restoring settings must never be able to abort class loading, so it is isolated.
  local okSettings, settingsErr = pcall(function()
    if storedSettings.enduranceEnabled ~= nil then
      enduranceEnabled = storedSettings.enduranceEnabled
    end
    -- Probe legacy settings independently: ac.storage rejects undeclared keys, and a
    -- failed legacy lookup must not abort restoration of the current settings below.
    local legacyEnabledOk, legacyEnabled = pcall(function()
      return storedSettings.qualiSaveEnabled
    end)
    local legacyFolderOk, legacyFolder = pcall(function()
      return storedSettings.qualiSaveFolder
    end)

    if legacyEnabledOk and legacyEnabled ~= nil then
      resultExportEnabled = legacyEnabled
      storedSettings.resultExportEnabled = legacyEnabled
    elseif storedSettings.resultExportEnabled ~= nil then
      resultExportEnabled = storedSettings.resultExportEnabled
    end

    if legacyFolderOk and legacyFolder ~= nil then
      resultFolder = NormalizePath(legacyFolder or "")
      storedSettings.resultFolder = resultFolder
    elseif storedSettings.resultFolder ~= nil then
      resultFolder = NormalizePath(storedSettings.resultFolder or "")
    end

    if legacyEnabledOk then
      pcall(function() storedSettings.qualiSaveEnabled = nil end)
    end
    if legacyFolderOk then
      pcall(function() storedSettings.qualiSaveFolder = nil end)
    end
  end)
  if not okSettings then
    Log("LoadManualClassesFromStorage: settings restore FAILED err=" .. tostring(settingsErr))
  end

  local okBuild, buildErr = pcall(BuildClassInfo)
  if not okBuild then
    Log("LoadManualClassesFromStorage: BuildClassInfo FAILED err=" .. tostring(buildErr))
  end
end

local function GetSessionNameLower(sim)
  sim = sim or ac.getSim()
  local ok, name = pcall(function() return ac.getSessionName(sim.currentSessionIndex) end)
  if not ok or not name then return "" end
  return string.lower(name)
end

local function IsRaceMode(sim)
  return GetSessionNameLower(sim):find("race") ~= nil
end

local function IsQualiPracticeMode(sim)
  local sn = GetSessionNameLower(sim)
  return sn:find("qualifying") ~= nil or sn:find("practice") ~= nil
end

-- The dashboard derives the session type from the subfolder the result lands in, so these
-- three names are a contract with Services/LuaResultPaths.cs.
--
-- Resolved from the ac.SessionType enum rather than the session display name: the name is
-- already gone by the time the session ends and the car returns to the pits, which made
-- the export silently produce nothing. Hotlap is a practice-style session, so it is
-- reported as practice instead of being dropped.
local function SessionTypeToFolder(sessionType)
  if type(sessionType) ~= "number" then return nil end
  if sessionType == SESSION_TYPE_RACE then return SUBFOLDER_RACE end
  if sessionType == SESSION_TYPE_QUALIFY then return SUBFOLDER_QUALIFYING end
  if sessionType == SESSION_TYPE_PRACTICE or sessionType == SESSION_TYPE_HOTLAP then
    return SUBFOLDER_PRACTICE
  end
  return nil
end

-- Reads the live session type from the session object, falling back to the sim's
-- raceSessionType. Only valid while the session is still running.
local function ReadLiveSessionType(sim)
  sim = sim or ac.getSim()

  if ac.getSession then
    local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
    if okSession and session then
      local okType, sessionType = pcall(function() return session.type end)
      if okType and type(sessionType) == "number" then return sessionType end
    end
  end

  local okRace, raceType = pcall(function() return sim.raceSessionType end)
  if okRace and type(raceType) == "number" then return raceType end

  return nil
end

-- Only used as a fallback when the app starts while results are already on screen, where
-- the enum is unavailable and the display name is all there is.
local function GetExportSessionType()
  if IsRaceMode() then return SUBFOLDER_RACE end
  if not IsQualiPracticeMode() then return nil end
  if GetSessionNameLower():find("qualifying") ~= nil then return SUBFOLDER_QUALIFYING end
  return SUBFOLDER_PRACTICE
end

local function GetTrackNameSafe()
  if ac.getTrackName then
    local ok, name = pcall(ac.getTrackName)
    if ok and type(name) == "string" and name ~= "" then return name end
  end
  return ""
end

local function GetCurrentSessionKey(sim)
  sim = sim or ac.getSim()
  local name = ""
  local ok, n = pcall(function() return ac.getSessionName(sim.currentSessionIndex) end)
  if ok and type(n) == "string" then name = n end
  return sFormat("%s:%s", tostring(sim.currentSessionIndex), name)
end

local function ReadRollingStartLapOffset()
  if not ac.INIConfig or type(ac.INIConfig.raceConfig) ~= "function" then return 0 end

  local okConfig, config = pcall(ac.INIConfig.raceConfig)
  if not okConfig or not config then return 0 end

  local okOffset, offset = pcall(function()
    return config:get("HEADER", "__MCR_ROLLING_START_LAP_OFFSET", 0)
  end)
  if not okOffset then return 0 end

  return tonumber(offset) == 1 and 1 or 0
end

-- ac.StateSession.isOver and sim.isLookingAtSessionResults are the two documented
-- "this session is done" signals; either one means the result is final.
local function IsSessionFinished(sim)
  sim = sim or ac.getSim()
  if sim.isInMainMenu == true then return false end

  local okResults, showingResults = pcall(function() return sim.isLookingAtSessionResults end)
  if okResults and showingResults == true then return true end

  if ac.getSession then
    local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
    if okSession and session then
      local okOver, isOver = pcall(function() return session.isOver end)
      if okOver and isOver == true then return true end
    end
  end

  return false
end

local function FormatLapMs(ms)
  if type(ms) ~= "number" or ms <= 0 then return "--:--.---" end
  local minutes = math.floor(ms / 60000)
  local rest = ms % 60000
  local seconds = math.floor(rest / 1000)
  local milli = rest % 1000
  return sFormat("%02d:%02d.%03d", minutes, seconds, milli)
end

local function JsonEscape(s)
  s = tostring(s or "")
  -- Control characters must be escaped too: a driver name containing a raw newline
  -- or tab would otherwise produce invalid JSON.
  s = s:gsub("\\", "\\\\"):gsub('"', '\\"')
  s = s:gsub("\n", "\\n"):gsub("\r", "\\r"):gsub("\t", "\\t")
  s = s:gsub("[\1-\31]", function(c) return sFormat("\\u%04x", string.byte(c)) end)
  return s
end

local function ResultDefaultFolder()
  -- Matches the dashboard default (Services/LuaResultPaths.cs) so the common case
  -- needs no configuration on either side.
  local okDocs, docs = pcall(function() return ac.getFolder(ac.FolderID.Documents) end)
  if okDocs and type(docs) == "string" and docs ~= "" then
    return NormalizePath(docs .. "/Assetto Corsa/mcr-results")
  end

  local candidates = {}

  local ok1, d1 = pcall(function() return ac.dirname() end)
  if ok1 and type(d1) == "string" and d1 ~= "" then
    candidates[#candidates + 1] = d1
  end

  local ok2, d2 = pcall(function() return ac.getFolder(ac.FolderID.ExtLua) end)
  if ok2 and type(d2) == "string" and d2 ~= "" then
    candidates[#candidates + 1] = d2
  end

  for _, c in ipairs(candidates) do
    local n = NormalizePath(c)
    if n ~= "" then
      return n
    end
  end
  return ""
end

local function ResultFolderWritable(folder)
  if folder == "" then return false, "no folder" end
  pcall(function()
    if type(io.createDir) == "function" then io.createDir(folder) end
  end)
  if type(io.dirExists) == "function" then
    local okE, exists = pcall(io.dirExists, folder)
    if okE and exists == false then
      return false, "folder does not exist"
    end
  end
  if type(io.save) ~= "function" then
    return true, nil
  end
  local probe = folder .. "/.mcr_write_test.tmp"
  local okW = pcall(function()
    if not io.save(probe, "") then
      error("io.save returned false")
    end
  end)
  pcall(function()
    if type(io.deleteFile) == "function" then io.deleteFile(probe) end
  end)
  if not okW then
    return false, "folder is not writable"
  end
  return true, nil
end

local function ResultEffectiveFolder()
  if resultFolder and resultFolder ~= "" then
    return resultFolder
  end
  return ResultDefaultFolder()
end

local function GetCarIdentity(i)
  local identity = { driver = "", car = "", skin = "" }

  local okD, d = pcall(function() return ac.getDriverName and ac.getDriverName(i) end)
  if okD and type(d) == "string" then identity.driver = d end

  local okC, c = pcall(function() return ac.getCarID and ac.getCarID(i) end)
  if okC and type(c) == "string" then identity.car = c end

  local okS, s = pcall(function() return ac.getCarSkinID and ac.getCarSkinID(i) end)
  if okS and type(s) == "string" then identity.skin = s end

  return identity
end

local function GetCarLapData(i)
  local data = {
    available = false,
    lapCount = 0,
    bestMs = 0,
    position = 0,
    inPit = nil,
    inPitlane = nil,
  }
  local okCar, car = pcall(getCar, i)
  if not okCar or not car then return data end
  data.available = true

  local okL, lc = pcall(function() return car.lapCount end)
  if okL and type(lc) == "number" and lc > 0 then data.lapCount = lc end

  local okB, bt = pcall(function() return car.bestLapTimeMs end)
  if okB and type(bt) == "number" and bt > 0 then data.bestMs = bt end

  local okP, p = pcall(function() return car.racePosition end)
  if okP and type(p) == "number" and p > 0 then data.position = p end

  local okPit, inPit = pcall(function() return car.isInPit end)
  if okPit and type(inPit) == "boolean" then data.inPit = inPit end

  local okPitlane, inPitlane = pcall(function() return car.isInPitlane end)
  if okPitlane and type(inPitlane) == "boolean" then data.inPitlane = inPitlane end

  return data
end

-- Keep a recent copy of every car's identity and result data while AC still exposes the
-- grid. `sim.carsCount` can become zero as soon as a timed session ends, so export must
-- never depend on live car objects after that transition.
local function CaptureLiveCarResults(forceFreeze)
  if driverCount <= 0 then return end

  local raceFinished = liveSessionType == SUBFOLDER_RACE
      and (forceFreeze == true or IsSessionFinished())
  local cars = {}
  local availableCars = 0
  for i = 0, driverCount - 1 do
    local identity = GetCarIdentity(i)
    local lapData = GetCarLapData(i)
    local position = lapData.position
    local finalLapReached = liveSessionType == SUBFOLDER_RACE
        and liveTotalLaps > 0
        and lapData.lapCount >= liveTotalLaps

    -- Assetto Corsa can rewrite the live racePosition after a car crosses the finish line
    -- (for example, promoting the player to P1 while the remaining field is still on track).
    -- Preserve the last pre-finish overall position for result export and the OVERALL panel. If AC
    -- ends the session before every car reaches the configured lap count, freeze the last observed
    -- classification at that transition as well.
    if finalLapReached or raceFinished then
      local frozenPosition = finishedRacePositions[i]

      if not frozenPosition then
        local previous = liveSessionCars[i + 1]
        if previous and previous.position > 0 and (raceFinished or previous.lapCount < liveTotalLaps) then
          frozenPosition = previous.position
        else
          frozenPosition = position
        end

        if frozenPosition and frozenPosition > 0 then
          finishedRacePositions[i] = frozenPosition
        else
          frozenPosition = nil
        end
      end

      if frozenPosition then
        position = frozenPosition
      end
    elseif finishedRacePositions[i] then
      position = finishedRacePositions[i]
    end

    cars[#cars + 1] = {
      index = i,
      driver = identity.driver,
      car = identity.car,
      skin = identity.skin,
      lapCount = lapData.lapCount,
      bestMs = lapData.bestMs,
      position = position,
      inPit = lapData.inPit,
      inPitlane = lapData.inPitlane,
    }
    if lapData.available or identity.driver ~= "" or identity.car ~= "" then
      availableCars = availableCars + 1
    end
  end

  -- Keep the last good copy if AC briefly reports a grid before populating its car data.
  if availableCars > 0 then
    liveSessionCars = cars
    liveSessionCarCount = #cars
  end
end

-- Snapshots everything the result needs while the session is still alive. Called every
-- frame while sim.isSessionStarted is true, so the last successful call before the
-- session ends holds metadata and car results even after AC reports zero cars.
local function CaptureLiveSession(sim)
  sim = sim or ac.getSim()
  if sim.isSessionStarted ~= true then return end

  local folder = SessionTypeToFolder(ReadLiveSessionType(sim))
  if folder == nil then return end

  local laps = 0
  if ac.getSession then
    local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
    if okSession and session then
      local okLaps, sessionLaps = pcall(function() return session.laps end)
      if okLaps and type(sessionLaps) == "number" and sessionLaps > 0 then laps = sessionLaps end
    end
  end

  local sessionKey = GetCurrentSessionKey(sim)
  if liveSessionKey ~= sessionKey then
    liveRollingStartLapOffset = folder == SUBFOLDER_RACE and ReadRollingStartLapOffset() or 0
  end

  liveSessionType    = folder
  liveSessionKey     = sessionKey
  liveTrackName      = GetTrackNameSafe()
  liveTotalLaps      = laps
  local okTimeLeft, timeLeft = pcall(function() return sim.sessionTimeLeft end)
  if okTimeLeft and type(timeLeft) == "number" then
    liveSessionTimeLeft = timeLeft
    if timeLeft > 0 then liveSessionHadCountdown = true end
  end
  CaptureLiveCarResults()
end

-- Called when a session ends, to arm the export. The result is written from the snapshot
-- on the next frame, outside the session-validity check and without requiring live cars.
local function ArmSessionEnded()
  if liveSessionType == nil then return end
  if exportedSessionKey ~= nil and exportedSessionKey == liveSessionKey then return end
  local wasPending = liveSessionPending
  liveSessionPending = true
  if not wasPending then
    Log("EXPORT: session ended, pending export for " .. tostring(liveSessionType) ..
        " key=" .. tostring(liveSessionKey) .. " cars=" .. tostring(liveSessionCarCount))
  end
end

-- Called when a new session starts, so a previous session's snapshot cannot leak into it.
local function ClearLiveSession()
  liveSessionType     = nil
  liveSessionKey      = nil
  liveTrackName       = ""
  liveTotalLaps       = 0
  liveSessionPending  = false
  liveSessionCars     = {}
  liveSessionCarCount = 0
  liveSessionTimeLeft = nil
  liveSessionHadCountdown = false
  finishedRacePositions = {}
  liveRollingStartLapOffset = 0
end

-- Practice / Qualifying: a bare array of best laps, read by
-- RaceService.ImportSessionResultAsync.
local function BuildSessionResultJson()
  local entries = {}

  for _, carData in ipairs(liveSessionCars) do
    if carData.bestMs > 0 then
      entries[#entries + 1] = {
        driver = JsonEscape(carData.driver),
        car = JsonEscape(carData.car),
        skin = JsonEscape(carData.skin),
        best = FormatLapMs(carData.bestMs)
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

-- Race: a Content Manager compatible session file, read by
-- RaceService.ImportRaceResultAsync. Emitting the same shape means the existing
-- importer, class classification and points award all work unchanged.
local function BuildRaceResultJson(trackName, totalLaps)
  local players = {}
  local lapsTotal = {}
  local bestLaps = {}
  local order = {}

  for _, carData in ipairs(liveSessionCars) do
    local officialLapCount = math.max(0, carData.lapCount - liveRollingStartLapOffset)

    players[#players + 1] = sFormat(
      '    { "name": "%s", "car": "%s", "skin": "%s" }',
      JsonEscape(carData.driver),
      JsonEscape(carData.car),
      JsonEscape(carData.skin))

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

  table.sort(order, function(a, b)
    if a.position == b.position then return a.index < b.index end
    return a.position < b.position
  end)

  local raceResult = {}
  for k, entry in ipairs(order) do
    raceResult[k] = tostring(entry.index)
  end

  local out = { "{" }
  out[#out + 1] = sFormat('  "track": "%s",', JsonEscape(trackName or ""))
  out[#out + 1] = '  "numberOfSessions": 1,'
  out[#out + 1] = '  "players": ['
  out[#out + 1] = table.concat(players, ",\n")
  out[#out + 1] = '  ],'
  out[#out + 1] = '  "sessions": ['
  out[#out + 1] = '    {'
  out[#out + 1] = '      "event": 0,'
  out[#out + 1] = '      "name": "Race",'
  out[#out + 1] = '      "type": 3,'
  out[#out + 1] = sFormat('      "lapsCount": %d,', math.max(0, totalLaps - liveRollingStartLapOffset))
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

-- Write to a temporary name and rename, so the dashboard can never pick up a
-- partially written result file.
local function WriteResultFile(targetPath, contents)
  local tempPath = targetPath .. ".tmp"

  if not io.save(tempPath, contents) then
    error("io.save returned false")
  end

  if type(os.rename) == "function" then
    pcall(function() os.rename(tempPath, targetPath) end)

    local exists = true
    if type(io.fileExists) == "function" then
      local ok, result = pcall(io.fileExists, targetPath)
      exists = ok and result == true
    end

    if exists then
      pcall(function()
        if type(io.deleteFile) == "function" then io.deleteFile(tempPath) end
      end)
      return true
    end
  end

  pcall(function()
    if type(io.deleteFile) == "function" then io.deleteFile(tempPath) end
  end)

  if not io.save(targetPath, contents) then
    error("io.save returned false")
  end

  return true
end

-- Writes the result from the snapshot taken while the session was running. sessionKey is
-- remembered on success so the same session can never be written twice.
local function ExportSessionResult(sessionType, sessionKey, trackName, totalLaps, silent, force)
  -- A failed export is retried rather than abandoned, but the results screen can sit for
  -- minutes and each attempt probes the folder and rebuilds the JSON, so the retries are
  -- throttled instead of running on every frame.
  if not force and type(os.time) == "function" then
    local now = os.time()
    if now < nextExportRetryAt then return end
    nextExportRetryAt = now + EXPORT_RETRY_SECONDS
  end

  if sessionType == nil then return end

  local root = ResultEffectiveFolder()
  if root == "" then
    local msg = "No result folder available - choose one with 'Choose result folder...'"
    if resultExportStatus ~= msg then
      resultExportStatus = msg
      Log("EXPORT: " .. msg)
      if not silent then ui.toast(ui.Icons.Warning, msg) end
    end
    return
  end

  local targetDir = root .. "/" .. sessionType
  local writable, why = ResultFolderWritable(targetDir)
  if not writable then
    local msg = "Cannot save session result: " .. tostring(why) .. " (" .. targetDir .. ")"
    if resultExportStatus ~= msg then
      resultExportStatus = msg
      Log("EXPORT: " .. msg)
      if not silent then ui.toast(ui.Icons.Warning, msg) end
    end
    return
  end

  if type(os.date) ~= "function" then
    local msg = "os.date is unavailable on this CSP version - cannot name the result file."
    if resultExportStatus ~= msg then
      resultExportStatus = msg
      Log("EXPORT: " .. msg)
      if not silent then ui.toast(ui.Icons.Warning, msg) end
    end
    return
  end

  local fileName = os.date("%y%m%d-%H%M%S") .. ".json"
  local targetPath = targetDir .. "/" .. fileName
  local contents = (sessionType == SUBFOLDER_RACE)
      and BuildRaceResultJson(trackName, totalLaps)
      or BuildSessionResultJson()

  Log("EXPORT: writing " .. targetPath)
  local ok, err = pcall(function()
    WriteResultFile(targetPath, contents)
  end)

  if ok then
    exportedSessionKey = sessionKey
    liveSessionPending = false
    resultExportStatus = sFormat(
      "Saved %s result at %s -> %s/%s",
      sessionType,
      os.date("%H:%M:%S"),
      sessionType,
      fileName)
    if not silent then ui.toast(ui.Icons.Play, "Session result saved") end
    Log("EXPORT: wrote " .. targetPath)
  else
    -- exportedSessionKey is left unset so the next frame retries rather than
    -- silently losing the session result.
    local msg = "Failed to save session result: " .. tostring(err)
    if resultExportStatus ~= msg then
      resultExportStatus = msg
      if not silent then ui.toast(ui.Icons.Warning, msg) end
    end
    Log("EXPORT: FAILED err=" .. tostring(err))
  end
end

-- Called once per frame from script.update. Writes at most once per session.
--
-- The primary trigger is the armed end-of-session flag, not the results screen: when the
-- car returns to the pits the results screen may never appear and the session name is
-- already gone. The results-screen check is kept as a secondary trigger for the case
-- where the app starts while results are already on screen.
local function CheckForFinishedSession(sim)
  if not resultExportEnabled then return end
  if liveSessionType == nil then return end
  if liveSessionCarCount <= 0 then return end
  if liveSessionPending == false and not IsSessionFinished(sim) then return end
  if exportedSessionKey ~= nil and exportedSessionKey == liveSessionKey then return end

  ExportSessionResult(liveSessionType, liveSessionKey, liveTrackName, liveTotalLaps)
end

-- Only called when a new session begins. Resetting on the end of a session instead would
-- clear the guard that stops the result being written twice.
local function ResetResultExport()
  exportedSessionKey = nil
  nextExportRetryAt = 0
  ClearLiveSession()
end

local function CaptureLiveSessionSafely(sim)
  local ok, err = pcall(CaptureLiveSession, sim)
  if ok then
    lastExportCaptureError = nil
  else
    local message = tostring(err)
    if message ~= lastExportCaptureError then
      Log("EXPORT: live-session capture failed: " .. message)
      lastExportCaptureError = message
    end
  end
end

local function CheckForFinishedSessionSafely(sim)
  local ok, err = pcall(CheckForFinishedSession, sim)
  if not ok then
    Log("EXPORT: finish check failed: " .. tostring(err))
  end
end

local function IsPlayerInPit()
  local okCar, car = pcall(getCar, 0)
  if okCar and car then
    local observed = false
    local okPit, inPit = pcall(function() return car.isInPit end)
    if okPit and type(inPit) == "boolean" then
      observed = true
      if inPit then return true end
    end
    local okPitlane, inPitlane = pcall(function() return car.isInPitlane end)
    if okPitlane and type(inPitlane) == "boolean" then
      observed = true
      if inPitlane then return true end
    end
    if observed then return false end
  end

  -- AC can remove car objects at session teardown. Use the last live observation in that
  -- case; the live session snapshot is refreshed every frame until the grid disappears.
  local playerSnapshot = liveSessionCars[1]
  if playerSnapshot then
    if playerSnapshot.inPit == true or playerSnapshot.inPitlane == true then return true end
    if playerSnapshot.inPit == false and playerSnapshot.inPitlane == false then return false end
  end
  return nil
end

local function ExportExpiredSessionOnRelease()
  local currentTimeLeft = liveSessionTimeLeft
  local okSim, sim = pcall(ac.getSim)
  if okSim and sim then
    local okTime, timeLeft = pcall(function() return sim.sessionTimeLeft end)
    if okTime and type(timeLeft) == "number" then currentTimeLeft = timeLeft end
  end

  Log(sFormat(
    "EXPORT: release check enabled=%s type=%s key=%s snapshotCars=%d hadCountdown=%s leftMs=%s exported=%s",
    tostring(resultExportEnabled), tostring(liveSessionType), tostring(liveSessionKey),
    liveSessionCarCount, tostring(liveSessionHadCountdown), tostring(currentTimeLeft),
    tostring(exportedSessionKey == liveSessionKey)))

  if not resultExportEnabled then return end
  if liveSessionType == nil or liveSessionCarCount <= 0 then return end
  if not liveSessionHadCountdown or type(currentTimeLeft) ~= "number" or currentTimeLeft > 0 then return end
  if exportedSessionKey ~= nil and exportedSessionKey == liveSessionKey then return end

  liveSessionPending = true
  local ok, err = pcall(
    ExportSessionResult,
    liveSessionType,
    liveSessionKey,
    liveTrackName,
    liveTotalLaps,
    true,
    true)
  if not ok then
    Log("EXPORT: release fallback failed: " .. tostring(err))
  end
end

-- Emit one diagnostic line whenever AC's export-relevant state changes. This makes it
-- possible to distinguish a missed end transition from a missing session type, empty car
-- snapshot, disabled setting, or an actual write failure without logging every frame.
local function LogExportState(sim, sessionEnded, sessionBegan, sessionSwitched)
  local sessionType = ReadLiveSessionType(sim)
  local sessionTimeLeft = nil
  local okTimeLeft, timeLeft = pcall(function() return sim.sessionTimeLeft end)
  if okTimeLeft and type(timeLeft) == "number" then sessionTimeLeft = timeLeft end
  local playerInPit = IsPlayerInPit()
  local sessionTimeBucket = sessionTimeLeft and math.floor(sessionTimeLeft / 1000) or nil
  local capturedTimeBucket = liveSessionTimeLeft and math.floor(liveSessionTimeLeft / 1000) or nil
  local sessionOver = nil
  if ac.getSession then
    local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
    if okSession and session then
      local okOver, over = pcall(function() return session.isOver end)
      if okOver then sessionOver = over end
    end
  end
  local resultsVisible = nil
  local okResults, results = pcall(function() return sim.isLookingAtSessionResults end)
  if okResults then resultsVisible = results end
  local sessionName = ""
  local okName, name = pcall(function() return ac.getSessionName(sim.currentSessionIndex) end)
  if okName and type(name) == "string" then sessionName = name end

  local parts = {
    tostring(sim.isSessionStarted),
    tostring(lastSessionStarted),
    tostring(sim.currentSessionIndex),
    tostring(lastSessionIndex),
    tostring(driverCount),
    tostring(sessionName),
    tostring(sessionType),
    tostring(sim.raceSessionType),
    tostring(sessionTimeBucket),
    tostring(playerInPit),
    tostring(capturedTimeBucket),
    tostring(liveSessionHadCountdown),
    tostring(sessionOver),
    tostring(resultsVisible),
    tostring(sessionEnded),
    tostring(sessionBegan),
    tostring(sessionSwitched),
    tostring(liveSessionType),
    tostring(liveSessionKey),
    tostring(liveSessionCarCount),
    tostring(liveSessionPending),
    tostring(resultResetAfterExport),
    tostring(resultExportEnabled),
  }
  local signature = table.concat(parts, "|")
  if signature ~= lastExportDiagnostic then
    lastExportDiagnostic = signature
    Log(sFormat(
      "EXPORT: state started=%s prevStarted=%s index=%s prevIndex=%s cars=%d name='%s' " ..
      "sessionType=%s raceType=%s leftMs=%s playerInPit=%s capturedLeftMs=%s hadCountdown=%s " ..
      "over=%s results=%s ended=%s began=%s switched=%s liveType=%s liveKey=%s " ..
      "snapshotCars=%d pending=%s deferredReset=%s enabled=%s",
      tostring(sim.isSessionStarted), tostring(lastSessionStarted),
      tostring(sim.currentSessionIndex), tostring(lastSessionIndex), driverCount,
      sessionName, tostring(sessionType), tostring(sim.raceSessionType),
      tostring(sessionTimeLeft), tostring(playerInPit), tostring(liveSessionTimeLeft),
      tostring(liveSessionHadCountdown), tostring(sessionOver), tostring(resultsVisible),
      tostring(sessionEnded), tostring(sessionBegan), tostring(sessionSwitched),
      tostring(liveSessionType), tostring(liveSessionKey), liveSessionCarCount,
      tostring(liveSessionPending), tostring(resultResetAfterExport),
      tostring(resultExportEnabled)))
  end
end

-- Process the export-related session transitions in one place so their ordering is
-- testable. If the session ends on the same frame that AC switches to its pit/garage
-- session, retain and export the ended session snapshot instead of clearing it as a new
-- session. A later session begin/switch still resets the previous export state.
local function UpdateResultExportFrame(sim)
  local sessionSwitched = lastSessionIndex ~= nil
                        and lastSessionIndex ~= sim.currentSessionIndex
  local currentSessionKey = GetCurrentSessionKey(sim)
  local currentSessionFolder = SessionTypeToFolder(ReadLiveSessionType(sim))
  local sessionTimeLeft = nil
  local okTimeLeft, timeLeft = pcall(function() return sim.sessionTimeLeft end)
  if okTimeLeft and type(timeLeft) == "number" then sessionTimeLeft = timeLeft end
  local playerInPit = IsPlayerInPit()
  local switchedAwayFromTrackedSession = sessionSwitched
                        and liveSessionType ~= nil
                        and liveSessionKey ~= nil
                        and currentSessionKey ~= liveSessionKey
  local sessionName = GetSessionNameLower(sim)
  local lostTrackedSessionData = driverCount <= 0
                        and liveSessionType ~= nil
                        and liveSessionCarCount > 0
                        and (currentSessionFolder == nil or sessionName == "")
  local timedSessionExpiredInPit = liveSessionType ~= nil
                        and liveSessionHadCountdown
                        and sessionTimeLeft ~= nil
                        and sessionTimeLeft <= 0
                        and playerInPit == true
  local endSignal = (lastSessionStarted == true and sim.isSessionStarted ~= true)
                        or switchedAwayFromTrackedSession
                        or lostTrackedSessionData
                        or timedSessionExpiredInPit
  local sessionNeedsExport = liveSessionType ~= nil
                        and (liveSessionPending or exportedSessionKey ~= liveSessionKey)
  local sessionEnded = endSignal and sessionNeedsExport
  local sessionBegan = lastSessionStarted ~= nil and lastSessionStarted == false
                        and sim.isSessionStarted == true and lastSessionIndex ~= nil
  local newlyEnded = sessionEnded and not liveSessionPending
  local sessionChanged = newlyEnded or sessionBegan or sessionSwitched

  LogExportState(sim, sessionEnded, sessionBegan, sessionSwitched)

  if sessionEnded then
    -- Refresh the old snapshot only if AC is still showing that same session. If the
    -- index already moved, the live cars belong to the incoming state (or are gone).
    if not sessionSwitched and driverCount > 0 then
      local ok, err = pcall(CaptureLiveCarResults, true)
      if not ok then Log("EXPORT: final car snapshot failed: " .. tostring(err)) end
    end
    ArmSessionEnded()
    if sessionSwitched or sessionBegan then
      resultResetAfterExport = true
    end
    CheckForFinishedSessionSafely(sim)
  elseif sessionBegan or sessionSwitched then
    if liveSessionPending then
      -- Don't discard a result that still needs retries just because AC began its next
      -- session. Finish exporting the previous snapshot first.
      resultResetAfterExport = true
      CheckForFinishedSessionSafely(sim)
    else
      ResetResultExport()
      resultResetAfterExport = false
      CaptureLiveSessionSafely(sim)
    end
  elseif resultResetAfterExport then
    CheckForFinishedSessionSafely(sim)
  else
    CaptureLiveSessionSafely(sim)
    CheckForFinishedSessionSafely(sim)
  end

  if resultResetAfterExport and not liveSessionPending then
    ResetResultExport()
    resultResetAfterExport = false
    if sim.isSessionStarted == true then
      CaptureLiveSessionSafely(sim)
    end
  end

  lastSessionIndex = sim.currentSessionIndex
  lastSessionStarted = sim.isSessionStarted
  return sessionChanged, sessionEnded, sessionBegan, sessionSwitched
end

local function CheckSessionValidity()
  local sim = ac.getSim()

  if ac.getPatchVersionCode() < 3334 then
    return false, "Please update your Custom Shaders Patch (CSP) to version 0.2.7 or higher!"
  end

  local sn = GetSessionNameLower()
  if not (sn:find("race") or sn:find("practice") or sn:find("qualifying") or sn:find("hotlap")) then
    return false, "This app works in Race, Practice, or Qualifying sessions!"
  end

  if sim.isOnlineRace then
    return false, "This app only works for Offline/Single-player sessions!"
  end

  return true, nil
end

local function RecordStartingPositions()
  allDriversStartingPos = {}
  rollingStartGridPositions = {}
  for i = 0, driverCount - 1 do
    local car = getCar(i)
    if car then
      local position = car.racePosition
      if type(position) ~= "number" or position <= 0 then
        position = i + 1
      end
      allDriversStartingPos[i] = position
      rollingStartGridPositions[i] = position
    end
  end
  BuildClassInfo()
end

-- Returns the car's left-hand axis (x, y, z), or nil when the state does not expose one. In AC/CSP
-- `car.side` is the LEFT axis; the `look x up` fallback yields the same handedness in AC's basis.
local function GetCarLeftAxis(car)
  if not car then return nil end
  local side = car.side
  if side then
    return side.x, side.y, side.z
  end
  if car.look and car.up then
    local lx, ly, lz = car.look.x, car.look.y, car.look.z
    local ux, uy, uz = car.up.x, car.up.y, car.up.z
    return ly * uz - lz * uy, lz * ux - lx * uz, lx * uy - ly * ux
  end
  return nil
end

-- Measures which side of its grid row the human car starts on and derives the lane convention for
-- the whole field from it. The player is a reliable anchor: we know the exact grid slot (odd/even)
-- and can read the world position relative to the car sharing the row. If the geometry is
-- unavailable we keep the reference default (odd positions on the left).
local function DetectRollingStartPoleSide()
  rollingStartPoleOnRight = false
  rollingStartPoleSideSource = "auto"

  local player = getCar(0)
  if not player then
    Log("ROLLING: pole-side detect skipped (no player car); default LEFT")
    return
  end

  local pos = rollingStartGridPositions[0]
  if type(pos) ~= "number" or pos <= 0 then
    pos = player.racePosition
  end
  if type(pos) ~= "number" or pos <= 0 then
    Log("ROLLING: pole-side detect skipped (no grid slot); default LEFT")
    return
  end

  -- The car sharing the player's two-wide row: slot+1 on an odd slot, slot-1 on an even one.
  -- Their midpoint approximates the racing line, so the vector between them is purely lateral.
  local partnerPos = (pos % 2 == 1) and (pos + 1) or (pos - 1)
  local partner = nil
  for i = 0, driverCount - 1 do
    local car = getCar(i)
    if car and car.racePosition == partnerPos then
      partner = car
      break
    end
  end
  if not partner then
    Log(sFormat("ROLLING: pole-side detect skipped (no row partner for slot %d); default LEFT", pos))
    return
  end

  local pp, gp = player.position, partner.position
  if not (pp and gp) then
    Log("ROLLING: pole-side detect skipped (no world position); default LEFT")
    return
  end

  -- Player's own left-hand axis (see GetCarLeftAxis). A positive dot of (player - partner) with the
  -- left axis therefore means the player sits on the LEFT of the row.
  local sx, sy, sz = GetCarLeftAxis(player)
  if not sx then
    Log("ROLLING: pole-side detect skipped (no lateral axis); default LEFT")
    return
  end

  local dx, dy, dz = pp.x - gp.x, pp.y - gp.y, pp.z - gp.z
  local lateral = dx * sx + dy * sy + dz * sz
  local playerOnRight = lateral < 0
  local playerIsOdd = (pos % 2 == 1)

  -- Odd slots on the right exactly when the player's grid side agrees with their slot parity.
  rollingStartPoleOnRight = (playerIsOdd == playerOnRight)

  Log(sFormat("ROLLING: pole-side detect slot=%d partner=%d lateral=%.3f side=(%.2f,%.2f,%.2f) playerRight=%s -> poleOnRight=%s",
      pos, partnerPos, lateral, sx, sy, sz, tostring(playerOnRight), tostring(rollingStartPoleOnRight)))
end

local function ReorderGridByClass()
  if #classOrder == 0 or driverCount == 0 then return end

  local function getBestLap(idx)
    local ok, car = pcall(getCar, idx)
    if ok and car then
      local okBt, bt = pcall(function() return car.bestLapTimeMs end)
      if okBt and bt and type(bt) == "number" and bt > 0 then return bt end
    end
    return 999999999
  end

  local classSlots = {}
  for ci, key in ipairs(classOrder) do
    classSlots[key] = {}
  end
  local noClass = {}

  for i = 0, driverCount - 1 do
    local cls = driverClass[i]
    if cls and classSlots[cls] then
      classSlots[cls][#classSlots[cls] + 1] = i
    else
      noClass[#noClass + 1] = i
    end
  end

  for _, key in ipairs(classOrder) do
    table.sort(classSlots[key], function(a, b)
      return getBestLap(a) < getBestLap(b)
    end)
  end
  table.sort(noClass, function(a, b)
    return getBestLap(a) < getBestLap(b)
  end)

  local ordered = {}
  for _, key in ipairs(classOrder) do
    for _, idx in ipairs(classSlots[key]) do
      ordered[#ordered + 1] = idx
    end
  end
  for _, idx in ipairs(noClass) do
    ordered[#ordered + 1] = idx
  end

  local pos = 1
  for _, idx in ipairs(ordered) do
    allDriversStartingPos[idx] = pos
    pos = pos + 1
  end

  Log(sFormat("ReorderGridByClass: reordered=%d drivers, classes={%s}", driverCount, table.concat(classOrder, ",")))
end

local function IsFinite(v)
  return type(v) == "number" and v == v and math.abs(v) ~= math.huge
end

local function NormalizeTrackProgress(progress)
  return progress - math.floor(progress)
end

local function RestoreAIControl(carIndex, clearOffset)
  pcall(setAITopSpeed, carIndex, 999999.0)
  pcall(setAIThrottleLimit, carIndex, 1.0)
  pcall(setAICaution, carIndex, 1.0)
  pcall(physics.setAIStopCounter, carIndex, 0.0)
  SetRollingPitBlock(carIndex, false)
  if clearOffset ~= false then
    SetRollingLaneOffset(carIndex, 0.0)
  end
end

local function RestoreRollingStartControls()
  -- The player car (index 0) is never held or autopiloted during the formation, so only the AI
  -- cars need their controls handed back.
  for carIndex in pairs(rollingStartCars) do
    if carIndex ~= 0 then
      RestoreAIControl(carIndex, true)
    end
  end

  rollingStartCars = {}
  rollingStartGroups = {}
  rollingStartQueue = {}
  rollingStartRaceHasStarted = false
  rollingStartReleaseTimer = 0.0
  rollingStartSessionTime = 0.0
  rollingStartLastFrame = nil
  rollingStartSessionKey = nil
end

local function MarkRollingStartUnavailable(message)
  RestoreRollingStartControls()
  rollingStartState = "unavailable"
  rollingStartStatus = message or "Rolling start unavailable"
  Log("ROLLING: unavailable: " .. rollingStartStatus)
  pcall(function() ui.toast(ui.Icons.Warning, rollingStartStatus) end)
end

local function RollingClassLabel(classKey)
  if classKey == "__rolling_unclassified" then return "Unclassified" end

  for _, className in ipairs(releaseClasses) do
    if string.lower("class" .. className) == classKey then
      return className
    end
  end

  return tostring(classKey):gsub("^class", "")
end

local function BuildRollingStartGroups()
  local groups = {}
  local byClass = {}

  local function getGridPosition(carIndex)
    local position = rollingStartGridPositions[carIndex]
    if type(position) == "number" and position > 0 then return position end

    local car = getCar(carIndex)
    position = car and car.racePosition or nil
    if type(position) == "number" and position > 0 then return position end
    return carIndex + 1
  end

  local function ensureGroup(classKey)
    local group = byClass[classKey]
    if not group then
      group = { classKey = classKey, label = RollingClassLabel(classKey), cars = {} }
      byClass[classKey] = group
      groups[#groups + 1] = group
    end
    return group
  end

  -- Seed known classes, then derive their actual rolling order from AC's captured grid positions.
  -- The dashboard writes the qualifying order into the race grid; current best laps are not a
  -- reliable substitute for that order once the session has loaded.
  for _, classKey in ipairs(classOrder) do
    ensureGroup(classKey)
  end

  for carIndex = 0, driverCount - 1 do
    local classKey = driverClass[carIndex] or "__rolling_unclassified"
    local group = ensureGroup(classKey)
    group.cars[#group.cars + 1] = carIndex
  end

  for _, group in ipairs(groups) do
    table.sort(group.cars, function(a, b)
      local positionA = getGridPosition(a)
      local positionB = getGridPosition(b)

      if positionA == positionB then return a < b end
      return positionA < positionB
    end)
    group.gridPosition = #group.cars > 0 and getGridPosition(group.cars[1]) or math.huge
  end

  local nonEmpty = {}
  for _, group in ipairs(groups) do
    if #group.cars > 0 then
      nonEmpty[#nonEmpty + 1] = group
    end
  end

  table.sort(nonEmpty, function(a, b)
    if a.gridPosition == b.gridPosition then
      return a.classKey < b.classKey
    end
    return a.gridPosition < b.gridPosition
  end)

  for _, group in ipairs(nonEmpty) do
    local positions = {}
    for _, carIndex in ipairs(group.cars) do
      positions[#positions + 1] = tostring(getGridPosition(carIndex))
    end
    Log(sFormat("ROLLING: grid order class '%s' start=%d positions={%s}",
        group.label, group.gridPosition, table.concat(positions, ",")))
  end

  return nonEmpty
end

-- Linear catch-up multiplier from the reference app: 1.0 at minDist, up to maxMult at maxDist.
local function GetLinearMultiplier(dist, minDist, maxDist, maxMult)
  maxMult = maxMult or ROLLING_START_CATCHUP_LIMIT
  if dist <= minDist then return 1.0 end
  if dist >= maxDist then return maxMult end
  return 1.0 + ((dist - minDist) / (maxDist - minDist)) * (maxMult - 1.0)
end

-- Lane follows the car's overall starting grid slot: odd and even slots take opposite lanes, and
-- rollingStartPoleOnRight decides which physical side the odd slots occupy (mirrors the reference
-- app's "Target Lane for Pole Position" setting). The value is auto-detected from the human car's
-- grid side each session; a manual toggle overrides it for the current session.
local function GetRollingLaneOffset(gridPosition)
  local isOdd = ((gridPosition or 1) % 2 == 1)
  if rollingStartPoleOnRight then
    return isOdd and ROLLING_START_LANE_METERS or -ROLLING_START_LANE_METERS
  end
  return isOdd and -ROLLING_START_LANE_METERS or ROLLING_START_LANE_METERS
end

-- Gentle side-to-side wave once the race is under way, so the two columns are not perfectly rigid.
local function GetOrganicLaneOffset(carIndex)
  if not rollingStartRaceHasStarted then return 0.0 end

  local time = rollingStartSessionTime
  local wave1 = math.sin(time * 0.25 + carIndex * 1.7) * 0.40
  local wave2 = math.cos(time * 0.40 - carIndex * 2.3) * 0.10
  return wave1 + wave2
end

-- Time-based release: a car goes when the shared release timer reaches its class start plus its
-- own row. Row 0 of the first class has threshold 0, so the overall leader always gets the field
-- moving (which then starts the timer via the "field is moving" check).
local function IsRollingCarReleased(state)
  local group = rollingStartGroups[state.groupIndex]
  if not group then return true end
  local threshold = (group.classStartTime or 0.0) + (state.row or 0) * ROLLING_START_ROW_SECONDS
  return rollingStartReleaseTimer >= threshold
end

-- Phase-two (double file) spacing, ported from the reference app but using class-relative
-- positions: the class leader waits for its P2, P2 tucks in behind its P1, and every other car
-- blends a "column" target (the car two rows ahead) with a "row partner" target. Returns the
-- commanded top speed and a throttle limit.
local function ApplyRollingPhaseTwoSpacing(state, group, trackLength)
  local car = getCar(state.carIndex)
  local startPos = state.lockedPhase2Position or state.classPosition
  local targetSpeed = ROLLING_START_SPEED_KMH
  local throttleLimit = 1.0
  if not car or type(startPos) ~= "number" then return targetSpeed, throttleLimit end

  local map = group and group.positionToCar or {}

  if startPos == 1 then
    local p2Index = map[2]
    if p2Index then
      local p2Car = getCar(p2Index)
      if p2Car then
        local isP2Ahead = (p2Car.splinePosition > car.splinePosition)
        if isP2Ahead then
          local longDist = (p2Car.splinePosition - car.splinePosition) * trackLength
          targetSpeed = ROLLING_START_SPEED_KMH * GetLinearMultiplier(longDist, 0.0, 10.0)
        end
      end
    end

  elseif startPos == 2 then
    local p1Index = map[1]
    if p1Index then
      local p1Car = getCar(p1Index)
      if p1Car then
        local longDist = (p1Car.splinePosition - car.splinePosition) * trackLength
        local isP1Ahead = (p1Car.splinePosition > car.splinePosition)
        if isP1Ahead then
          if longDist > 1.0 then
            targetSpeed = ROLLING_START_SPEED_KMH * GetLinearMultiplier(longDist, 1.0, 10.0)
          else
            local ratio = 0.85 + (longDist / 1.0) * 0.15
            targetSpeed = ROLLING_START_SPEED_KMH * ratio
            throttleLimit = ratio
          end
        else
          local leadError = math.abs(longDist)
          targetSpeed = math.max(ROLLING_START_SPEED_KMH * 0.80, ROLLING_START_SPEED_KMH - leadError * 4.0)
          throttleLimit = math.max(0.70, 1.0 - (leadError / 5.0))
        end
      end
    end

  else
    local targetSpeedCol = ROLLING_START_SPEED_KMH
    local throttleCol = 1.0
    local splineDist = 0.0

    local aheadIndex = map[startPos - 2]
    if aheadIndex then
      local aheadCar = getCar(aheadIndex)
      if aheadCar then
        local isAheadCarInFront = (aheadCar.splinePosition > car.splinePosition)
        if isAheadCarInFront then
          splineDist = (aheadCar.splinePosition - car.splinePosition) * trackLength
          if splineDist > 8.0 then
            local gapError = splineDist - 8.0
            targetSpeedCol = ROLLING_START_SPEED_KMH * GetLinearMultiplier(gapError, 0.0, 14.0)
          elseif splineDist > 1.5 then
            local ratio = 0.50 + ((splineDist - 1.5) / 6.5) * 0.50
            targetSpeedCol = ROLLING_START_SPEED_KMH * ratio
            throttleCol = ratio
          else
            targetSpeedCol = 0.0
            throttleCol = 0.0
          end
        else
          local leadError = (car.splinePosition - aheadCar.splinePosition) * trackLength
          targetSpeedCol = math.max(ROLLING_START_SPEED_KMH * 0.80, ROLLING_START_SPEED_KMH - leadError * 4.0)
          throttleCol = math.max(0.70, 1.0 - (leadError / 5.0))
        end
      end
    end

    local targetSpeedRow = ROLLING_START_SPEED_KMH
    local throttleRow = 1.0

    local isEven = (startPos % 2 == 0)
    if isEven then
      local partnerIndex = map[startPos - 1]
      if partnerIndex then
        local partnerCar = getCar(partnerIndex)
        if partnerCar then
          local isPartnerAhead = (partnerCar.splinePosition > car.splinePosition)
          local longDistRow = (partnerCar.splinePosition - car.splinePosition) * trackLength
          if isPartnerAhead then
            if longDistRow > 1.0 then
              targetSpeedRow = ROLLING_START_SPEED_KMH * GetLinearMultiplier(longDistRow - 1.0, 0.0, 10.0)
            else
              local ratio = 0.85 + (longDistRow / 1.0) * 0.15
              targetSpeedRow = ROLLING_START_SPEED_KMH * ratio
              throttleRow = ratio
            end
          else
            local leadError = math.abs(longDistRow)
            targetSpeedRow = math.max(ROLLING_START_SPEED_KMH * 0.80, ROLLING_START_SPEED_KMH - leadError * 4.0)
            throttleLimit = math.max(0.70, 1.0 - (leadError / 5.0))
          end
        end
      end
    else
      local partnerIndex = map[startPos + 1]
      if partnerIndex then
        local partnerCar = getCar(partnerIndex)
        if partnerCar then
          local isPartnerBehind = (car.splinePosition > partnerCar.splinePosition)
          local longDistRow = (car.splinePosition - partnerCar.splinePosition) * trackLength
          if isPartnerBehind then
            if longDistRow > 2.0 then
              targetSpeedRow = ROLLING_START_SPEED_KMH * 0.85
              throttleRow = 0.70
            end
          else
            targetSpeedRow = ROLLING_START_SPEED_KMH * GetLinearMultiplier(math.abs(longDistRow), 0.0, 10.0)
          end
        end
      end
    end

    local blendFactor = math.min(1.0, math.max(0.0, (splineDist - 8.0) / 6.0))
    local parallelSpeed = math.min(targetSpeedCol, targetSpeedRow)
    local parallelThrottle = math.min(throttleCol, throttleRow)

    targetSpeed = parallelSpeed + (targetSpeedCol - parallelSpeed) * blendFactor
    throttleLimit = parallelThrottle + (throttleCol - parallelThrottle) * blendFactor
  end

  return targetSpeed, throttleLimit
end

local function HoldRollingCar(carIndex)
  if carIndex == 0 then return end

  setAITopSpeed(carIndex, 0.0)
  setAIThrottleLimit(carIndex, 0.0)
  setAICaution(carIndex, 1.0)
  physics.setAIStopCounter(carIndex, ROLLING_START_STOP_COUNTER_HELD)
  SetRollingPitBlock(carIndex, true)
  SetRollingLaneOffset(carIndex, 0.0)
end

local function ReleaseRollingCar(carIndex)
  local state = rollingStartCars[carIndex]
  if not state or state.released then return end

  state.released = true
  state.releasedAt = rollingStartSessionTime
  if carIndex == 0 then return end

  physics.setAIStopCounter(carIndex, 0.0)
  SetRollingPitBlock(carIndex, false)
  Log(sFormat("ROLLING: released car %d (queue %d)", carIndex, state.queueIndex or -1))
end

local function IsFieldStillAtStartingGrid(sim)
  if driverCount <= 0 then return false end

  local session = ac.getSession and ac.getSession(sim.currentSessionIndex) or nil
  if session and type(session.leaderCompletedLaps) == "number" and session.leaderCompletedLaps > 0 then
    return false
  end

  for carIndex = 0, driverCount - 1 do
    local car = getCar(carIndex)
    if not car or (car.speedKmh or 0) > 3.0 or (car.sessionLapCount or car.lapCount or 0) > 0 then
      return false
    end

    local progress = NormalizeTrackProgress(car.splinePosition or 0)
    if progress > 0.1 and progress < 0.9 then
      return false
    end
  end

  return true
end

local function PrepareRollingStart(sim)
  if rollingStartState ~= "idle" or not IsRaceMode(sim) then return end

  if driverCount <= 0 or
      not ac.hasTrackSpline or not ac.hasTrackSpline() then
    MarkRollingStartUnavailable("Track spline or car data is unavailable.")
    return
  end

  local trackLength = sim.trackLengthM
  if not IsFinite(trackLength) or trackLength <= 500 then
    MarkRollingStartUnavailable("Track length is unavailable or too short.")
    return
  end

  local session = ac.getSession and ac.getSession(sim.currentSessionIndex) or nil
  local preStart = sim.isSessionStarted ~= true
  if session and type(session.startTime) == "number" and type(sim.time) == "number" then
    preStart = preStart or sim.time < session.startTime
  end
  preStart = preStart or IsFieldStillAtStartingGrid(sim)
  if not preStart then
    MarkRollingStartUnavailable("The race was already moving when the Lua app loaded.")
    return
  end

  local groups = BuildRollingStartGroups()
  if #groups == 0 then
    MarkRollingStartUnavailable("No cars were available to stage.")
    return
  end

  -- Flat front-to-back release order derived from the class ordering (grid position 1 first). The
  -- field is NOT moved: cars launch from AC's real grid as the queue releases them.
  local queue = {}
  for groupIndex, group in ipairs(groups) do
    group.index = groupIndex
    for _, carIndex in ipairs(group.cars) do
      queue[#queue + 1] = { carIndex = carIndex, groupIndex = groupIndex }
    end
  end

  rollingStartGroups = groups
  rollingStartCars = {}
  rollingStartQueue = {}
  rollingStartLaneDiagDone = false

  -- Per-car rolling state. Longitudinal positions are class-relative so each class forms its own
  -- single-file then double-file grid, while the lane follows the car's overall starting grid slot
  -- (so every car keeps the side it started on). The player is included so the AI queue behind
  -- them, but is never controlled.
  for groupIndex, group in ipairs(groups) do
    group.rows = math.max(1, math.ceil(#group.cars / 2))
    for classPosition, carIndex in ipairs(group.cars) do
      local gridPosition = rollingStartGridPositions[carIndex] or (carIndex + 1)
      rollingStartCars[carIndex] = {
        carIndex = carIndex,
        groupIndex = groupIndex,
        classKey = group.classKey,
        classPosition = classPosition,
        row = math.floor((classPosition - 1) / 2),
        laneSide = GetRollingLaneOffset(gridPosition) < 0 and -1 or 1,
        gridPosition = gridPosition,
        released = false,
        hasRolledOver = false,
        enteredPhase2 = false,
        lockedPhase2Position = nil,
        currentSplineOffset = 0.0,
        green = false,
        wrapCount = 0,
        lastProgress = nil,
      }
    end
  end

  for queueIndex, entry in ipairs(queue) do
    rollingStartQueue[queueIndex] = entry.carIndex
    local state = rollingStartCars[entry.carIndex]
    local car = getCar(entry.carIndex)
    if state then
      state.queueIndex = queueIndex
      state.lastProgress = car and car.splinePosition or nil
    end
  end

  -- Cumulative class release schedule: each class starts CLASS_GAP seconds after the previous
  -- class's final row, so the gap between the tail of one and the head of the next is fixed.
  local startTime = 0.0
  for _, group in ipairs(groups) do
    group.classStartTime = startTime
    startTime = startTime + group.rows * ROLLING_START_ROW_SECONDS + ROLLING_START_CLASS_GAP_SECONDS
    group.green = false
    group.complete = false
    group.windowCloseTimer = 0
  end

  rollingStartState = "staged"
  rollingStartStatus = "ROLLING START READY"
  rollingStartSessionKey = GetCurrentSessionKey(sim)
  rollingStartLastFrame = nil
  rollingStartDiagFrame = 0
  Log(sFormat("ROLLING: prepared %d cars in %d classes (reference-style, no teleport); lapOffset=%d",
      #queue, #groups, liveRollingStartLapOffset))
end

-- Formation begins when AC green-lights the race. Every AI car is held on the grid; the row-0
-- leader of each class is released immediately (threshold 0) and the rest follow on the timer.
local function BeginRollingStart(now)
  rollingStartRaceHasStarted = false
  rollingStartReleaseTimer = 0.0
  rollingStartSessionTime = 0.0
  rollingStartLastFrame = now

  for _, carIndex in ipairs(rollingStartQueue) do
    local state = rollingStartCars[carIndex]
    if state and carIndex ~= 0 then
      local car = getCar(carIndex)
      state.lastProgress = car and car.splinePosition or state.lastProgress
      HoldRollingCar(carIndex)
    end
  end

  Log(sFormat("ROLLING: formation beginning; %d cars in %d classes.", #rollingStartQueue, #rollingStartGroups))
end

local function MaintainRollingFormation(dt, trackLength)
  rollingStartSessionTime = rollingStartSessionTime + dt

  -- The release clock only starts once the field is actually moving. Until then each class's row-0
  -- leader is already released (threshold 0), so the overall leader always gets things going.
  if rollingStartSessionTime > 0.5 and not rollingStartRaceHasStarted then
    for carIndex = 0, driverCount - 1 do
      local car = getCar(carIndex)
      if car and (car.speedKmh or 0) > 2.0 then
        rollingStartRaceHasStarted = true
        break
      end
    end
  end

  if rollingStartRaceHasStarted then
    rollingStartReleaseTimer = rollingStartReleaseTimer + dt
  end

  local maxLaneMove = ROLLING_START_LANE_RAMP_MPS * dt
  local greenLaneMove = ROLLING_START_GREEN_OFFSET_RAMP_MPS * dt

  -- Class-relative locked-position maps used by the phase-two (double file) spacing.
  for _, group in ipairs(rollingStartGroups or {}) do
    local map = {}
    for _, carIndex in ipairs(group.cars) do
      local state = rollingStartCars[carIndex]
      if state and state.enteredPhase2 then
        local pos = state.lockedPhase2Position or state.classPosition
        if pos and not map[pos] then map[pos] = carIndex end
      end
    end
    group.positionToCar = map
  end

  for _, carIndex in ipairs(rollingStartQueue) do
    local state = rollingStartCars[carIndex]
    if state then
      local car = getCar(carIndex)
      local isAI = (carIndex ~= 0)
      local group = rollingStartGroups[state.groupIndex]

      if not state.released and IsRollingCarReleased(state) then
        ReleaseRollingCar(carIndex)
      end
      local released = state.released

      if not released then
        if isAI then
          setAITopSpeed(carIndex, 0.0)
          setAIThrottleLimit(carIndex, 0.0)
          setAICaution(carIndex, 1.0)
          physics.setAIStopCounter(carIndex, ROLLING_START_STOP_COUNTER_HELD)
        end
      else
        -- Track the start-line crossing so a car only double-files on its flying lap.
        if car and type(car.splinePosition) == "number" then
          if not state.hasRolledOver and car.splinePosition < ROLLING_START_ROLLED_OVER_SPLINE then
            state.hasRolledOver = true
          end

          if state.hasRolledOver and car.splinePosition >= ROLLING_START_FORMATION_POINT
              and not state.enteredPhase2 then
            state.enteredPhase2 = true
            state.phase2At = rollingStartSessionTime
            local pos = state.classPosition
            if group then
              for _, memberIndex in ipairs(group.cars) do
                local member = rollingStartCars[memberIndex]
                if member and member ~= state and member.enteredPhase2
                    and member.lockedPhase2Position == pos then
                  pos = state.classPosition + 0.5
                  break
                end
              end
            end
            state.lockedPhase2Position = pos
          end
        end

        if state.green then
          -- Green transition: ease the lane offset back to the racing line.
          local applied = state.currentSplineOffset or 0.0
          if math.abs(applied) > 0.001 then
            local diff = -applied
            if math.abs(diff) > greenLaneMove then
              applied = applied + (diff > 0 and greenLaneMove or -greenLaneMove)
            else
              applied = 0.0
            end
            state.currentSplineOffset = applied
            if isAI then SetRollingLaneOffset(carIndex, applied) end
          end
        elseif isAI then
          local targetSpeed, targetOffset, cautionLevel, throttleLimit

          if state.enteredPhase2 then
            targetSpeed = ROLLING_START_SPEED_KMH
            cautionLevel = 0.7
            targetOffset = GetRollingLaneOffset(state.gridPosition) + GetOrganicLaneOffset(carIndex)
            local speed2, throttle2 = ApplyRollingPhaseTwoSpacing(state, group, trackLength)
            targetSpeed = speed2
            throttleLimit = throttle2
          else
            targetSpeed = ROLLING_START_FORMATION_SPEED_KMH
            cautionLevel = 2.0
            targetOffset = 0.0
            throttleLimit = 1.0
          end

          local applied = state.currentSplineOffset or 0.0
          local diff = targetOffset - applied
          if math.abs(diff) > maxLaneMove then
            applied = applied + (diff > 0 and maxLaneMove or -maxLaneMove)
          else
            applied = targetOffset
          end
          state.currentSplineOffset = applied

          setAITopSpeed(carIndex, targetSpeed)
          setAICaution(carIndex, cautionLevel)
          setAIThrottleLimit(carIndex, throttleLimit)
          setAISplineAbsoluteOffset(carIndex, applied, false)
          physics.setAIStopCounter(carIndex, 0.0)
        end
      end
    end
  end

  -- Per-class green transition: ramp caution from 0 to 1 over the reference window.
  for _, group in ipairs(rollingStartGroups or {}) do
    if group.green and (group.windowCloseTimer or 0) <= ROLLING_START_GREEN_WINDOW_SECONDS + 0.1 then
      group.windowCloseTimer = (group.windowCloseTimer or 0) + dt
      local progress = math.min(1.0, group.windowCloseTimer / ROLLING_START_GREEN_WINDOW_SECONDS)
      for _, memberIndex in ipairs(group.cars) do
        if memberIndex ~= 0 then
          setAICaution(memberIndex, progress)
        end
      end
    end
  end

  -- One-shot probe: confirm which world side a commanded offset actually places a car on. Once two
  -- phase-2 cars of opposite grid parity have settled lanes, log their offsets next to their real
  -- lateral position relative to the player (positive = player's left side).
  if not rollingStartLaneDiagDone then
    local player = getCar(0)
    local psx, psy, psz = GetCarLeftAxis(player)
    if player and psx then
      local oddState, evenState
      for _, carIndex in ipairs(rollingStartQueue) do
        local state = rollingStartCars[carIndex]
        if state and state.enteredPhase2 and state.gridPosition then
          if state.gridPosition % 2 == 1 and not oddState then
            oddState = state
          elseif state.gridPosition % 2 == 0 and not evenState then
            evenState = state
          end
        end
      end
      if oddState and evenState
          and math.abs(oddState.currentSplineOffset or 0) > 1.0
          and math.abs(evenState.currentSplineOffset or 0) > 1.0 then
        local function LatVsPlayer(state)
          local car = getCar(state.carIndex)
          local pp = car and car.position
          if not pp then return 0 end
          local dx = pp.x - player.position.x
          local dy = pp.y - player.position.y
          local dz = pp.z - player.position.z
          return dx * psx + dy * psy + dz * psz
        end
        Log(sFormat("ROLLING: lane diag oddGrid=%d oddOff=%.2f oddLat=%.2f | evenGrid=%d evenOff=%.2f evenLat=%.2f",
            oddState.gridPosition, oddState.currentSplineOffset or 0, LatVsPlayer(oddState),
            evenState.gridPosition, evenState.currentSplineOffset or 0, LatVsPlayer(evenState)))
        rollingStartLaneDiagDone = true
      end
    end
  end

  -- Periodic diagnostic so the formation can be verified from the log.
  rollingStartDiagFrame = (rollingStartDiagFrame or 0) + 1
  if rollingStartDiagFrame % 30 == 0 then
    for _, carIndex in ipairs(rollingStartQueue) do
      local state = rollingStartCars[carIndex]
      local car = getCar(carIndex)
      if state and car then
        local group = rollingStartGroups and rollingStartGroups[state.groupIndex] or nil
        Log(sFormat("ROLLING: car %d cls=%s cp=%d p=%.3f v=%.1f phase=%d off=%.2f rel=%s green=%s wrap=%d",
            carIndex, (group and group.label) or "?", state.classPosition or -1,
            NormalizeTrackProgress(car.splinePosition), car.speedKmh or -1,
            state.enteredPhase2 and 2 or 1, state.currentSplineOffset or 0,
            tostring(state.released), tostring(state.green), state.wrapCount or 0))
      end
    end
  end
end

local function GiveRollingGreen(carIndex, state, now)
  if state.green then return end

  state.green = true
  state.greenAt = now
  -- Hand the AI back its racing controls now; the lane offset eases out over the next moments.
  if carIndex ~= 0 then
    RestoreAIControl(carIndex, false)
  end
  Log(sFormat("ROLLING: car %d green at progress %.3f", carIndex, state.lastProgress or -1))
  if carIndex == 0 then
    rollingStartStatus = "GREEN — GO"
    ui.toast(ui.Icons.Play, "Rolling start: green light")
  end
end

local function UpdateRollingStart(sim)
  if rollingStartState == "idle" then return end
  if not IsRaceMode(sim) then
    RestoreRollingStartControls()
    rollingStartState = "idle"
    rollingStartStatus = ""
    return
  end
  if rollingStartSessionKey and GetCurrentSessionKey(sim) ~= rollingStartSessionKey then
    RestoreRollingStartControls()
    rollingStartState = "idle"
    rollingStartStatus = ""
    return
  end
  if rollingStartState == "unavailable" or rollingStartState == "complete" or rollingStartState == "finished" then return end

  local now = type(sim.time) == "number" and sim.time / 1000 or 0

  if rollingStartState == "staged" then
    -- Cars stay on AC's real grid until the race actually starts. Nothing is teleported.
    if sim.isSessionStarted ~= true then return end

    local startTrackLength = sim.trackLengthM
    if not IsFinite(startTrackLength) or startTrackLength <= ROLLING_START_MIN_TRACK_LENGTH_M then
      MarkRollingStartUnavailable("Track length became unavailable at race start.")
      return
    end

    rollingStartState = "rolling"
    rollingStartStatus = "SINGLE FILE — FORMATION"
    BeginRollingStart(now)
    Log("ROLLING: race start detected; formation beginning.")
  elseif rollingStartState == "rolling" and sim.isSessionStarted ~= true then
    RestoreRollingStartControls()
    rollingStartState = "finished"
    rollingStartStatus = ""
    Log("ROLLING: session ended before every car received green; controls restored.")
    return
  end

  if rollingStartState ~= "rolling" then return end

  local trackLength = sim.trackLengthM
  if not IsFinite(trackLength) or trackLength <= ROLLING_START_MIN_TRACK_LENGTH_M then
    MarkRollingStartUnavailable("Track length became unavailable during formation.")
    return
  end

  local dt = now - (rollingStartLastFrame or now)
  if dt < 0 then dt = 0 end
  if dt > 0.25 then dt = 0.25 end
  rollingStartLastFrame = now

  MaintainRollingFormation(dt, trackLength)

  -- Detect each car's line crossings. The first crossing (just off the grid) merely starts the
  -- formation lap; the second, after a full lap, is the real race start. A whole class goes green
  -- the moment its first car makes that second crossing (per-class green).
  for _, carIndex in ipairs(rollingStartQueue) do
    local state = rollingStartCars[carIndex]
    if state and not state.green then
      local car = getCar(carIndex)
      if car and type(car.splinePosition) == "number" and type(state.lastProgress) == "number" then
        local progress = NormalizeTrackProgress(car.splinePosition)
        if math.abs(progress - state.lastProgress) > 0.5 then
          state.wrapCount = (state.wrapCount or 0) + 1
          if state.wrapCount >= 2 then
            local group = rollingStartGroups[state.groupIndex]
            if group and not group.green then
              group.green = true
              group.greenAt = now
              group.windowCloseTimer = 0
              for _, memberIndex in ipairs(group.cars) do
                local memberState = rollingStartCars[memberIndex]
                if memberState and not memberState.green then
                  GiveRollingGreen(memberIndex, memberState, now)
                end
              end
            elseif not group then
              GiveRollingGreen(carIndex, state, now)
            end
          end
        end
        state.lastProgress = progress
      end
    end
  end

  -- The procedure is complete once every class has gone green.
  local allGreen = true
  for _, group in ipairs(rollingStartGroups or {}) do
    if not group.green then
      allGreen = false
      break
    end
  end

  if allGreen then
    -- Make sure no car is left holding a lane offset now that the whole field is racing.
    for carIndex, state in pairs(rollingStartCars) do
      if carIndex ~= 0 then
        SetRollingLaneOffset(carIndex, 0.0)
      end
      state.currentSplineOffset = 0.0
    end
    rollingStartState = "complete"
    rollingStartStatus = "GREEN — GO"
    raceDisplayPending = nil
    raceDisplayStable = nil
    Log("ROLLING: every class received green.")
    return
  end

  local playerState = rollingStartCars[0]
  if playerState and not playerState.green then
    rollingStartStatus = playerState.enteredPhase2 and "DOUBLE FILE — FORMATION" or "SINGLE FILE — FORMATION"
  end
end

local function LeaderboardProgress(carIndex)
  local car = getCar(carIndex)
  if not car then return 0 end
  return (car.sessionLapCount or 0) + (car.splinePosition or 0)
end

local function GetCarBestLapMs(idx)
  local ok, car = pcall(getCar, idx)
  if ok and car then
    local okBt, bt = pcall(function() return car.bestLapTimeMs end)
    if okBt and type(bt) == "number" and bt > 0 then return bt end
  end
  return nil
end

local function GetCurrentLapInfo()
  local sim = ac.getSim()
  local playerCar = getCar(0)

  local totalLaps = 0
  local session = ac.getSession(sim.currentSessionIndex)
  if session then
    totalLaps = session.laps or 0
  end
  local lapRaw = 1
  if playerCar then
    lapRaw = playerCar.lapCount + 1
  end
  -- Display AC's natural lap count: the formation lap is lap 1 and the race begins on lap 2. The
  -- rolling-start lap offset is applied only to the exported classification, never to the display.
  local currentLap = totalLaps > 0 and math.min(totalLaps, lapRaw) or lapRaw

  local speed = playerCar and (playerCar.speedKmh or 0) or 0
  if playerStoppedState then
    if speed > 8.0 then playerStoppedState = false end
  else
    if speed < 2.0 then playerStoppedState = true end
  end

  return currentLap, totalLaps, playerStoppedState
end

local function GetPlayerPositions()
  local sim = ac.getSim()
  local trackLength = sim.trackLengthM
  local playerCar = getCar(0)

  local currentLap, totalLaps, stopped = GetCurrentLapInfo()

  local overallPos = 1
  local classPos = 1
  local classTotal = 0
  local totalCars = 0
  local gapFront = nil
  local gapBehind = nil

  local playerClass = displayClass[0]

  local allCars = {}
  local classCars = {}

  for i = 0, driverCount - 1 do
    local car = getCar(i)
    if car then
      local prog = LeaderboardProgress(i)
      totalCars = totalCars + 1
      allCars[#allCars + 1] = { idx = i, prog = prog }

      -- playerClass can be nil when no class could be resolved; without this guard
      -- 'nil == nil' would match every car and inflate classTotal up to totalCars.
      if playerClass and displayClass[i] == playerClass then
        classTotal = classTotal + 1
        classCars[#classCars + 1] = { idx = i, prog = prog }
      end
    end
  end

  -- progSorter tolerates a missing prog (nil) so a sort can never abort clLeaderboard.
  -- Mirrors bestSorter: cars with a value sort ahead of those without, ties broken by idx.
  local function progSorter(a, b)
    if a.prog and b.prog then return a.prog > b.prog end
    if a.prog then return true end
    if b.prog then return false end
    return a.idx < b.idx
  end
  table.sort(allCars, progSorter)
  table.sort(classCars, progSorter)

  for i, car in ipairs(allCars) do
    if car.idx == 0 then
      overallPos = i
      break
    end
  end

  for i, car in ipairs(classCars) do
    if car.idx == 0 then
      classPos = i
      if i > 1 then
        local g = ac.getGapBetweenCars(0, classCars[i - 1].idx)
        if IsFinite(g) then gapFront = g end
      end
      if i < #classCars then
        local g = ac.getGapBetweenCars(0, classCars[i + 1].idx)
        if IsFinite(g) then gapBehind = g end
      end
      break
    end
  end

  if not sim.isSessionStarted then
    gapFront = nil
    gapBehind = nil
  end

  if stopped then
    gapFront = nil
    gapBehind = nil
  end

  -- CSP computes car.racePosition every frame; ac.getCarLeaderboardPosition() is the
  -- legacy Python leaderboard and returns a frozen classification, so it must not be used here.
  local function EnginePos(idx)
    local frozenPosition = finishedRacePositions[idx]
    if frozenPosition and frozenPosition > 0 then
      return frozenPosition
    end

    local ok, p = pcall(function() return getCar(idx).racePosition end)
    if ok and type(p) == "number" and p > 0 then
      return p
    end
    return nil
  end

  local enginePos = EnginePos(0)
  if enginePos then
    local pendingPos = raceDisplayPending
    if pendingPos and pendingPos.value == enginePos then
      pendingPos.frames = pendingPos.frames + 1
    else
      pendingPos = { value = enginePos, frames = 1 }
      raceDisplayPending = pendingPos
    end

    local stable = enginePos
    if pendingPos.frames >= RACE_POS_STABLE_FRAMES then
      raceDisplayStable = enginePos
    elseif raceDisplayStable and raceDisplayStable > 0 then
      stable = raceDisplayStable
    end

    overallPos = stable
    classPos = 1
    for _, car in ipairs(classCars) do
      if car.idx ~= 0 then
        local rp = EnginePos(car.idx)
        if rp and rp < stable then
          classPos = classPos + 1
        end
      end
    end
  else
    -- Engine has no classification for this car (pre-start, pits, retired): keep showing
    -- the last known good position instead of snapping back to a progress-derived guess.
    if raceDisplayStable and raceDisplayStable > 0 then
      overallPos = raceDisplayStable
    end
  end

  return overallPos, totalCars, classPos, classTotal, gapFront, gapBehind, currentLap, totalLaps, stopped
end

local function GetPlayerPositionsQuali()
  local playerClass = displayClass[0]

  local allCars = {}
  local classCars = {}
  local totalCars = 0
  local classTotal = 0

  for i = 0, driverCount - 1 do
    local car = getCar(i)
    if car then
      local best = GetCarBestLapMs(i)
      totalCars = totalCars + 1
      allCars[#allCars + 1] = { idx = i, best = best }
      if playerClass and displayClass[i] == playerClass then
        classTotal = classTotal + 1
        classCars[#classCars + 1] = { idx = i, best = best }
      end
    end
  end

  local function bestSorter(a, b)
    if a.best and b.best then return a.best < b.best end
    if a.best then return true end
    if b.best then return false end
    return a.idx < b.idx
  end
  table.sort(allCars, bestSorter)
  table.sort(classCars, bestSorter)

  local overallPos = 1
  for i, c in ipairs(allCars) do
    if c.idx == 0 then
      overallPos = i
      break
    end
  end
  local classPos = 1
  for i, c in ipairs(classCars) do
    if c.idx == 0 then
      classPos = i
      break
    end
  end

  local classFastestMs = nil
  for _, c in ipairs(classCars) do
    if c.best then
      classFastestMs = c.best
      break
    end
  end

  local playerBestMs = GetCarBestLapMs(0)

  return overallPos, totalCars, classPos, classTotal, classFastestMs, playerBestMs
end

local hasDWrite = (type(ui.pushDWriteFont) == "function" and type(ui.dwriteText) == "function")

local function MeasureBoldText(text, fontSize)
  if hasDWrite then
    ui.pushDWriteFont("Segoe UI;Weight=Bold")
    local s = ui.measureDWriteText(text, fontSize)
    ui.popDWriteFont()
    return s
  end
  return ui.measureText(text)
end

local function DrawBoldText(text, centerX, centerY, fontSize, color)
  if not hasDWrite then
    local size = ui.measureText(text)
    ui.setCursor(vec2(centerX - size.x / 2, centerY - size.y / 2))
    if color then
      ui.textColored(text, color)
    else
      ui.text(text)
    end
    return size.y
  end
  ui.pushDWriteFont("Segoe UI;Weight=Bold")
  local size = ui.measureDWriteText(text, fontSize)
  ui.setCursor(vec2(centerX - size.x / 2, centerY - size.y / 2))
  if color then
    ui.dwriteText(text, fontSize, color)
  else
    ui.dwriteText(text, fontSize)
  end
  ui.popDWriteFont()
  return size.y
end

-- The lane the human car should hold during the formation, from its starting grid slot parity and
-- the current pole-side setting. nil when the slot is unknown.
local function GetPlayerLaneSide()
  local pos = rollingStartGridPositions[0]
  if type(pos) ~= "number" or pos <= 0 then return nil end
  local isOdd = (pos % 2 == 1)
  if rollingStartPoleOnRight then
    return isOdd and "RIGHT" or "LEFT"
  end
  return isOdd and "LEFT" or "RIGHT"
end

local function GetRollingStartDisplay(sim)
  if rollingStartState == "staged" then
    return "ROLLING START READY", rgbm(1.0, 0.75, 0.25, 1)
  end

  if rollingStartState == "complete" then
    local player = rollingStartCars[0]
    if player and player.greenAt and type(sim.time) == "number" and sim.time / 1000 - player.greenAt <= 4 then
      return "GREEN — GO", rgbm(0.25, 1.0, 0.35, 1)
    end
    return nil, nil
  end

  if rollingStartState == "unavailable" then
    return "ROLLING START UNAVAILABLE", rgbm(1.0, 0.55, 0.25, 1)
  end

  if rollingStartState == "rolling" then
    local player = rollingStartCars[0]
    if player and player.green then
      if player.greenAt and type(sim.time) == "number" and sim.time / 1000 - player.greenAt <= 4 then
        return "GREEN — GO", rgbm(0.25, 1.0, 0.35, 1)
      end
      return nil, nil
    end
    local phaseText = player.enteredPhase2 and "DOUBLE FILE" or "SINGLE FILE"
    local lane = GetPlayerLaneSide()
    if lane then
      return sFormat("%s — YOUR LANE: %s", phaseText, lane), rgbm(1.0, 0.75, 0.25, 1)
    end
    return phaseText .. " — FORMATION", rgbm(1.0, 0.75, 0.25, 1)
  end

  return nil, nil
end

function script.clLeaderboard(dt)
  local sim = ac.getSim()
  local w = ui.windowWidth()
  local h = ui.windowHeight()

  local padX = 12
  local gap = 16
  local panelW = (w - 2 * padX - 2 * gap) / 3
  local panelTop = 8
  local gapPanelH = 46
  local gapPanelTop = h - 8 - gapPanelH
  local columnBottom = gapPanelTop - 10
  local panelH = columnBottom - panelTop

  local qualiMode = IsQualiPracticeMode()
  local overallPos, totalCars, classPos, classTotal, gapFront, gapBehind, currentLap, totalLaps, stopped
  local classFastestMs, playerBestMs = nil, nil
  local rollingText, rollingColor = GetRollingStartDisplay(sim)
  if qualiMode then
    overallPos, totalCars, classPos, classTotal, classFastestMs, playerBestMs = GetPlayerPositionsQuali()
    currentLap, totalLaps, stopped = GetCurrentLapInfo()
    gapFront, gapBehind = nil, nil
  else
    overallPos, totalCars, classPos, classTotal, gapFront, gapBehind, currentLap, totalLaps, stopped = GetPlayerPositions()
  end

  -- During the rolling formation the engine classification is not meaningful yet, so hold the
  -- positions until every class has gone green (rollingStartState == "complete").
  local rollingActive = (rollingStartState == "staged" or rollingStartState == "rolling")
  if rollingActive then
    overallPos, classPos, gapFront, gapBehind = nil, nil, nil, nil
  end

  local function DrawPanel(x, title, bigText, totalText, color)
    local cx = x + panelW / 2

    ui.drawRectFilled(vec2(x, panelTop), vec2(x + panelW, columnBottom), rgbm(0.08, 0.10, 0.15, 0.6), 8)
    ui.drawRect(vec2(x, panelTop), vec2(x + panelW, columnBottom), rgbm(0.3, 0.3, 0.36, 0.7), 8, nil, 1.5)

    DrawBoldText(title, cx, panelTop + 8 + MeasureBoldText(title, 13).y / 2, 13, rgbm(0.7, 0.7, 0.8, 1))

    local bigCy = panelTop + panelH * 0.42
    local bigSize = math.max(12, math.floor(panelH * 0.5))
    local bigW = MeasureBoldText(bigText, bigSize).x
    if bigW > panelW * 0.9 then
      bigSize = math.max(12, math.floor(bigSize * (panelW * 0.9) / bigW))
    end
    local bigH = MeasureBoldText(bigText, bigSize).y
    local bigMaxHalf = panelH * 0.22
    if bigH / 2 > bigMaxHalf then
      bigSize = math.max(12, math.floor(bigSize * bigMaxHalf / (bigH / 2)))
    end
    local drawnBigH = DrawBoldText(bigText, cx, bigCy, bigSize, color)

    local sepW = panelW * 0.5
    local sepY = bigCy + drawnBigH / 2 + 10
    ui.drawRectFilled(vec2(cx - sepW / 2, sepY), vec2(cx + sepW / 2, sepY + 2), rgbm(0.5, 0.5, 0.6, 0.9))

    if totalText then
      local totalSize = MeasureBoldText(totalText, 16)
      local totalCy = math.min(sepY + 8 + totalSize.y / 2, columnBottom - totalSize.y / 2 - 8)
      DrawBoldText(totalText, cx, totalCy, 16, rgbm(0.7, 0.7, 0.8, 1))
    end
  end

  local lapTotalText = (totalLaps or 0) > 0 and sFormat("%d", totalLaps) or nil
  local overallText = overallPos and sFormat("%d", overallPos) or "-"
  local classText = classPos and sFormat("%d", classPos) or "-"
  DrawPanel(padX, "LAP", sFormat("%d", math.max(1, currentLap)), lapTotalText, rgbm(0.95, 0.95, 1, 1))
  DrawPanel(padX + panelW + gap, "OVERALL", overallText, sFormat("%d", totalCars), rgbm(0.95, 0.95, 1, 1))
  if classTotal > 0 then
    DrawPanel(padX + 2 * (panelW + gap), "CLASS", classText, sFormat("%d", classTotal), rgbm(0.35, 0.95, 0.45, 1))
  else
    DrawPanel(padX + 2 * (panelW + gap), "CLASS", "-", "add a class", rgbm(0.55, 0.55, 0.6, 1))
  end

  if rollingText then
    local gapPanelRight = w - padX
    ui.drawRectFilled(vec2(padX, gapPanelTop), vec2(gapPanelRight, h - 8), rgbm(0.08, 0.10, 0.15, 0.6), 8)
    ui.drawRect(vec2(padX, gapPanelTop), vec2(gapPanelRight, h - 8), rgbm(0.3, 0.3, 0.36, 0.7), 8, nil, 1.5)
    local size = MeasureBoldText(rollingText, 17)
    local fontSize = size.x > (w - 2 * padX - 24)
        and math.max(12, math.floor(17 * (w - 2 * padX - 24) / size.x))
        or 17
    DrawBoldText(rollingText, w / 2, gapPanelTop + gapPanelH / 2, fontSize, rollingColor)
  elseif qualiMode then
    local posY = gapPanelTop + gapPanelH / 2
    local gapPanelRight = w - padX
    ui.drawRectFilled(vec2(padX, gapPanelTop), vec2(gapPanelRight, h - 8), rgbm(0.08, 0.10, 0.15, 0.6), 8)
    ui.drawRect(vec2(padX, gapPanelTop), vec2(gapPanelRight, h - 8), rgbm(0.3, 0.3, 0.36, 0.7), 8, nil, 1.5)

    local fastLap = classFastestMs and FormatLapMs(classFastestMs) or "--:--.---"
    local myLap = playerBestMs and FormatLapMs(playerBestMs) or "--:--.---"

    local maxW = w - 2 * padX - 24
    local gFs = 15
    local fastRow = "FASTEST  " .. fastLap
    local myRow = "YOU      " .. myLap
    local need = math.max(MeasureBoldText(fastRow, gFs).x, MeasureBoldText(myRow, gFs).x)
    if need > maxW then
      gFs = math.max(12, math.floor(gFs * maxW / need))
    end

    DrawBoldText(fastRow, w / 2, posY - (math.floor(gFs / 2) + 1), gFs, rgbm(0.35, 0.95, 0.45, 1))
    DrawBoldText(myRow, w / 2, posY + (math.floor(gFs / 2) + 1), gFs, rgbm(0.95, 0.95, 1, 1))
  elseif classTotal > 1 then
    local gapStr = "--"
    local gapColor = rgbm(0.55, 0.55, 0.6, 0.9)
    if sim.isSessionStarted then
      if stopped then
        gapStr = "Car Stopped"
        gapColor = rgbm(1.0, 0.7, 0.35, 1)
      elseif gapFront and gapBehind then
        gapStr = sFormat("+%.1fs / -%.1fs", math.abs(gapFront), math.abs(gapBehind))
      elseif gapFront then
        gapStr = sFormat("+%.1fs", math.abs(gapFront))
      elseif gapBehind then
        gapStr = sFormat("-%.1fs", math.abs(gapBehind))
      end
    end

    local gapPanelRight = w - padX
    ui.drawRectFilled(vec2(padX, gapPanelTop), vec2(gapPanelRight, h - 8), rgbm(0.08, 0.10, 0.15, 0.6), 8)
    ui.drawRect(vec2(padX, gapPanelTop), vec2(gapPanelRight, h - 8), rgbm(0.3, 0.3, 0.36, 0.7), 8, nil, 1.5)

    local gFs = 20
    local gSize = MeasureBoldText(gapStr, gFs)
    if gSize.x > (w - 2 * padX - 24) then
      gFs = math.max(12, math.floor(gFs * (w - 2 * padX - 24) / gSize.x))
    end
    DrawBoldText(gapStr, w / 2, gapPanelTop + gapPanelH / 2, gFs, gapColor)
  end
end

function script.update(dt)
  local sim = ac.getSim()
  driverCount = sim.carsCount or 0

  -- Log the incoming state before any export-side API calls. If the transition handler
  -- encounters an AC/CSP state object that is no longer valid after cars disappear, this
  -- entry still records the zero-car state that led to the failure.
  local entryOk, entryLine = pcall(function()
    local sessionName = ""
    local okName, name = pcall(function() return ac.getSessionName(sim.currentSessionIndex) end)
    if okName and type(name) == "string" then sessionName = name end

    local sessionType, sessionOver = nil, nil
    if ac.getSession then
      local okSession, session = pcall(ac.getSession, sim.currentSessionIndex)
      if okSession and session then
        local okType, value = pcall(function() return session.type end)
        if okType then sessionType = value end
        local okOver, over = pcall(function() return session.isOver end)
        if okOver then sessionOver = over end
      end
    end

    local resultsVisible = nil
    local okResults, results = pcall(function() return sim.isLookingAtSessionResults end)
    if okResults then resultsVisible = results end
    local sessionTimeLeft = nil
    local okTimeLeft, timeLeft = pcall(function() return sim.sessionTimeLeft end)
    if okTimeLeft and type(timeLeft) == "number" then sessionTimeLeft = timeLeft end
    local sessionTimeBucket = sessionTimeLeft and math.floor(sessionTimeLeft / 1000) or nil
    local playerInPit = IsPlayerInPit()

    return sFormat(
      "EXPORT: update-entry started=%s prevStarted=%s index=%s prevIndex=%s cars=%d " ..
      "name='%s' sessionType=%s raceType=%s leftSec=%s playerInPit=%s over=%s results=%s",
      tostring(sim.isSessionStarted), tostring(lastSessionStarted),
      tostring(sim.currentSessionIndex), tostring(lastSessionIndex), driverCount,
      sessionName, tostring(sessionType), tostring(sim.raceSessionType),
      tostring(sessionTimeBucket), tostring(playerInPit),
      tostring(sessionOver), tostring(resultsVisible))
  end)
  if entryOk and entryLine ~= lastExportUpdateDiagnostic then
    lastExportUpdateDiagnostic = entryLine
    Log(entryLine)
  elseif not entryOk then
    Log("EXPORT: update-entry diagnostics failed: " .. tostring(entryLine))
  end

  -- AC's in-place "Restart Session" reuses the same session index/name and does not fire
  -- ac.onSessionStart, so detect the true -> false edge of isSessionStarted ourselves and arm a
  -- fresh re-init for the coming pre-start countdown. This is read before the export frame so
  -- lastSessionStarted still holds the previous frame's value.
  if lastSessionStarted == true and sim.isSessionStarted ~= true then
    if rollingStartState ~= "idle" then
      RestoreRollingStartControls(false)
      rollingStartState = "idle"
      rollingStartStatus = ""
    end
    rollingPendingReinit = true
  end
  if sim.isSessionStarted == true then
    rollingPendingReinit = false
  end
  if rollingPendingReinit and driverCount > 0 then
    rollingPendingReinit = false
    firstFrame = true
    gridReordered = false
    classReloadAttempted = false
  end

  local updateOk, sessionChanged, sessionEnded, sessionBegan, sessionSwitched =
      pcall(UpdateResultExportFrame, sim)
  if not updateOk then
    local message = tostring(sessionChanged)
    if message ~= lastExportTransitionError then
      Log("EXPORT: transition handler failed: " .. message)
      lastExportTransitionError = message
    end
    sessionChanged = false
  else
    lastExportTransitionError = nil
  end
  if sessionSwitched and rollingStartState ~= "idle" then
    RestoreRollingStartControls(false)
    rollingStartState = "idle"
    rollingStartStatus = ""
  end
  if sessionChanged then
    firstFrame = true
    carClassCache = {}
    uiSourceCache = {}
  end
  local valid, validationError = CheckSessionValidity()
  if firstFrame then
    -- Cleared before the work runs: if anything below throws, the flag stays false so a
    -- single failure cannot turn into a per-frame infinite loop.
    firstFrame = false
    Log(sFormat("FIRSTFRAME: idx=%s race=%s dc=%d rc=%d", tostring(sim.currentSessionIndex), tostring(IsRaceMode()), driverCount, #releaseClasses))
    leaderboardOpened = false
    playerStoppedState = false
    allDriversStartingPos = {}
    rollingStartGridPositions = {}
    if driverCount > 0 then
      for i = 0, driverCount - 1 do
        local okID, carID = pcall(function() return ac.getCarID and ac.getCarID(i) end)
        if not okID then carID = nil end
        Log(sFormat("GRID: i=%d carID=%s name=%s", i, tostring(carID), tostring(ac.getCarName and ac.getCarName(i))))
      end
      local okLoad, loadErr = pcall(LoadManualClassesFromStorage)
      if not okLoad then
        Log("FIRSTFRAME: LoadManualClassesFromStorage FAILED err=" .. tostring(loadErr))
      end
      if IsRaceMode(sim) then
        RecordStartingPositions()
        DetectRollingStartPoleSide()
        ReorderGridByClass()
        gridReordered = true
      end
    end
    Log(sFormat("FIRSTFRAME DONE: rc=%d classes={%s}", #releaseClasses, table.concat(releaseClasses, ",")))
  end

  -- Attempts at most once per session. Without this guard the block re-ran every frame
  -- whenever no class was assigned, reloading storage for the whole grid each time and
  -- starving the leaderboard.
  if not classReloadAttempted and #releaseClasses == 0 and driverCount > 0 then
    classReloadAttempted = true
    LoadManualClassesFromStorage()
    if #releaseClasses > 0 then
      BuildClassInfo()
      if IsRaceMode(sim) then
        RecordStartingPositions()
        DetectRollingStartPoleSide()
        ReorderGridByClass()
        gridReordered = true
      end
    end
  end

  if valid and IsRaceMode(sim) and rollingStartState == "idle" and driverCount > 0
      and not IsSessionFinished(sim) then
    if not gridReordered then
      RecordStartingPositions()
      DetectRollingStartPoleSide()
      ReorderGridByClass()
      gridReordered = true
    end
    local okRolling, rollingErr = pcall(PrepareRollingStart, sim)
    if not okRolling then
      MarkRollingStartUnavailable("Could not prepare the field: " .. tostring(rollingErr), true)
    end
  end

  if sim.isInMainMenu == true then
    ac.setWindowOpen("cl_config", true)
  end

  if not valid then
    if rollingStartState == "staged" or rollingStartState == "rolling" then
      MarkRollingStartUnavailable(
        "Rolling start stopped: " .. tostring(validationError or "this session is not supported."),
        false)
    end
    return
  end

  if not leaderboardOpened and sim.isInMainMenu ~= true then
    ac.setWindowOpen("cl_leaderboard", true)
    leaderboardOpened = true
  end

  local rollingOk, rollingErr = pcall(UpdateRollingStart, sim)
  if not rollingOk then
    MarkRollingStartUnavailable("Rolling start stopped safely: " .. tostring(rollingErr), false)
  end
end

function script.clConfig()
  local valid, err = CheckSessionValidity()
  local sim = ac.getSim()
  local w = ui.windowWidth()
  local h = ui.windowHeight()

  local colX = 24

  ui.pushStyleVar(ui.StyleVar.FramePadding, vec2(6, 6))

  ui.drawRectFilled(vec2(0, 0), vec2(w, h), rgbm(0.04, 0.05, 0.07, 0.85), 0)
  ui.drawRect(vec2(0, 0), vec2(w, h), rgbm(0.18, 0.18, 0.20, 0.6), 0, nil, 1)

  ui.pushFont(ui.Font.Title)
  local titleText = "Multiple Class Race Configuration"
  local titleSize = ui.measureText(titleText)
  local titleX = (w - titleSize.x) / 2
  ui.setCursor(vec2(titleX, 12))
  ui.text(titleText)
  ui.popFont()

  local separatorY = ui.getCursor().y + 10
  ui.drawLine(vec2(colX, separatorY), vec2(w - 24, separatorY), rgbm(0.25, 0.25, 0.28, 0.5), 1)
  ui.setCursor(vec2(colX, separatorY + 12))

  -- Available in Race, Practice and Qualifying alike.
  if ui.checkbox("Auto-save session result when the session finishes", resultExportEnabled) then
    resultExportEnabled = not resultExportEnabled
    storedSettings.resultExportEnabled = resultExportEnabled
    if not resultExportEnabled then
      ResetResultExport()
    end
  end

  ui.setCursor(vec2(colX, ui.getCursor().y + 4))
  if ui.button("Choose result folder...", vec2(w - 48, 28)) then
    local dlgOk, dlgErr = pcall(function()
      if type(os.openFileDialog) ~= "function" then
        ui.toast(ui.Icons.Warning, "Folder dialog is not available on this CSP version.")
      else
        local defaultFolder = ac.getFolder and ac.getFolder(ac.FolderID.Documents) or ""
        os.openFileDialog({
          title = "Choose save folder for session results",
          defaultFolder = defaultFolder,
          folder = (resultFolder ~= "") and resultFolder or nil,
          flags = os.DialogFlags and bit.bor(os.DialogFlags.PickFolders, os.DialogFlags.PathMustExist) or nil
        }, function(err, path)
          if (not err or err == "") and path and path ~= "" then
            resultFolder = NormalizePath(path)
            storedSettings.resultFolder = resultFolder
            ui.toast(ui.Icons.Play, "Session result folder set")
          elseif err and err ~= "" then
            ui.toast(ui.Icons.Warning, "Error choosing folder: " .. err)
          end
        end)
      end
    end)
    if not dlgOk then
      ui.toast(ui.Icons.Warning, "Error opening folder dialog: " .. tostring(dlgErr))
    end
  end

  ui.setCursor(vec2(colX, ui.getCursor().y + 6))
  ui.pushFont(ui.Font.Small)
  if resultFolder ~= "" then
    ui.textWrapped("Result folder: " .. resultFolder)
    ui.setCursor(vec2(colX, ui.getCursor().y + 2))
    if ui.button("Reset to Default", vec2(230, 22)) then
      resultFolder = ""
      storedSettings.resultFolder = ""
    end
  else
    local resolved = ResultEffectiveFolder()
    if resolved ~= "" then
      ui.textColored("Result folder: " .. resolved .. " (default)", rgbm(0.6, 0.6, 0.7, 1))
      if ui.itemHovered() then
        ui.setTooltip("Documents\\Assetto Corsa\\mcr-results, which is also the dashboard default. Click 'Choose result folder...' to change it.")
      end
    else
      ui.textColored("Result folder: not resolved - please choose a folder", rgbm(1.0, 0.7, 0.35, 1))
    end
  end

  ui.setCursor(vec2(colX, ui.getCursor().y + 2))
  ui.textWrapped("Results are split into practice, qualifying and race subfolders. Point the dashboard's 'Lua App Result Path' at the same folder.")
  ui.setCursor(vec2(colX, ui.getCursor().y + 2))
  if resultExportStatus ~= "" then
    ui.textWrapped(resultExportStatus)
  else
    ui.textColored("No session result saved yet.", rgbm(0.6, 0.6, 0.7, 1))
  end
  ui.popFont()
  ui.dummy(vec2(0, 8))

  if valid then
    if IsRaceMode(sim) then
      ui.setCursor(vec2(colX, ui.getCursor().y))
      ui.text("Rolling Start - lane for pole position (P1, P3, ...):")
      ui.setCursor(vec2(colX, ui.getCursor().y + 4))
      local laneButtonText = rollingStartPoleOnRight
          and "RIGHT LANE (P1, P3, ...)" or "LEFT LANE (P1, P3, ...)"
      if ui.button(laneButtonText, vec2(w - 48, 28)) then
        rollingStartPoleOnRight = not rollingStartPoleOnRight
        rollingStartPoleSideSource = "manual"
      end
      if ui.itemHovered() then
        ui.setTooltip("The lane taken by odd grid slots (P1, P3, ...) during the rolling start; even slots take the other lane.\n\nDefault is auto-detected from the side your car occupies on the starting grid. Click to override for this session.")
      end
      ui.setCursor(vec2(colX, ui.getCursor().y + 4))
      ui.pushFont(ui.Font.Small)
      local srcText = (rollingStartPoleSideSource == "manual")
          and "Overridden manually for this session."
          or "Auto-detected from your starting-grid position."
      ui.textColored(srcText, rgbm(0.6, 0.6, 0.7, 1))
      ui.popFont()
      ui.dummy(vec2(0, 8))
    end

    if ui.checkbox("Enable Multiple Class Race", enduranceEnabled) then
      enduranceEnabled = not enduranceEnabled
      storedSettings.enduranceEnabled = enduranceEnabled
    end

    if enduranceEnabled then

      if driverCount > 0 then
        ui.setCursor(vec2(colX, ui.getCursor().y))
        ui.text("Add Class (auto-assign by car tag):")
        ui.setCursor(vec2(colX, ui.getCursor().y + 4))
        ui.pushItemWidth(w - 48)
        classAddInput = ui.inputText("##NewReleaseClass", classAddInput or "")
        ui.popItemWidth()
        ui.setCursor(vec2(colX, ui.getCursor().y + 6))
        if ui.button("Add Class", vec2(w - 48, 28)) then
          local addOk, addErr = pcall(function()
            AddReleaseClass(classAddInput)
          end)
          if not addOk then
            classAddError = "Error adding class: " .. tostring(addErr)
          end
        end
        ui.setCursor(vec2(colX, ui.getCursor().y + 6))
        if ui.button("Verify", vec2(w - 48, 28)) then
          pcall(VerifyClassAssignments)
        end
        if ui.itemHovered() then
          ui.setTooltip("Use 'Add Class' first to assign cars by tag, then 'Verify' to check every car belongs to an added class.")
        end

        if classAddError then
          ui.setCursor(vec2(colX, ui.getCursor().y + 8))
          ui.pushFont(ui.Font.Small)
          ui.textColored(classAddError, rgbm.colors.red)
          ui.popFont()
        end

        if #releaseClasses > 0 then
          ui.setCursor(vec2(colX, ui.getCursor().y + 12))
          ui.text("Classes:")
          for ci = 1, #releaseClasses do
            local clsName = releaseClasses[ci]
            local carList = {}
            local count = 0
            for i = 0, driverCount - 1 do
              if manualDriverClass[i] == clsName then
                count = count + 1
                local cn = ac.getCarName and ac.getCarName(i) or sFormat("Car %d", i + 1)
                carList[#carList + 1] = cn
              end
            end
            ui.setCursor(vec2(colX + 8, ui.getCursor().y + 2))
            ui.text(sFormat("%s (%d car%s)", clsName, count, count == 1 and "" or "s"))
            if count > 0 then
              if ui.itemHovered() then
                ui.setTooltip("Cars: " .. table.concat(carList, ", "))
              end
            end
            ui.sameLine()
            ui.pushStyleVar(ui.StyleVar.FramePadding, vec2(6, 2))
            ui.pushStyleVar(ui.StyleVar.ButtonTextAlign, vec2(0.5, 0.5))
            if ui.button("Remove##removeclass" .. ci, vec2(64, 20)) then
              RemoveReleaseClass(ci)
            end
            ui.popStyleVar(2)
          end
        end

      else
        ui.setCursor(vec2(colX, ui.getCursor().y))
        ui.pushFont(ui.Font.Small)
        ui.textColored("Waiting for car data to load...", rgbm(0.6, 0.7, 0.9, 1))
        ui.popFont()
      end

    ui.dummy(vec2(0, 8))
    end

  else
    ui.setCursor(vec2(colX, ui.getCursor().y))
    ui.pushItemWidth(w - 48)
    ui.textWrapped(err or "Class-based mode cannot be enabled under current conditions!")
    ui.popItemWidth()
    ui.dummy(vec2(0, 8))
  end

  ui.popStyleVar()
end

ac.onSessionStart(function()
  local sim = ac.getSim()
  -- A new session always drops any field left over from the previous one, whatever state it was
  -- in (a restart can catch us mid staged/rolling).
  RestoreRollingStartControls(false)
  rollingStartState = "idle"
  rollingStartStatus = ""

  firstFrame          = true
  playerStoppedState  = false
  raceDisplayStable   = nil
  raceDisplayPending  = nil
  gridReordered       = false
  classReloadAttempted = false
  carClassCache       = {}
  allDriversStartingPos = {}
  uiSourceCache       = {}
  if liveSessionPending then
    resultResetAfterExport = true
  else
    ResetResultExport()
    resultResetAfterExport = false
  end
  lastExportDiagnostic = nil
  Log("onSessionStart: firstFrame=true")
end)

if type(ac.onRelease) == "function" then
  local okRelease, releaseErr = pcall(ac.onRelease, ExportExpiredSessionOnRelease)
  if not okRelease then
    Log("EXPORT: failed to register release fallback: " .. tostring(releaseErr))
  end

  local okRollingRelease, rollingReleaseErr = pcall(ac.onRelease, function()
    RestoreRollingStartControls(false)
  end)
  if not okRollingRelease then
    Log("ROLLING: failed to register cleanup callback: " .. tostring(rollingReleaseErr))
  end
end

Log("=== APP LOADED ===")
