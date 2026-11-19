param([int]$DatabasePort = 5433, [int]$RedisPort = 6379, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$seedBackendRoot = Split-Path $PSScriptRoot -Parent
$seedEnvPath = Join-Path $seedBackendRoot '.env'
if (!(Test-Path -LiteralPath $seedEnvPath)) { throw 'Configure backend/.env before running this script.' }
$seedBranch = & git -C $seedBackendRoot branch --show-current
if ($seedBranch -in @('main', 'develop')) { throw 'Run the development dataset from a development branch.' }
$seedOriginalEnvironment = @{}
function Set-SeedEnvironment([string]$Name, [string]$Value) {
    if (!$seedOriginalEnvironment.ContainsKey($Name)) { $seedOriginalEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process') }
    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}
try {
    foreach ($seedLine in Get-Content -LiteralPath $seedEnvPath) {
        if ($seedLine -match '^\s*([A-Z][A-Z0-9_]*)=(.*)$') {
            $seedName = $Matches[1]
            $seedValue = $Matches[2].Trim()
            if (($seedValue.StartsWith('"') -and $seedValue.EndsWith('"')) -or ($seedValue.StartsWith("'") -and $seedValue.EndsWith("'"))) {
                $seedValue = $seedValue.Substring(1, $seedValue.Length - 2)
            }
            Set-SeedEnvironment $seedName $seedValue
        }
    }
    if ([string]::IsNullOrWhiteSpace($env:DEV_SEED_DASHBOARD_PASSWORD) -or $env:DEV_SEED_DASHBOARD_PASSWORD.Length -lt 12) {
        throw 'Set DEV_SEED_DASHBOARD_PASSWORD (at least 12 characters) in the ignored backend/.env file.'
    }
    Set-SeedEnvironment 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-SeedEnvironment 'DOTNET_ENVIRONMENT' 'Development'
    Set-SeedEnvironment 'DATABASE_URL' "Host=127.0.0.1;Port=$DatabasePort;Database=finmate_main;Username=finmate;Password=finmate"
    Set-SeedEnvironment 'REDIS_URL' "127.0.0.1:$RedisPort"
    Set-SeedEnvironment 'AI_SERVICE_URL' 'http://127.0.0.1:8000'
    if (!$NoBuild) {
        & dotnet build (Join-Path $seedBackendRoot 'FinMate.API/FinMate.API.csproj') --no-restore -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Build failed. The dataset was not loaded.' }
    }
    & dotnet (Join-Path $seedBackendRoot 'FinMate.API/bin/Debug/net9.0/FinMate.API.dll') --seed-dashboard-dev-only true
    if ($LASTEXITCODE -ne 0) { throw 'Development dataset failed; inspect the error and retain the backup.' }
} finally {
    foreach ($seedName in $seedOriginalEnvironment.Keys) { [Environment]::SetEnvironmentVariable($seedName, $seedOriginalEnvironment[$seedName], 'Process') }
}
