-- KEYS[1] = ai:quota:{restaurantId}:{yyyyMM}
-- KEYS[2] = ai:res_zset:{restaurantId}:{yyyyMM}
-- KEYS[3] = ai:res_amt:{restaurantId}:{yyyyMM}
-- KEYS[4] = ai:config:{restaurantId}
-- ARGV[1] = reservationId (Guid)
-- ARGV[2] = estimatedTokens
-- ARGV[3] = nowUnixMs
-- ARGV[4] = ttlMs (e.g. 120000 for 2 mins)
-- ARGV[5] = defaultBaseQuota (250000)

local quotaKey = KEYS[1]
local resZsetKey = KEYS[2]
local resAmtKey = KEYS[3]
local configKey = KEYS[4]

local resId = ARGV[1]
local estTokens = tonumber(ARGV[2])
local nowMs = tonumber(ARGV[3])
local ttlMs = tonumber(ARGV[4])
local defaultBase = tonumber(ARGV[5])

-- 1. Evict expired in-flight reservations (Self-Healing)
local expiredIds = redis.call("ZRANGEBYSCORE", resZsetKey, "-inf", nowMs)
if #expiredIds > 0 then
    redis.call("ZREMRANGEBYSCORE", resZsetKey, "-inf", nowMs)
    redis.call("HDEL", resAmtKey, unpack(expiredIds))
end

-- 2. Read tenant configuration
local baseQuota = defaultBase
local allowOverage = 0
local overageLimit = 0
local isSuspended = 0

if redis.call("EXISTS", configKey) == 1 then
    baseQuota = tonumber(redis.call("HGET", configKey, "base_quota") or defaultBase)
    allowOverage = tonumber(redis.call("HGET", configKey, "allow_overage") or 0)
    overageLimit = tonumber(redis.call("HGET", configKey, "overage_limit") or 0)
    isSuspended = tonumber(redis.call("HGET", configKey, "is_suspended") or 0)
end

if isSuspended == 1 then
    return { -1, "TENANT_AI_SUSPENDED", 0, 0 }
end

local maxAllowed = baseQuota
if allowOverage == 1 then
    maxAllowed = baseQuota + overageLimit
end

-- 3. Sum active non-expired reservations
local activeResAmounts = redis.call("HVALS", resAmtKey)
local totalReserved = 0
for i = 1, #activeResAmounts do
    totalReserved = totalReserved + tonumber(activeResAmounts[i])
end

local usedEffective = tonumber(redis.call("HGET", quotaKey, "used_effective") or 0)
local currentCommitted = usedEffective + totalReserved

if (currentCommitted + estTokens) > maxAllowed then
    if allowOverage == 0 and (currentCommitted + estTokens) > baseQuota then
        return { 0, "HARD_CAP_EXCEEDED", currentCommitted, baseQuota }
    else
        return { 0, "SAFETY_CEILING_EXCEEDED", currentCommitted, maxAllowed }
    end
end

-- 4. Record reservation hold with expiration timestamp
redis.call("ZADD", resZsetKey, nowMs + ttlMs, resId)
redis.call("HSET", resAmtKey, resId, estTokens)

if redis.call("TTL", quotaKey) < 0 then
    redis.call("EXPIRE", quotaKey, 5184000)
    redis.call("EXPIRE", resZsetKey, 5184000)
    redis.call("EXPIRE", resAmtKey, 5184000)
end

return { 1, "OK", currentCommitted + estTokens, maxAllowed }
