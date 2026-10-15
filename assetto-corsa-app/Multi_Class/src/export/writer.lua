-- Result folder resolution and atomic-ish file writing.
local S = require("src/state")
local U = require("src/util")

local Writer = {}

function Writer.ResultDefaultFolder()
  -- Matches the dashboard default (Services/LuaResultPaths.cs) so the common case
  -- needs no configuration on either side.
  local okDocs, docs = pcall(function() return ac.getFolder(ac.FolderID.Documents) end)
  if okDocs and type(docs) == "string" and docs ~= "" then
    return U.NormalizePath(docs .. "/Assetto Corsa/mcr-results")
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
    local n = U.NormalizePath(c)
    if n ~= "" then
      return n
    end
  end
  return ""
end

local function probeWrite(folder)
  local probe = folder .. "/.mcr_write_test.tmp"
  local okW = pcall(function()
    if not io.save(probe, "") then
      error("io.save returned false")
    end
  end)
  pcall(function()
    if type(io.deleteFile) == "function" then io.deleteFile(probe) end
  end)
  return okW
end

function Writer.ResultFolderWritable(folder)
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
  if not probeWrite(folder) then
    return false, "folder is not writable"
  end
  return true, nil
end

function Writer.ResultEffectiveFolder()
  if S.resultFolder and S.resultFolder ~= "" then
    return S.resultFolder
  end
  return Writer.ResultDefaultFolder()
end

local function deleteFileQuiet(path)
  pcall(function()
    if type(io.deleteFile) == "function" then io.deleteFile(path) end
  end)
end

-- Renames tempPath over targetPath; true when the target exists afterwards.
local function renameIntoPlace(tempPath, targetPath)
  if type(os.rename) ~= "function" then return false end
  pcall(function() os.rename(tempPath, targetPath) end)

  local exists = true
  if type(io.fileExists) == "function" then
    local ok, result = pcall(io.fileExists, targetPath)
    exists = ok and result == true
  end
  return exists
end

-- Write to a temporary name and rename, so the dashboard can never pick up a
-- partially written result file.
function Writer.WriteResultFile(targetPath, contents)
  local tempPath = targetPath .. ".tmp"

  if not io.save(tempPath, contents) then
    error("io.save returned false")
  end

  if renameIntoPlace(tempPath, targetPath) then
    deleteFileQuiet(tempPath)
    return true
  end

  deleteFileQuiet(tempPath)

  if not io.save(targetPath, contents) then
    error("io.save returned false")
  end

  return true
end

return Writer
