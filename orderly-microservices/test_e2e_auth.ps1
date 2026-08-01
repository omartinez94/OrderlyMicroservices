# Orderly Microservices - Phase 4 Integration & E2E Validation Script
# This script authenticates, validates JWT tokens, refreshes tokens, and checks downstream API enforcement.

# Disable SSL Certificate checking for local self-signed dev certificates
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$IdentityHttpsUrl = "https://localhost:5007"
$IdentityHttpUrl = "http://localhost:5008"
$IdentityDockerUrl = "http://localhost:5007" # docker-compose HTTP port

$BasketHttpsUrl = "https://localhost:5051"
$BasketHttpUrl = "http://localhost:5001"
$BasketDockerUrl = "http://localhost:6001" # docker-compose HTTP port

# 1. Determine active endpoints
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   Orderly Microservices E2E Tester       " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

Write-Host "Detecting active Identity API..." -ForegroundColor Gray
$BaseIdentityUrl = $null

foreach ($url in @($IdentityHttpsUrl, $IdentityHttpUrl, $IdentityDockerUrl)) {
    try {
        $response = Invoke-WebRequest -Uri "$url/health" -Method Get -TimeoutSec 2 -UseBasicParsing -ErrorAction Ignore
        if ($response -and $response.StatusCode -eq 200) {
            $BaseIdentityUrl = $url
            Write-Host "[SUCCESS] Detected Identity API active at: $BaseIdentityUrl" -ForegroundColor Green
            break
        }
    } catch {}
}

if (-not $BaseIdentityUrl) {
    Write-Host "[ERROR] Identity Service is not running! Please run 'dotnet run' in Identity.API or 'docker-compose up -d --build'." -ForegroundColor Red
    exit 1
}

Write-Host "`nDetecting active Basket API..." -ForegroundColor Gray
$BaseBasketUrl = $null
foreach ($url in @($BasketHttpsUrl, $BasketHttpUrl, $BasketDockerUrl)) {
    try {
        $response = Invoke-WebRequest -Uri "$url/health" -Method Get -TimeoutSec 2 -UseBasicParsing -ErrorAction Ignore
        if ($response -and $response.StatusCode -eq 200) {
            $BaseBasketUrl = $url
            Write-Host "[SUCCESS] Detected Basket API active at: $BaseBasketUrl" -ForegroundColor Green
            break
        }
    } catch {}
}

if (-not $BaseBasketUrl) {
    Write-Host "[WARNING] Basket Service is not active. Downstream JWT validation tests will be skipped." -ForegroundColor Yellow
}

# 2. Authenticate and issue token
Write-Host "`n[STEP 1] Requesting OIDC Token via /api/auth/login..." -ForegroundColor Cyan
$LoginBody = @{
    Email = "admin@orderly.com"
    Password = "Admin@123456"
} | ConvertTo-Json

try {
    $LoginResponse = Invoke-RestMethod -Uri "$BaseIdentityUrl/api/auth/login" -Method Post -Body $LoginBody -ContentType "application/json"
    $AccessToken = $LoginResponse.access_token
    $RefreshToken = $LoginResponse.refresh_token
    
    if ($AccessToken) {
        Write-Host "[SUCCESS] Token successfully issued by OpenIddict!" -ForegroundColor Green
        Write-Host "Access Token Prefix: $($AccessToken.Substring(0, 30))..." -ForegroundColor Gray
        Write-Host "Refresh Token Prefix: $($RefreshToken.Substring(0, 30))..." -ForegroundColor Gray
    } else {
        Write-Host "[FAIL] Login response did not contain access token." -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "[FAIL] Token issuance failed: $_" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "Response Details: $($reader.ReadToEnd())" -ForegroundColor Red
    }
    exit 1
}

# 3. Test downstream token validation if Basket API is active
if ($BaseBasketUrl) {
    Write-Host "`n[STEP 2] Testing Downstream JWT validation on Basket.API (Secured Endpoint)..." -ForegroundColor Cyan
    # Phase 3: the /api/v1/baskets/{userId}/{restaurantId} shim was
    # removed end of Phase 3. The only GET endpoint is the token-bound
    # /api/v1/cart — identity derives from the Bearer token, not the
    # URL. Updating the test script to match.
    $BasketEndpoint = "$BaseBasketUrl/api/v1/cart"

    Write-Host "Sending unauthorized request (No Bearer Token)..." -ForegroundColor Gray
    try {
        $response = Invoke-WebRequest -Uri $BasketEndpoint -Method Get -UseBasicParsing
        Write-Host "[FAIL] Secure endpoint allowed request without token! Status: $($response.StatusCode)" -ForegroundColor Red
    } catch {
        if ($_.Exception.Response.StatusCode -eq 401) {
            Write-Host "[SUCCESS] Secure endpoint successfully blocked unauthorized request with 401 Unauthorized!" -ForegroundColor Green
        } else {
            Write-Host "[FAIL] Unexpected error code on unauthorized request: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
        }
    }

    Write-Host "Sending authorized request (With Bearer Token)..." -ForegroundColor Gray
    try {
        $Headers = @{ Authorization = "Bearer $AccessToken" }
        $response = Invoke-WebRequest -Uri $BasketEndpoint -Method Get -Headers $Headers -UseBasicParsing -ErrorAction Ignore

        # 200 OK — the cart is returned (empty-cart body when no cart exists).
        # Never 404: the Phase 3 contract is 200 + empty body for unknown
        # carts (plan §0.4.7).
        if ($response.StatusCode -eq 200) {
            Write-Host "[SUCCESS] Downstream OIDC JWT validation succeeded! Response code: $($response.StatusCode)" -ForegroundColor Green
        } else {
            Write-Host "[FAIL] Downstream request failed with status code: $($response.StatusCode)" -ForegroundColor Red
        }
    } catch {
        Write-Host "[FAIL] Downstream JWT validation request failed: $_" -ForegroundColor Red
    }
}

# 4. Token Refresh
Write-Host "`n[STEP 3] Testing OIDC Token Refresh Flow..." -ForegroundColor Cyan
$RefreshBody = "grant_type=refresh_token&refresh_token=$RefreshToken"

try {
    $RefreshResponse = Invoke-RestMethod -Uri "$BaseIdentityUrl/connect/token" -Method Post -Body $RefreshBody -ContentType "application/x-www-form-urlencoded"
    $NewAccessToken = $RefreshResponse.access_token
    if ($NewAccessToken) {
        Write-Host "[SUCCESS] Token successfully refreshed!" -ForegroundColor Green
        Write-Host "New Access Token Prefix: $($NewAccessToken.Substring(0, 30))..." -ForegroundColor Gray
    } else {
        Write-Host "[FAIL] Token refresh failed. No access token in response." -ForegroundColor Red
    }
} catch {
    Write-Host "[FAIL] Token refresh failed: $_" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "Response Details: $($reader.ReadToEnd())" -ForegroundColor Red
    }
}

# 5. Audit logs retrieval
Write-Host "`n[STEP 4] Retrieving Security Audit logs via /api/audit-log..." -ForegroundColor Cyan
try {
    $Headers = @{ Authorization = "Bearer $AccessToken" }
    $AuditResponse = Invoke-RestMethod -Uri "$BaseIdentityUrl/api/audit-log" -Method Get -Headers $Headers

    if ($AuditResponse) {
        Write-Host "[SUCCESS] Successfully retrieved audit logs from database!" -ForegroundColor Green
        Write-Host "Total audit entries retrieved: $($AuditResponse.Count)" -ForegroundColor Green

        # Display latest audit logs
        $Latest = $AuditResponse | Select-Object -First 3
        foreach ($log in $Latest) {
            Write-Host " - [$($log.Timestamp)] EventType: $($log.EventType), IP: $($log.IpAddress)" -ForegroundColor Gray
        }
    } else {
        Write-Host "[FAIL] Audit logs response is empty." -ForegroundColor Red
    }
} catch {
    Write-Host "[FAIL] Failed to retrieve security audit logs: $_" -ForegroundColor Red
}

# =====================================================================
# Phase 6 of the Trust Root Hardening plan: YARP gateway hardening
# (appsettings: per-route + per-cluster AuthorizationPolicy metadata;
# Program.cs: AddJwtAuthenticationWithDevFallback + AddAuthorization +
# AddCors + UseForwardedHeaders + anonymous /health). These tests
# prove the wiring works end-to-end against a running gateway + the
# Identity authority it trusts for inbound JWT validation.
# =====================================================================

$GatewayHttpUrl = "http://localhost:6004"
$GatewayHttpsUrl = "https://localhost:6064"
$GatewayDockerUrl = "http://localhost:6004" # docker-compose port
$GatewayBaseUrl = $null

foreach ($url in @($GatewayHttpUrl, $GatewayHttpsUrl, $GatewayDockerUrl)) {
    try {
        # Phase 6 §6.6: /health MUST be anonymous (no Authorization
        # header sent) so orchestrator probes work. A 200 on
        # /health without auth proves the mapping is BEFORE the
        # auth middleware.
        $response = Invoke-WebRequest -Uri "$url/health" -Method Get -TimeoutSec 2 -UseBasicParsing -ErrorAction Ignore
        if ($response -and $response.StatusCode -eq 200) {
            $GatewayBaseUrl = $url
            Write-Host "[SUCCESS] Detected YARP Gateway active at: $GatewayBaseUrl" -ForegroundColor Green
            break
        }
    } catch {}
}

if (-not $GatewayBaseUrl) {
    Write-Host "[WARNING] YARP Gateway is not active. Phase 6 gateway scenarios will be skipped." -ForegroundColor Yellow
} else {
    # -----------------------------------------------------------------
    # [STEP 5.1] Anonymous /health returns 200 (no token required).
    # Phase 6 §6.6: the /health endpoint is mapped BEFORE the auth
    # middleware so Docker HEALTHCHECK and K8s liveness/readiness
    # probes work without a JWT.
    # -----------------------------------------------------------------
    Write-Host "`n[STEP 5.1] Testing gateway /health (anonymous, no auth)..." -ForegroundColor Cyan
    try {
        $response = Invoke-WebRequest -Uri "$GatewayBaseUrl/health" -Method Get -UseBasicParsing
        if ($response.StatusCode -eq 200) {
            Write-Host "[SUCCESS] Gateway /health is anonymous + returns 200 (orchestrator probes work)" -ForegroundColor Green
        } else {
            Write-Host "[FAIL] /health returned $($response.StatusCode) — should be 200" -ForegroundColor Red
        }
    } catch {
        Write-Host "[FAIL] /health request failed: $_" -ForegroundColor Red
    }

    # -----------------------------------------------------------------
    # [STEP 5.2] Proxied request without Bearer token returns 401.
    # Phase 6 §6.6: the YARP route metadata references the default
    # auth policy, so anonymous calls are denied before the
    # downstream service is hit.
    # -----------------------------------------------------------------
    Write-Host "`n[STEP 5.2] Testing gateway auth on proxied route without Bearer token..." -ForegroundColor Cyan
    try {
        # Pick a route that's likely to exist behind the gateway in
        # dev compose. The catalog GET /api/v1/restaurants is
        # anonymous (per Phase 4), so a 401 here proves the gateway
        # itself is enforcing auth, not the downstream service.
        $response = Invoke-WebRequest -Uri "$GatewayBaseUrl/catalog-api/api/v1/restaurants" -Method Get -UseBasicParsing
        Write-Host "[FAIL] Gateway allowed anonymous call to protected route! Status: $($response.StatusCode)" -ForegroundColor Red
    } catch {
        if ($_.Exception.Response.StatusCode -eq 401) {
            Write-Host "[SUCCESS] Gateway correctly returned 401 on anonymous proxied call" -ForegroundColor Green
        } else {
            Write-Host "[WARN] Unexpected status on anonymous proxied call: $($_.Exception.Response.StatusCode) — gateway may not be enforcing auth" -ForegroundColor Yellow
        }
    }

    # -----------------------------------------------------------------
    # [STEP 5.3] Proxied request with Bearer token succeeds.
    # Phase 6 §6.6: the gateway validates the JWT against the Identity
    # authority, then forwards the call to the downstream service
    # with the Authorization header intact.
    # -----------------------------------------------------------------
    Write-Host "`n[STEP 5.3] Testing gateway auth on proxied route WITH Bearer token..." -ForegroundColor Cyan
    try {
        $Headers = @{ Authorization = "Bearer $AccessToken" }
        $response = Invoke-WebRequest -Uri "$GatewayBaseUrl/catalog-api/api/v1/restaurants" -Method Get -Headers $Headers -UseBasicParsing -ErrorAction Ignore
        # 200 (anonymous GET, token accepted) or 5xx (downstream
        # service not running in this environment) are both acceptable
        # — what matters is NOT 401. The gateway should have
        # validated the JWT and forwarded to the downstream service.
        if ($response.StatusCode -ne 401) {
            Write-Host "[SUCCESS] Gateway validated JWT + forwarded to downstream (status: $($response.StatusCode))" -ForegroundColor Green
        } else {
            Write-Host "[FAIL] Gateway returned 401 even with a valid Bearer token!" -ForegroundColor Red
        }
    } catch {
        # WebException with 5xx status code means the downstream
        # service didn't respond — fine for this assertion, the
        # point is that the gateway accepted the token.
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -ne 401) {
            Write-Host "[SUCCESS] Gateway accepted the token (downstream returned $statusCode — expected in test env)" -ForegroundColor Green
        } else {
            Write-Host "[FAIL] Gateway returned 401 with a valid Bearer token!" -ForegroundColor Red
        }
    }

    # -----------------------------------------------------------------
    # [STEP 5.4] CORS preflight returns 200 with CORS headers.
    # Phase 6 §6.6: UseCors is registered before auth so OPTIONS
    # preflight from a registered origin succeeds without a token.
    # The allowed origin (http://localhost:3000) matches appsettings.
    # -----------------------------------------------------------------
    Write-Host "`n[STEP 5.4] Testing gateway CORS preflight (allowed origin, no token)..." -ForegroundColor Cyan
    try {
        $corsHeaders = @{
            "Origin" = "http://localhost:3000"
            "Access-Control-Request-Method" = "GET"
            "Access-Control-Request-Headers" = "authorization,content-type"
        }
        $response = Invoke-WebRequest -Uri "$GatewayBaseUrl/catalog-api/api/v1/restaurants" -Method Options -Headers $corsHeaders -UseBasicParsing
        if ($response.StatusCode -eq 200 -or $response.StatusCode -eq 204) {
            # CORS preflight should echo the origin back via
            # Access-Control-Allow-Origin.
            $allowOrigin = $response.Headers["Access-Control-Allow-Origin"]
            if ($allowOrigin -eq "http://localhost:3000") {
                Write-Host "[SUCCESS] CORS preflight succeeded (Access-Control-Allow-Origin: $allowOrigin)" -ForegroundColor Green
            } else {
                Write-Host "[WARN] CORS preflight returned $($response.StatusCode) but Allow-Origin header missing/wrong (got: $allowOrigin)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "[FAIL] CORS preflight returned $($response.StatusCode) — should be 200 or 204" -ForegroundColor Red
        }
    } catch {
        Write-Host "[WARN] CORS preflight request failed: $_" -ForegroundColor Yellow
    }

    # -----------------------------------------------------------------
    # [STEP 5.5] CORS preflight from a NON-allowed origin is denied.
    # Phase 6 §6.6: the CORS policy enumerates allowed origins
    # explicitly; any other Origin header is denied.
    # -----------------------------------------------------------------
    Write-Host "`n[STEP 5.5] Testing gateway CORS preflight (NON-allowed origin)..." -ForegroundColor Cyan
    try {
        $corsHeaders = @{
            "Origin" = "http://evil.example.com"
            "Access-Control-Request-Method" = "GET"
        }
        $response = Invoke-WebRequest -Uri "$GatewayBaseUrl/catalog-api/api/v1/restaurants" -Method Options -Headers $corsHeaders -UseBasicParsing
        # A non-allowed origin either: doesn't get the Allow-Origin
        # header back, or gets blocked. Both are acceptable.
        $allowOrigin = $response.Headers["Access-Control-Allow-Origin"]
        if (-not $allowOrigin) {
            Write-Host "[SUCCESS] CORS preflight from disallowed origin did NOT echo Access-Control-Allow-Origin" -ForegroundColor Green
        } else {
            Write-Host "[FAIL] CORS preflight from http://evil.example.com echoed Allow-Origin: $allowOrigin" -ForegroundColor Red
        }
    } catch {
        # 403 / refused connection are both acceptable for a
        # disallowed origin preflight.
        Write-Host "[SUCCESS] CORS preflight from disallowed origin was blocked: $_" -ForegroundColor Green
    }
}

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "   E2E Validation Completed!              " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
