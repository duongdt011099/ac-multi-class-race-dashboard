-- Persistence of per-car class assignments and the class name list in ac.storage.
local S = require("src/state")
local U = require("src/util")

local ClsStore = {}

local sFormat = string.format

-- Storage key resolution. Primary key is the car's AC ID; when that is unavailable we
-- fall back to the grid index so a class added in one session is not silently lost.
function ClsStore.carClassKey(carID, carIndex)
  if carID and carID ~= "" then
    return "carclass_" .. tostring(carID)
  end
  if carIndex ~= nil then
    return "carclass_idx_" .. tostring(carIndex)
  end
  return nil
end

function ClsStore.SaveCarClass(carID, cls, carIndex)
  local key = ClsStore.carClassKey(carID, carIndex)
  if not key then
    U.Log("SaveCarClass ABORT: no carID and no carIndex - value NOT persisted")
    return false
  end
  local ok = pcall(function()
    ac.storage[key] = cls or ""
  end)
  U.Log(sFormat("SaveCarClass: key=%s cls='%s' idx=%s ok=%s", key, tostring(cls), tostring(carIndex), tostring(ok)))
  return ok
end

function ClsStore.LoadCarClass(carID, carIndex)
  local key = ClsStore.carClassKey(carID, carIndex)
  if not key then
    U.Log("LoadCarClass ABORT: no carID and no carIndex")
    return nil
  end
  local ok, v = pcall(function()
    return ac.storage[key]
  end)
  if ok and v and v ~= "" then
    U.Log(sFormat("LoadCarClass: key=%s -> '%s' idx=%s", key, tostring(v), tostring(carIndex)))
    return v
  end
  U.Log(sFormat("LoadCarClass: key=%s -> (empty) idx=%s", key, tostring(carIndex)))
  return nil
end

function ClsStore.SaveClassNames()
  pcall(function()
    ac.storage["releaseClassNames"] = table.concat(S.releaseClasses, "|")
  end)
end

function ClsStore.LoadClassNames()
  local ok, v = pcall(function()
    return ac.storage["releaseClassNames"]
  end)
  if ok and type(v) == "string" and v ~= "" then
    return v
  end
  return nil
end

return ClsStore
