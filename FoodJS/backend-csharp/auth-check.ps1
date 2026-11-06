# quick test for login and admin-only stuff. start the api first, then run:
#   pwsh backend-csharp/auth-check.ps1
$api = "http://localhost:3001/api"
$failed = 0

function Call($method, $path, $body = $null, $token = $null) {
    $headers = @{}
    if ($token) { $headers.Authorization = "Bearer $token" }
    $json = if ($body) { $body | ConvertTo-Json } else { $null }
    $response = Invoke-WebRequest -Method $method -Uri "$api$path" -Headers $headers -Body $json `
        -ContentType "application/json" -SkipHttpErrorCheck
    [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue }
}

function Check($name, $actual, $expected) {
    if ($actual -eq $expected) { Write-Host "PASS $name" }
    else { Write-Host "FAIL $name (expected $expected, got $actual)"; $script:failed++ }
}

$menuItem = @{ name = "Auth check"; categoryId = "test"; price = 1; prepTime = 5; description = "x" }

$admin = Call POST /users/login @{ email = "admin@foodjs.demo"; password = "admin123" }
Check "admin login" $admin.Status 200
Check "admin role" $admin.Body.user.role "admin"

$customer = Call POST /users/login @{ email = "CUSTOMER@foodjs.demo"; password = "customer123" }
Check "customer login (email case-insensitive)" $customer.Status 200
Check "customer role" $customer.Body.user.role "customer"

Check "wrong password" (Call POST /users/login @{ email = "admin@foodjs.demo"; password = "nope" }).Status 401
Check "missing fields" (Call POST /users/login @{ email = "" }).Status 400
Check "google without code" (Call POST /auth/google/callback @{}).Status 400

Check "menu list is public" (Call GET /menu).Status 200
$noToken = Call POST /menu $menuItem
Check "no token" $noToken.Status 401
Check "no token message" $noToken.Body.message "Unauthorized. Token required."
Check "bad token" (Call POST /menu $menuItem "not-a-jwt").Status 401
$forbidden = Call POST /menu $menuItem $customer.Body.token
Check "customer blocked from admin" $forbidden.Status 403
Check "forbidden message" $forbidden.Body.message "Forbidden. Admin access required."
Check "admin allowed" (Call POST /menu $menuItem $admin.Body.token).Status 201

if ($failed) { Write-Host "$failed check(s) failed"; exit 1 } else { Write-Host "All checks passed" }
