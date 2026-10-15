-- Small stateless helpers shared by every module.
local C = require("src/constants")
local S = require("src/state")

local U = {}

local sFormat = string.format
local mMin = math.min

local function resolveLogPath()
  if type(ac.dirname) == "function" then
    return ac.dirname() .. "/debug.log"
  end
  if type(io.relative) == "function" then
    return io.relative("debug.log")
  end
  return "debug.log"
end

function U.Log(msg)
  if not C.DEBUG_ENABLED then return end
  pcall(function()
    if not S.debugLogFile then
      S.debugLogFile = io.open(resolveLogPath(), "a")
    end
    if S.debugLogFile then
      S.debugLogFile:write(msg .. "\n")
      S.debugLogFile:flush()
    end
  end)
end

-- Reads obj[key] without ever throwing. Returns nil when the read fails.
function U.SafeGet(obj, key)
  local ok, value = pcall(function() return obj[key] end)
  if ok then return value end
  return nil
end

-- Reads a numeric field. Returns nil unless the value is a number.
function U.SafeNumber(obj, key)
  local value = U.SafeGet(obj, key)
  if type(value) == "number" then return value end
  return nil
end

-- Reads a boolean field. Returns nil unless the value is a boolean.
function U.SafeBool(obj, key)
  local value = U.SafeGet(obj, key)
  if type(value) == "boolean" then return value end
  return nil
end

-- ac.getCarID wrapped for older CSP builds and mid-teardown grids. Returns nil on failure.
function U.SafeCarID(carIndex)
  local ok, id = pcall(function() return ac.getCarID and ac.getCarID(carIndex) end)
  if ok then return id end
  return nil
end

-- Trims surrounding whitespace and returns exactly one value.
function U.Trim(s)
  local trimmed = (s or ""):gsub("^%s+", ""):gsub("%s+$", "")
  return trimmed
end

-- Backslashes to forward slashes. Returns exactly one value.
function U.NormalizePath(p)
  if type(p) ~= "string" or p == "" then return p or "" end
  local normalized = p:gsub("\\", "/")
  return normalized
end

function U.FormatLapMs(ms)
  if type(ms) ~= "number" or ms <= 0 then return "--:--.---" end
  local minutes = math.floor(ms / 60000)
  local rest = ms % 60000
  local seconds = math.floor(rest / 1000)
  local milli = rest % 1000
  return sFormat("%02d:%02d.%03d", minutes, seconds, milli)
end

-- Control characters must be escaped too: a driver name containing a raw newline
-- or tab would otherwise produce invalid JSON.
function U.JsonEscape(s)
  s = tostring(s or "")
  s = s:gsub("\\", "\\\\"):gsub('"', '\\"')
  s = s:gsub("\n", "\\n"):gsub("\r", "\\r"):gsub("\t", "\\t")
  s = s:gsub("[\1-\31]", function(c) return sFormat("\\u%04x", string.byte(c)) end)
  return s
end

function U.EditDistance(a, b)
  local m, n = #a, #b
  if m == 0 then return n end
  if n == 0 then return m end
  local d = {}
  for i = 0, m do d[i * (n + 1)] = i end
  for j = 0, n do d[j] = j end
  for i = 1, m do
    local row = i * (n + 1)
    for j = 1, n do
      local cost = (a:sub(i, i) == b:sub(j, j)) and 0 or 1
      d[row + j] = mMin(d[row - (n + 1) + j] + 1, d[row + j - 1] + 1, d[row - (n + 1) + j - 1] + cost)
    end
  end
  return d[m * (n + 1) + n]
end

function U.IsFinite(v)
  return type(v) == "number" and v == v and math.abs(v) ~= math.huge
end

function U.NormalizeTrackProgress(progress)
  return progress - math.floor(progress)
end

return U
