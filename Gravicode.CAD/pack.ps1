# Packs every library, the gcad tool and the templates into ./artifacts/packages.
param([string]$Configuration = "Release", [string]$Output = "$PSScriptRoot/artifacts/packages")
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Output | Out-Null
Get-ChildItem "$PSScriptRoot/src" -Filter *.csproj -Recurse | ForEach-Object {
    dotnet pack $_.FullName -c $Configuration -o $Output --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "pack failed: $($_.Name)" }
}
dotnet pack "$PSScriptRoot/templates/Gravicode.CAD.Templates/Gravicode.CAD.Templates.csproj" -c $Configuration -o $Output --nologo -v q
Get-ChildItem $Output -Filter *.nupkg | Sort-Object Name | Format-Table Name, Length
