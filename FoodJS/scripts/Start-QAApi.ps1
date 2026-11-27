param([int]$Port = 3004)
$ErrorActionPreference = "Stop"
if ($Port -lt 1024 -or $Port -gt 65535) { throw "Invalid QA port." }
if ([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $Port) {
    throw "QA port $Port is already in use. Refusing to use an unknown API/database."
}
$apiRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\backend-csharp\FoodJS.Api")).Path
$runRoot = Join-Path $apiRoot (".qa-runtime\" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
& dotnet build (Join-Path $apiRoot "FoodJS.Api.csproj") -c QA --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "QA build failed." }
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = (Get-Command dotnet.exe).Source
$start.Arguments = '"' + (Join-Path $apiRoot "bin\QA\net10.0\FoodJS.Api.dll") + '"'
$start.WorkingDirectory = $apiRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($key in @($start.EnvironmentVariables.Keys)) {
    if ($key -match '^(Email|Google|BootstrapAdmin)(__|:)') { $start.EnvironmentVariables.Remove($key) }
}
$settings = @{
    ASPNETCORE_ENVIRONMENT = "Development"
    DOTNET_ENVIRONMENT = "Development"
    ASPNETCORE_URLS = "http://127.0.0.1:$Port"
    ConnectionStrings__DefaultConnection = "Data Source=$(Join-Path $runRoot 'qa.db')"
    DataProtection__KeyRingPath = (Join-Path $runRoot "keys")
    Email__FrontendUrl = "http://127.0.0.1:5174"
    Email__FromAddress = ""
    Email__Smtp__Host = ""
    Email__Smtp__Username = ""
    Email__Smtp__Password = ""
    Google__ClientId = ""
    Google__ClientSecret = ""
    Google__RedirectUri = ""
    BootstrapAdmin__Email = ""
    BootstrapAdmin__Password = ""
    Cors__AllowedOrigins__0 = "http://127.0.0.1:5174"
    Logging__LogLevel__Default = "Warning"
    "Logging__LogLevel__Microsoft.AspNetCore" = "Warning"
}
foreach ($key in $settings.Keys) { $start.EnvironmentVariables[$key] = $settings[$key] }
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes); $start.EnvironmentVariables['Jwt__Secret'] = [Convert]::ToBase64String($bytes) }
finally { $rng.Dispose(); [Array]::Clear($bytes, 0, $bytes.Length) }
$api = [System.Diagnostics.Process]::Start($start)
$start.EnvironmentVariables.Clear()
@{ Database = (Join-Path $runRoot 'qa.db'); ApiProcessId = $api.Id; Port = $Port } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $apiRoot '.qa-runtime\current.json') -Encoding UTF8
try { $api.WaitForExit(); exit $api.ExitCode }
finally { if (!$api.HasExited) { $api.Kill() }; $api.Dispose() }
