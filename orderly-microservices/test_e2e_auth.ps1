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

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "   E2E Validation Completed!              " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
