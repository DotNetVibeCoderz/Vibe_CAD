# Publishes ./artifacts/packages/*.nupkg to nuget.org.
# The API key is read from a local file (never committed) — default: ../PackageCredentials.txt,
# using the first line that looks like a NuGet key ("oy2..."), or pass -ApiKey.
param(
    [string]$KeyFile = "$PSScriptRoot/../PackageCredentials.txt",
    [string]$ApiKey = "",
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$Packages = "$PSScriptRoot/artifacts/packages"
)
$ErrorActionPreference = "Stop"
if (-not $ApiKey) {
    if (-not (Test-Path $KeyFile)) { throw "No API key: pass -ApiKey or create $KeyFile" }
    $ApiKey = (Get-Content $KeyFile | Where-Object { $_ -match "oy2[a-z0-9]{40,}" } | ForEach-Object { $Matches[0] } | Select-Object -First 1)
    if (-not $ApiKey) { throw "No NuGet API key found in $KeyFile" }
}
Get-ChildItem $Packages -Filter *.nupkg | ForEach-Object {
    dotnet nuget push $_.FullName --api-key $ApiKey --source $Source --skip-duplicate
}
