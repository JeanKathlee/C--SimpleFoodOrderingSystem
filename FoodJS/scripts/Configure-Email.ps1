param(
    [string]$SmtpHost = "smtp.gmail.com",
    [int]$SmtpPort = 587
)

$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\backend-csharp\FoodJS.Api\FoodJS.Api.csproj")).Path
$senderEmail = Read-Host "Sender email address"
if ([string]::IsNullOrWhiteSpace($senderEmail)) { throw "A sender email is required." }
Write-Host "Use your provider's SMTP credential/app password, NOT the Google OAuth client secret."
$securePassword = Read-Host "SMTP app password" -AsSecureString
$smtpPassword = $null
$settings = @{}
try {
    $smtpPassword = [System.Net.NetworkCredential]::new("", $securePassword).Password
    if ([string]::IsNullOrWhiteSpace($smtpPassword)) { throw "An SMTP password is required." }
    if ($SmtpHost -eq "smtp.gmail.com") {
        $smtpPassword = $smtpPassword -replace '\s', ''
        if ($smtpPassword.StartsWith("GOCSPX-")) { throw "That is a Google OAuth client secret. Use a Gmail app password for SMTP instead." }
    }
    $settings = @{
        "Email:FromAddress" = $senderEmail.Trim()
        "Email:Smtp:Username" = $senderEmail.Trim()
        "Email:Smtp:Host" = $SmtpHost
        "Email:Smtp:Port" = $SmtpPort
        "Email:Smtp:Password" = $smtpPassword
        "Email:FrontendUrl" = "http://localhost:5173"
    }
    $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw "Could not save SMTP settings." }
    Write-Host "SMTP settings saved locally. Restart the backend, then test a signup."
}
finally {
    $settings.Clear()
    $securePassword.Dispose()
    Remove-Variable smtpPassword, securePassword, settings -ErrorAction SilentlyContinue
}
