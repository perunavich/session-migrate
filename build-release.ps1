# Builds standalone Windows binaries into publish/ (gitignored — ship these as release assets).
# Each is a self-contained single-file exe: no .NET install needed (Win11 already has the WebView2
# runtime the UI uses).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:DebugType=none', '-p:DebugSymbols=false')

# UI: bundle the web assets and the WebView2 native loader into the single file.
dotnet publish Ui/SessionMigrate.Ui.csproj @common `
    -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o publish/ui

# CLI: keep assets/ loose next to the exe so the harvest extension folder is loadable into Chrome.
dotnet publish Cli/SessionMigrate.Cli.csproj @common -o publish/cli

Write-Host ''
Write-Host 'Done:'
Write-Host '  GUI : publish/ui/SessionMigrate.Ui.exe'
Write-Host '  CLI : publish/cli/session-migrate.exe   (+ assets/cookie-export-ext for `harvest`)'
