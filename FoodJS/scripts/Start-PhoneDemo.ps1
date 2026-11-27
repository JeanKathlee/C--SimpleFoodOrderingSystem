param(
    [Parameter(Mandatory = $true)][string]$PublicUrl,
    [int]$Port = 3003,
    [int]$TunnelProcessId = 0
)

$ErrorActionPreference = "Stop"
$demoUri = $null
if (![Uri]::TryCreate($PublicUrl, [UriKind]::Absolute, [ref]$demoUri) -or
    $demoUri.Scheme -ne "https" -or !$demoUri.Host.EndsWith(".trycloudflare.com") -or
    $demoUri.AbsolutePath -ne "/" -or $demoUri.Query -ne "" -or $demoUri.Fragment -ne "" -or $demoUri.UserInfo -ne "") {
    throw "Pass the exact HTTPS origin printed by your Cloudflare quick tunnel."
}
$PublicUrl = $demoUri.GetLeftPart([UriPartial]::Authority)
if ($Port -lt 1024 -or $Port -gt 65535) { throw "Choose an unprivileged local port." }
$apiRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\backend-csharp\FoodJS.Api")).Path
$frontendRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\frontend")).Path
$apiDll = Join-Path $apiRoot "bin\Release\net10.0\FoodJS.Api.dll"
$distPath = Join-Path $frontendRoot "dist"
if (!(Test-Path -LiteralPath $apiDll) -or !(Test-Path -LiteralPath (Join-Path $distPath "index.html"))) {
    throw "Build first: npm.cmd run build:frontend; dotnet build backend-csharp/FoodJS.Api/FoodJS.Api.csproj -c Release"
}
$runtimeDir = Join-Path $apiRoot ".demo-runtime"
if ([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $Port) {
    throw "Port $Port is already in use. Stop the previous demo before starting another."
}
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
# Never copy existing users/orders or reuse an unknown database. Migrations create
# this new file; only the sample catalog is seeded, without any demo/admin users.
$demoDb = Join-Path $runtimeDir ("phone-demo-" + [Guid]::NewGuid().ToString("N") + ".db")

# User-approved SMTP settings only; never print or persist credentials.
$secretRows = @(& dotnet user-secrets list --project (Join-Path $apiRoot "FoodJS.Api.csproj"))
if ($LASTEXITCODE -ne 0) { throw "Could not read the project's local SMTP settings." }
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = (Get-Command dotnet.exe).Source
$start.Arguments = '"' + $apiDll + '" --webroot "' + $distPath + '"'
$start.WorkingDirectory = $apiRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
# Do not inherit OAuth/bootstrap credentials from a parent shell, either.
foreach ($name in @($start.EnvironmentVariables.Keys)) {
    if ($name -match '^(Google|BootstrapAdmin)(__|:)') { $start.EnvironmentVariables.Remove($name) }
}
$start.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Production"
$start.EnvironmentVariables["DOTNET_ENVIRONMENT"] = "Production"
$start.EnvironmentVariables["ASPNETCORE_URLS"] = "http://127.0.0.1:$Port"
foreach ($row in $secretRows) {
    $parts = $row -split ' = ', 2
    if ($parts.Count -eq 2 -and $parts[0] -in @("Email:FromAddress", "Email:Smtp:Host", "Email:Smtp:Port", "Email:Smtp:Username", "Email:Smtp:Password")) {
        $start.EnvironmentVariables[$parts[0].Replace(":", "__")] = $parts[1]
    }
}
$secretRows = $null
$parts = $null
$row = $null
foreach ($name in @("Email__FromAddress", "Email__Smtp__Host", "Email__Smtp__Username", "Email__Smtp__Password")) {
    if ([string]::IsNullOrWhiteSpace($start.EnvironmentVariables[$name])) {
        $start.EnvironmentVariables.Clear()
        throw "SMTP is incomplete. Run Configure-Email.ps1 locally first."
    }
}
$start.EnvironmentVariables["Email__FrontendUrl"] = $PublicUrl
$start.EnvironmentVariables["Google__ClientId"] = ""
$start.EnvironmentVariables["Google__ClientSecret"] = ""
$start.EnvironmentVariables["Google__RedirectUri"] = ""
$start.EnvironmentVariables["BootstrapAdmin__Email"] = ""
$start.EnvironmentVariables["BootstrapAdmin__Password"] = ""
$start.EnvironmentVariables["Demo__SeedCatalog"] = "true"
$start.EnvironmentVariables["DataProtection__KeyRingPath"] = (Join-Path $runtimeDir "keys")
$start.EnvironmentVariables["Logging__LogLevel__Microsoft.EntityFrameworkCore"] = "Warning"
$start.EnvironmentVariables["Cors__AllowedOrigins__0"] = $PublicUrl
$start.EnvironmentVariables["AllowedHosts"] = "$($demoUri.Host);localhost;127.0.0.1"
$start.EnvironmentVariables["ConnectionStrings__DefaultConnection"] = "Data Source=$demoDb"
$keyBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($keyBytes)
    $start.EnvironmentVariables["Jwt__Secret"] = [Convert]::ToBase64String($keyBytes)
}
finally { $rng.Dispose(); [Array]::Clear($keyBytes, 0, $keyBytes.Length) }

$apiProcess = [System.Diagnostics.Process]::Start($start)
@{
    PublicUrl = $PublicUrl
    ApiProcessId = $apiProcess.Id
    TunnelProcessId = $TunnelProcessId
    ApiAssembly = $apiDll
    Database = $demoDb
    Port = $Port
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtimeDir "phone-demo.json") -Encoding UTF8
$start.EnvironmentVariables.Clear()
Write-Host "Phone demo: $PublicUrl"
Write-Host "Fresh demo database with sample catalog only; normal localhost data/settings are unchanged."
Write-Host "Register a new account at this URL. Google sign-in is not enabled for this temporary demo."
try { $apiProcess.WaitForExit(); exit $apiProcess.ExitCode }
finally { $apiProcess.Dispose() }
