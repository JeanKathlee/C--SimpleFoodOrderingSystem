$ErrorActionPreference = "Stop"
$apiRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\backend-csharp\FoodJS.Api")).Path
$stateFile = Join-Path $apiRoot ".demo-runtime\phone-demo.json"
if (!(Test-Path -LiteralPath $stateFile)) { throw "No phone demo state was found." }
$state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json

# Verify both executable and assembly before stopping anything: PIDs can be reused.
foreach ($entry in @(@{ Id = $state.ApiProcessId; Kind = "api" }, @{ Id = $state.TunnelProcessId; Kind = "tunnel" })) {
    if ([int]$entry.Id -le 0) { continue }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$entry.Id)" -ErrorAction Stop
    if ($null -eq $process) { continue }
    $matches = if ($entry.Kind -eq "api") {
        $process.Name -eq "dotnet.exe" -and $process.CommandLine.Contains([string]$state.ApiAssembly)
    } else {
        $process.Name -eq "cloudflared.exe" -and $process.CommandLine.Contains("http://127.0.0.1:$($state.Port)")
    }
    if (!$matches) { throw "Process identity changed. Refusing to stop an unrelated process." }
    Stop-Process -Id ([int]$entry.Id) -ErrorAction Stop
}
Write-Host "Phone demo stopped. Normal localhost processes/data/settings were not changed."
