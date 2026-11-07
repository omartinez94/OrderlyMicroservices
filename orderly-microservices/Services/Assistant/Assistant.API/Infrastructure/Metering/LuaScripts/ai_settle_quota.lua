-- KEYS[1] = ai:quota:{restaurantId}:{yyyyMM}
-- KEYS[2] = ai:res_zset:{restaurantId}:{yyyyMM}
-- KEYS[3] = ai:res_amt:{restaurantId}:{yyyyMM}
-- ARGV[1] = reservationId
-- ARGV[2] = uncachedPromptTokens
-- ARGV[3] = cachedPromptTokens
-- ARGV[4] = completionTokens

local quotaKey = KEYS[1]
local resZsetKey = KEYS[2]
local resAmtKey = KEYS[3]

local resId = ARGV[1]
local uncachedPrompt = tonumber(ARGV[2])
local cachedPrompt = tonumber(ARGV[3])
local completion = tonumber(ARGV[4])

-- 1. Release reservation hold
redis.call("ZREM", resZsetKey, resId)
redis.call("HDEL", resAmtKey, resId)

-- 2. Compute effective billable tokens (cached tokens weighted at 25%)
local discountedCached = math.ceil(cachedPrompt * 0.25)
local effectiveDelta = uncachedPrompt + discountedCached + completion

redis.call("HINCRBY", quotaKey, "used_uncached_prompt", uncachedPrompt)
redis.call("HINCRBY", quotaKey, "used_cached_prompt", cachedPrompt)
redis.call("HINCRBY", quotaKey, "used_completion", completion)
local newEffective = redis.call("HINCRBY", quotaKey, "used_effective", effectiveDelta)

return { newEffective, effectiveDelta }
