-- Shared material helpers for the flag-board lighting methods (methods/*.lua).
--
-- A method is a table:
--   name               string, shown in the log
--   Matches(filter, config) -> boolean   true when the track config says boards found with this
--                       mesh filter are lit this way (config = race_control/track_config.lua result)
--   Prepare(ref) -> state                reads the originals the method will restore
--   Apply(panel, colour)                 colour: rgb glow, or nil for the board's original look
-- Most methods only differ in a few extra material values written while lit; Base.Make builds one
-- from such a list.
local M = {}

function M.ReadNumber(ref, prop)
  local ok, value = pcall(function() return ref:getMaterialPropertyValue(1, prop) end)
  if ok and type(value) == "number" then return value end
  return nil
end

function M.ReadEmissive(ref)
  local ok, value = pcall(function() return ref:getMaterialPropertyValue(1, "ksEmissive") end)
  if ok and value ~= nil and type(value) ~= "number" then
    -- CSP returns a vec3 (x, y, z); an rgb (r, g, b) is accepted too.
    local okVec, copy = pcall(function() return rgb(value.x, value.y, value.z) end)
    if okVec and copy then return copy end
    local okRgb, copy2 = pcall(function() return rgb(value.r, value.g, value.b) end)
    if okRgb and copy2 then return copy2 end
  end
  return rgb(0, 0, 0)
end

local function setProp(ref, prop, value)
  pcall(function() ref:setMaterialProperty(prop, value) end)
end

-- A method that writes ksEmissive plus `litProps` (material property -> value) while the board shows
-- a colour, and puts every original back when it goes off. A property whose original cannot be read
-- is never written.
function M.Make(name, litProps, matches)
  local method = { name = name, Matches = matches }

  function method.Prepare(ref)
    local extras = {}
    for prop in pairs(litProps) do extras[prop] = M.ReadNumber(ref, prop) end
    return { emissive = M.ReadEmissive(ref), extras = extras }
  end

  function method.Apply(panel, colour)
    local ref, state = panel.ref, panel.state
    setProp(ref, "ksEmissive", colour or state.emissive)
    for prop, litValue in pairs(litProps) do
      local original = state.extras[prop]
      if original ~= nil then setProp(ref, prop, colour and litValue or original) end
    end
  end

  return method
end

return M
