-- AI enhancement layer. Every AI car gets base values (its own ai.ini, scaled by
-- its class profile) plus the situation modifiers of ai/modifiers.lua, clamped and applied through
-- the physics.setAI* calls a few times a second. Cars that another feature is steering (the yellow
-- flag limit, the safety car, the rolling start, a crash-guard pit lock, the pits) are left alone and
-- re-tuned as soon as they are free again.
-- Also: stuck cars. A car that crawls under STUCK_KMH for STUCK_SECONDS with nothing slow ahead of
-- it to explain it is sent to the pits (the crash guard repairs it there) and held for
-- STUCK_FREEZE_SECONDS.
local C = require("src/constants")
local S = require("src/state")
local U = require("src/util")
local Sess = require("src/session")
local RCtl = require("src/rolling/control")
local Field = require("src/ai/field")
local Mods = require("src/ai/modifiers")

local sFormat = string.format
local getCar = ac.getCar
local abs, min, max = math.abs, math.min, math.max

local M = {}

local clock = 0.0          -- seconds since the race went green
local nextApplyAt = 0.0
local base = {}            -- [i] = the car's own values (ai.ini + the aggression it started with)
local cur = {}             -- [i] = values currently set on the car (smoothed)
local applied = {}         -- [i] = last values sent to physics
local stuck = {}           -- [i] = seconds spent crawling
local frozen = {}          -- [i] = clock at which a teleported car was sent to the pits
local slowT, slowLogged = {}, {}   -- diagnostics: seconds a car has crawled / already logged

local function inPit(car)
  return car.isInPitlane == true or car.isInPit == true
end

local function clamp(v, lo, hi) return max(lo, min(hi, v)) end

local function iniNumber(cfg, section, key, fallback)
  if not (cfg and cfg.get) then return fallback end
  local ok, v = pcall(cfg.get, cfg, section, key, fallback)
  if ok and type(v) == "number" then return v end
  return fallback
end

local function baseFor(i, car)
  local b = base[i]
  if b then return b end
  local cfg
  if ac.INIConfig and ac.INIConfig.carData then
    local ok, res = pcall(ac.INIConfig.carData, i, "ai.ini")
    if ok then cfg = res end
  end
  b = {
    brake = iniNumber(cfg, "PEDALS", "BRAKE_HINT", 1.0),
    lookahead = iniNumber(cfg, "LOOKAHEAD", "BASE", 12),
    steer = iniNumber(cfg, "STEER", "STEER_GAIN", 1.0),
    tyre = iniNumber(cfg, "TYRES", "HINT", 1.0),
    aggression = (type(car.aiAggression) == "number" and car.aiAggression) or 0.3,
  }
  base[i] = b
  return b
end

local function profileFor(i)
  local key = S.driverClass[i]
  local p = key and C.AI_CLASS_PROFILES[string.lower(key)]
  return p or C.AI_PROFILE_DEFAULT
end

-- Cars another feature is steering right now.
local function controlled(i, car)
  if i == 0 or i == S.safetyCarIndex or S.crashLocked[i] or inPit(car) then return true end
  if S.fcy ~= "green" then return true end
  local y = S.yellowCars[i]
  return (y and (y.hazard or y.limited or y.releasing)) or false
end

local function restoreBase(i)
  local b = base[i]
  if not b then return end
  pcall(physics.setAICaution, i, 1.0)
  pcall(physics.setExtraAIGrip, i, 1.0)
  pcall(physics.setAIBrakeHint, i, b.brake)
  pcall(physics.setAIAggression, i, b.aggression)
  pcall(physics.setAILookaheadBase, i, b.lookahead)
  pcall(physics.setAITyresHint, i, b.tyre)
  pcall(physics.setAISteerMultiplier, i, b.steer)
end

-- ---------------------------------------------------------------------------------------------
-- Stuck cars
local function blockedAhead(i, d)
  if d.carAhead < 0 or d.gapAhead > C.AI_STUCK_AHEAD_M then return false end
  local ad = Field.Get(d.carAhead)
  return ad ~= nil and ad.speed < C.AI_STUCK_QUEUE_KMH
end

local function updateStuck(dt)
  for i = 1, S.driverCount - 1 do
    local car = getCar(i)
    if car and i ~= S.safetyCarIndex and not S.crashLocked[i] then
      local froze = frozen[i]
      if froze then
        -- Teleported: hold the car still in the pits for a while, then give it back.
        if inPit(car) then
          pcall(physics.setAITopSpeed, i, 0.0)
          pcall(physics.setAIThrottleLimit, i, 0.0)
          pcall(physics.setAIStopCounter, i, C.ROLLING_START_STOP_COUNTER_HELD)
          if clock - froze >= C.AI_STUCK_FREEZE_SECONDS then
            frozen[i] = nil
            RCtl.RestoreAIControl(i, false)
            U.Log(sFormat("AI: car %d released from the pits after a stuck teleport", i))
          end
        elseif clock - froze > 3.0 then
          frozen[i] = nil   -- the teleport did not take; start watching again
        end
      else
        local d = Field.Get(i)
        -- Diagnostics: a car crawling under SLOWLOG_KMH for SLOWLOG_SECONDS is logged once with what
        -- the AI layer was doing to it and what is in front of it.
        if d and clock >= C.AI_STUCK_GRACE_SECONDS and d.speed < C.AI_SLOWLOG_KMH then
          slowT[i] = (slowT[i] or 0.0) + dt
          if slowT[i] >= C.AI_SLOWLOG_SECONDS and not slowLogged[i] then
            slowLogged[i] = true
            local a = d.carAhead >= 0 and Field.Get(d.carAhead)
            local c = cur[i] or {}
            local y = S.yellowCars[i]
            U.Log(sFormat("AI: t=%.1f car %d (%s, class %s) slow at %.0f km/h for %.0f s | ahead=%s | set caution=%s grip=%s brake=%s aggr=%s look=%s | fcy=%s hazard=%s limited=%s | %s",
                clock, i, tostring(ac.getCarID(i)), tostring(S.driverClass[i]), d.speed, C.AI_SLOWLOG_SECONDS,
                a and sFormat("car %d at %.0f m, %.0f km/h", d.carAhead, d.gapAhead, a.speed) or "none",
                tostring(c.caution and sFormat("%.2f", c.caution)), tostring(c.grip and sFormat("%.2f", c.grip)),
                tostring(c.brake and sFormat("%.2f", c.brake)), tostring(c.aggression and sFormat("%.2f", c.aggression)),
                tostring(c.lookahead and sFormat("%.1f", c.lookahead)), tostring(S.fcy),
                tostring(y and y.hazard), tostring(y and y.limited), U.CarDiag(car)))
          end
        else
          slowT[i], slowLogged[i] = nil, nil
        end
        local watch = d and clock >= C.AI_STUCK_GRACE_SECONDS and S.fcy == "green"
            and d.speed < C.AI_STUCK_KMH and not blockedAhead(i, d)
            and not (S.yellowCars[i] and (S.yellowCars[i].limited or S.yellowCars[i].releasing))
        if watch then
          stuck[i] = (stuck[i] or 0.0) + dt
          if stuck[i] >= C.AI_STUCK_SECONDS then
            stuck[i] = nil
            U.Log(sFormat("AI: t=%.1f car %d stuck at %.0f km/h for %.0f s; teleporting it to the pits | %s",
                clock, i, d.speed, C.AI_STUCK_SECONDS, U.CarDiag(car)))
            local ok = physics.teleportCarTo and ac.SpawnSet and pcall(physics.teleportCarTo, i, ac.SpawnSet.Pits)
            if ok then
              frozen[i] = clock
              applied[i], cur[i] = nil, nil
            end
          end
        else
          stuck[i] = nil
        end
      end
    end
  end
end

-- ---------------------------------------------------------------------------------------------
local function smoothTo(i, key, target, dt, upFast)
  local c = cur[i]
  local v = c[key]
  if v == nil or (upFast and target > v) then
    c[key] = target
  else
    c[key] = v + (target - v) * min(1.0, dt * C.AI_SMOOTH_PER_SECOND)
  end
  return c[key]
end

local function send(i, key, value, fn, eps)
  local a = applied[i]
  if a[key] == nil or abs(a[key] - value) > eps then
    a[key] = value
    pcall(fn, i, value)
  end
end

local function tune(i, car, applyDt)
  local b = baseFor(i, car)
  local p = profileFor(i)
  local m = Mods.Compute(i, car, clock) or {}

  cur[i] = cur[i] or {}
  applied[i] = applied[i] or {}

  local caution = clamp(p.caution + (m.aiCaution or 0), 0.0, 3.0)
  local grip = clamp(p.grip + (m.aiGrip or 0), 0.1, 2.0)
  local brake = clamp(b.brake * p.brake + (m.brakehint or 0), 0.05, 2.0)
  local aggression = clamp(b.aggression + p.aggression + (m.aggression or 0), 0.05, 1.0)
  local lookahead = clamp(b.lookahead * p.lookahead + (m.lookaheadBase or 0), 1, 100)
  local tyre = clamp(b.tyre + (m.tyreHint or 0), 0.1, 2.0)
  local steer = clamp(b.steer * p.steer, 0.1, 10)

  -- More caution / more grip (an emergency) is taken at once; everything else eases in.
  caution = smoothTo(i, "caution", caution, applyDt, true)
  grip = smoothTo(i, "grip", grip, applyDt, true)
  brake = smoothTo(i, "brake", brake, applyDt, false)
  aggression = smoothTo(i, "aggression", aggression, applyDt, false)
  lookahead = smoothTo(i, "lookahead", lookahead, applyDt, false)
  tyre = smoothTo(i, "tyre", tyre, applyDt, false)

  send(i, "caution", caution, physics.setAICaution, 0.004)
  send(i, "grip", grip, physics.setExtraAIGrip, 0.002)
  send(i, "brake", brake, physics.setAIBrakeHint, 0.004)
  send(i, "aggression", aggression, physics.setAIAggression, 0.004)
  send(i, "lookahead", lookahead, physics.setAILookaheadBase, 0.05)
  send(i, "tyre", tyre, physics.setAITyresHint, 0.002)
  send(i, "steer", steer, physics.setAISteerMultiplier, 0.01)
end

function M.Reset()
  for i in pairs(base) do
    if i ~= 0 then
      restoreBase(i)
      if frozen[i] then RCtl.RestoreAIControl(i, false) end
    end
  end
  base, cur, applied, stuck, frozen, slowT, slowLogged = {}, {}, {}, {}, {}, {}, {}
  clock, nextApplyAt = 0.0, 0.0
  Field.Reset()
end

local function shouldRun(sim)
  if sim.isSessionStarted ~= true or not Sess.IsRaceMode(sim) then return false end
  if S.rollingStartState == "staged" or S.rollingStartState == "rolling" then return false end
  return not Sess.IsSessionFinished(sim)
end

function M.Update(sim, dt)
  if not shouldRun(sim) then
    if next(base) ~= nil or next(frozen) ~= nil then M.Reset() end
    return
  end
  clock = clock + dt
  Field.Update(dt)
  if clock < nextApplyAt then return end
  local applyDt = max(dt, C.AI_APPLY_SECONDS)
  nextApplyAt = clock + C.AI_APPLY_SECONDS

  for i = 1, S.driverCount - 1 do
    local car = getCar(i)
    if car then
      if controlled(i, car) then
        -- Someone else steers this car: forget what we set, so it is re-tuned when it is free.
        if applied[i] then applied[i], cur[i] = nil, nil end
      elseif not frozen[i] then
        tune(i, car, applyDt)
      end
    end
  end
  updateStuck(applyDt)
end

return M
