-- DonjHACK treasury_capture.lua
-- Capture read-only de fenetres memoire autour des candidats treasury.
-- Aucune ecriture memoire, aucun hook, aucune injection.

local CONFIG = {
  processName = "Rome2.exe",
  processId = nil,
  donjHackRoot = [[C:\Games\Total War - Rome 2\DonjHACK]],
  featureId = "campaign.player_faction.treasury",
  scenarioId = "manual-scenario",
  stepId = "before-action",
  action = "manual capture",
  uiTreasury = 0,
  valueHistory = {},
  contextBeforeBytes = 0x200,
  contextAfterBytes = 0x200,
  candidates = {
    -- Exemple:
    -- { candidateId = "treasury-int32-0x12345678", address = 0x12345678 },
  }
}

local function hex(value)
  if value == nil then
    return nil
  end
  return string.format("0x%X", value)
end

local function relativeHex(value)
  if value == nil then
    return nil
  end
  if value < 0 then
    return "-0x" .. string.format("%X", math.abs(value))
  end
  return hex(value)
end

local function signedToUint32(value)
  if value == nil then
    return nil
  end
  if value < 0 then
    return value + 0x100000000
  end
  return value
end

local function escapeJson(value)
  value = tostring(value or "")
  value = value:gsub("\\", "\\\\")
  value = value:gsub("\"", "\\\"")
  value = value:gsub("\r", "\\r")
  value = value:gsub("\n", "\\n")
  value = value:gsub("\t", "\\t")
  return value
end

local function isArray(tbl)
  if type(tbl) ~= "table" then
    return false
  end
  local count = 0
  for key, _ in pairs(tbl) do
    if type(key) ~= "number" then
      return false
    end
    count = count + 1
  end
  return count == #tbl
end

local function jsonEncode(value)
  local valueType = type(value)
  if valueType == "nil" then
    return "null"
  end
  if valueType == "boolean" then
    return value and "true" or "false"
  end
  if valueType == "number" then
    return tostring(value)
  end
  if valueType == "string" then
    return "\"" .. escapeJson(value) .. "\""
  end
  if valueType ~= "table" then
    return "\"" .. escapeJson(value) .. "\""
  end

  local parts = {}
  if isArray(value) then
    for i = 1, #value do
      parts[#parts + 1] = jsonEncode(value[i])
    end
    return "[" .. table.concat(parts, ",") .. "]"
  end

  local keys = {}
  for key, _ in pairs(value) do
    keys[#keys + 1] = key
  end
  table.sort(keys, function(a, b) return tostring(a) < tostring(b) end)
  for _, key in ipairs(keys) do
    parts[#parts + 1] = "\"" .. escapeJson(key) .. "\":" .. jsonEncode(value[key])
  end
  return "{" .. table.concat(parts, ",") .. "}"
end

local function ensureDirectory(path)
  os.execute('mkdir "' .. path .. '" 2>nul')
end

local function readBytesSafe(address, count)
  local ok, bytes = pcall(readBytes, address, count, true)
  if not ok or bytes == nil then
    return nil
  end
  return bytes
end

local function readInt32FromBytes(bytes, index)
  local b1 = bytes[index] or 0
  local b2 = bytes[index + 1] or 0
  local b3 = bytes[index + 2] or 0
  local b4 = bytes[index + 3] or 0
  local value = b1 + (b2 * 0x100) + (b3 * 0x10000) + (b4 * 0x1000000)
  if value >= 0x80000000 then
    value = value - 0x100000000
  end
  return value
end

local function bytesToHex(bytes)
  local parts = {}
  for i = 1, #bytes do
    parts[#parts + 1] = string.format("%02X", bytes[i] or 0)
  end
  return table.concat(parts, "")
end

local function getRegionInfoSafe(address)
  if type(getMemoryRegionInfo) ~= "function" then
    return nil
  end

  local ok, info = pcall(getMemoryRegionInfo, address)
  if not ok or info == nil then
    return nil
  end

  local baseAddress = info.BaseAddress or info.baseAddress or 0
  local size = info.RegionSize or info.regionSize or info.Size or info.size or 0
  local protection = tostring(info.Protect or info.protect or info.Protection or "unknown")
  local state = tostring(info.State or info.state or "unknown")
  local regionType = tostring(info.Type or info.type or "unknown")

  return {
    baseAddress = baseAddress,
    baseAddressHex = hex(baseAddress),
    size = size,
    state = state,
    protection = protection,
    type = regionType,
    isReadable = not protection:find("NOACCESS"),
    isWritable = protection:find("WRITE") ~= nil or protection:find("WRITECOPY") ~= nil,
    isExecutable = protection:find("EXECUTE") ~= nil
  }
end

local function enumModulesSafe()
  if type(enumModules) ~= "function" then
    return {}
  end

  local ok, modules = pcall(enumModules)
  if not ok or modules == nil then
    return {}
  end

  local result = {}
  for _, module in ipairs(modules) do
    local baseAddress = module.Address or module.address or module.BaseAddress or module.baseAddress or 0
    result[#result + 1] = {
      name = module.Name or module.name or "unknown",
      baseAddress = baseAddress,
      baseAddressHex = hex(baseAddress),
      size = module.Size or module.size or 0,
      path = module.PathToFile or module.path or module.Path or ""
    }
  end
  return result
end

local function captureCandidate(candidate)
  local warnings = {}
  local address = candidate.address
  if address == nil or address == 0 then
    return {
      candidateId = candidate.candidateId or "unknown",
      address = 0,
      addressHex = "0x0",
      contextStart = 0,
      contextStartHex = "0x0",
      contextByteCount = 0,
      contextBytesHex = "",
      decodedInt32Fields = {},
      pointerLikeValues = {},
      evidence = {},
      warnings = { "Adresse candidate absente." }
    }
  end

  local contextStart = address - CONFIG.contextBeforeBytes
  if contextStart < 0 then
    contextStart = 0
  end
  local contextByteCount = CONFIG.contextBeforeBytes + 4 + CONFIG.contextAfterBytes
  local bytes = readBytesSafe(contextStart, contextByteCount)
  if bytes == nil then
    bytes = {}
    warnings[#warnings + 1] = "Lecture de fenetre impossible."
  end

  local fields = {}
  local pointers = {}
  for offset = 0, #bytes - 4, 4 do
    local fieldAddress = contextStart + offset
    local value = readInt32FromBytes(bytes, offset + 1)
    local unsignedValue = signedToUint32(value)
    local relativeOffset = fieldAddress - address
    local matchesUi = value == CONFIG.uiTreasury
    fields[#fields + 1] = {
      relativeOffset = relativeOffset,
      relativeOffsetHex = relativeHex(relativeOffset),
      address = fieldAddress,
      addressHex = hex(fieldAddress),
      value = value,
      matchesUiValue = matchesUi
    }

    if unsignedValue ~= nil and unsignedValue >= 0x10000 and unsignedValue <= 0x7FFFFFFF then
      pointers[#pointers + 1] = {
        relativeOffset = relativeOffset,
        relativeOffsetHex = relativeHex(relativeOffset),
        address = fieldAddress,
        addressHex = hex(fieldAddress),
        value = unsignedValue,
        valueHex = hex(unsignedValue),
        targetRegionBaseHex = nil
      }
    end
  end

  return {
    candidateId = candidate.candidateId or ("treasury-int32-" .. hex(address)),
    address = address,
    addressHex = hex(address),
    region = getRegionInfoSafe(address),
    contextStart = contextStart,
    contextStartHex = hex(contextStart),
    contextByteCount = #bytes,
    contextBytesHex = bytesToHex(bytes),
    decodedInt32Fields = fields,
    pointerLikeValues = pointers,
    evidence = { "Capture CE/Lua read-only autour du candidat treasury." },
    warnings = warnings
  }
end

local function run()
  if #CONFIG.candidates == 0 then
    error("Aucun candidat configure dans CONFIG.candidates.")
  end

  if CONFIG.processId ~= nil then
    openProcess(CONFIG.processId)
  else
    openProcess(CONFIG.processName)
  end

  local candidates = {}
  for _, candidate in ipairs(CONFIG.candidates) do
    candidates[#candidates + 1] = captureCandidate(candidate)
  end

  local processId = 0
  if type(getOpenedProcessID) == "function" then
    processId = getOpenedProcessID()
  end

  local envelope = {
    createdAt = os.date("!%Y-%m-%dT%H:%M:%SZ"),
    tool = "Cheat Engine Lua treasury_capture.lua",
    featureId = CONFIG.featureId,
    process = {
      processId = processId,
      processName = CONFIG.processName,
      architecture = "x86",
      modules = enumModulesSafe()
    },
    scenario = {
      scenarioId = CONFIG.scenarioId,
      stepId = CONFIG.stepId,
      action = CONFIG.action
    },
    knownValues = {
      uiTreasury = CONFIG.uiTreasury,
      valueHistory = CONFIG.valueHistory
    },
    candidates = candidates,
    warnings = {}
  }

  local outputDirectory = CONFIG.donjHackRoot .. [[\evidence\lua-captures]]
  ensureDirectory(outputDirectory)
  local fileName = string.format(
    "treasury-lua-capture-%s-%s-%s.json",
    CONFIG.scenarioId,
    CONFIG.stepId,
    os.date("!%Y%m%d-%H%M%S"))
  local outputPath = outputDirectory .. "\\" .. fileName
  local file = assert(io.open(outputPath, "w"))
  file:write(jsonEncode(envelope))
  file:close()
  print("DonjHACK capture exportee: " .. outputPath)
end

run()
