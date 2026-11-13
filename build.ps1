$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Test-Path "$PSScriptRoot\dist") { Remove-Item "$PSScriptRoot\dist" -Recurse -Force }
dotnet build -c Release -o "$PSScriptRoot\dist\bin"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item "$PSScriptRoot\package\*" "$PSScriptRoot\dist" -Recurse -Force
if (!(Test-Path "$PSScriptRoot\.tools\LogiPluginTool.exe")) {
    dotnet tool install LogiPluginTool --tool-path "$PSScriptRoot\.tools"
    if ($LASTEXITCODE -ne 0) { throw 'Could not install LogiPluginTool.' }
}
& "$PSScriptRoot\.tools\LogiPluginTool.exe" pack "$PSScriptRoot\dist" "$PSScriptRoot\HoverHaptics.lplug4"
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
& "$PSScriptRoot\.tools\LogiPluginTool.exe" verify "$PSScriptRoot\HoverHaptics.lplug4"
if ($LASTEXITCODE -ne 0) { throw 'Package verification failed.' }
Write-Host 'Built HoverHaptics.lplug4. Double-click it to install.'
