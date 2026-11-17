# Builds the board, the contract and every tool under Tools\ ; the tools copy themselves into
# ProjectManagementBoard\bin\<Configuration>\net10.0-windows\Plugins\<ToolName>\ .
#   .\build-all.ps1              Debug build
#   .\build-all.ps1 -Release     Release build
#   .\build-all.ps1 -Run         build, then start the board
param(
    [switch]$Release,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$config = if ($Release) { 'Release' } else { 'Debug' }

dotnet build "$PSScriptRoot\ProjectManagementBoard.slnx" -c $config
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$out = "$PSScriptRoot\ProjectManagementBoard\bin\$config\net10.0-windows"
Write-Host "`nBoard: $out\ProjectManagementBoard.exe"
Write-Host "Tools:"
Get-ChildItem "$out\Plugins" -Directory -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $($_.Name)" }

if ($Run) { Start-Process "$out\ProjectManagementBoard.exe" }
