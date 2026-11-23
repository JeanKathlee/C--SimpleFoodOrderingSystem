# quick test for login and admin-only stuff. start the api first, then run:
#   pwsh backend-csharp/auth-check.ps1
# (to test an api on another port: pwsh backend-csharp/auth-check.ps1 http://localhost:3002/api)
param($api = "http://localhost:3001/api")
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

$menu = Call GET /menu
Check "menu list is public" $menu.Status 200
Check "menu list has seeded food" (@($menu.Body).Count -ge 8) $true
Check "menu item has category name" (@($menu.Body)[0].category) "Burgers"
Check "customer menu has only available items" (@($menu.Body | Where-Object { -not $_.isAvailable }).Count) 0
Check "admin menu list" (Call GET /menu $null $admin.Body.token).Status 200
Check "view one food item" (Call "GET" "/menu/$(@($menu.Body)[0].id)").Body.name (@($menu.Body)[0].name)
Check "view missing food item" (Call GET /menu/999999).Status 404
Check "search fries (any case)" (@((Call GET "/menu?search=FRIES").Body).Count) 2
Check "search finds description" (@((Call GET "/menu?search=sauce").Body).Count) 2
Check "filter by category" (@((Call GET "/menu?category=Drinks").Body | Where-Object category -ne "Drinks").Count) 0
Check "search + filter" (@((Call GET "/menu?category=Drinks&search=mango").Body).Count) 1
$noToken = Call POST /menu $menuItem
Check "no token" $noToken.Status 401
Check "no token message" $noToken.Body.message "Unauthorized. Token required."
Check "bad token" (Call POST /menu $menuItem "not-a-jwt").Status 401
$forbidden = Call POST /menu $menuItem $customer.Body.token
Check "customer blocked from admin" $forbidden.Status 403
Check "forbidden message" $forbidden.Body.message "Forbidden. Admin access required."
Check "admin allowed" (Call POST /menu $menuItem $admin.Body.token).Status 201

# user accounts: register, list, edit, delete. uses a new email every run
$email = "test$(Get-Date -Format 'yyyyMMddHHmmss')@foodjs.demo"
$newUser = @{ email = $email; password = "secret1"; firstName = "Test"; lastName = "User"; address = "123 Main St" }

Check "register missing fields" (Call POST /users/register @{ email = $email }).Status 400
Check "register short password" (Call POST /users/register @{ email = $email; password = "123"; firstName = "a"; lastName = "b" }).Status 400
Check "register bad email" (Call POST /users/register @{ email = "nope"; password = "secret1"; firstName = "a"; lastName = "b" }).Status 400
$registered = Call POST /users/register $newUser
Check "register" $registered.Status 201
Check "register makes a customer" $registered.Body.user.role "customer"
Check "register saves address" $registered.Body.user.address "123 Main St"
Check "register same email again" (Call POST /users/register $newUser).Status 400
Check "new user can log in" (Call POST /users/login @{ email = $email; password = "secret1" }).Status 200

Check "user list needs login" (Call GET /users).Status 401
Check "user list blocked for customer" (Call GET /users $null $customer.Body.token).Status 403
$list = Call GET /users $null $admin.Body.token
Check "user list for admin" $list.Status 200
Check "user list has new user" (@($list.Body | Where-Object email -eq $email).Count) 1

$me = Call GET /users/me $null $registered.Body.token
Check "get my profile" $me.Body.email $email
Check "edit with empty first name" (Call PATCH /users/me @{ firstName = " " } $registered.Body.token).Status 400
$edited = Call PATCH /users/me @{ firstName = "Changed"; mobileNumber = "0917"; address = "456 New St" } $registered.Body.token
Check "edit profile" $edited.Status 200
Check "edit changes name" $edited.Body.name "Changed User"
Check "edit changes address" $edited.Body.address "456 New St"
Check "edit keeps one address" (@($edited.Body.addressBook).Count) 1

$id = $registered.Body.user.id
Check "delete blocked for customer" (Call DELETE "/users/$id" $null $customer.Body.token).Status 403
Check "admin can't delete self" (Call DELETE "/users/$($admin.Body.user.id)" $null $admin.Body.token).Status 400
Check "admin deletes user" (Call DELETE "/users/$id" $null $admin.Body.token).Status 200
Check "delete again is not found" (Call DELETE "/users/$id" $null $admin.Body.token).Status 404
Check "deleted user can't log in" (Call POST /users/login @{ email = $email; password = "secret1" }).Status 401

if ($failed) { Write-Host "$failed check(s) failed"; exit 1 } else { Write-Host "All checks passed" }
