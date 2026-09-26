<#
.SYNOPSIS
    Loads one of your saves in a copy of your RimWorld configuration and checks the aircraft in it.

.DESCRIPTION
    Copies your ModsConfig.xml, mod settings and the save into <DataDir> (your own files are only read),
    starts RimWorld with -af_savecheck -af_loadsave=<Save>, and prints the PASS/FAIL report: aircraft load intact,
    legacy nested follow targets are dropped, colonists reload the aircraft, and saving again stays small.

.EXAMPLE
    .\tools\Run-SaveCheck.ps1 -Save testreload -DataDir C:\Temp\af-savecheck
    .\tools\Run-SaveCheck.ps1 -Save repaired -SaveFile C:\Temp\testreload_repaired.rws -DataDir C:\Temp\af-savecheck
#>
param(
    [string]$RimWorldDir = "D:\Games\Steam\steamapps\common\RimWorld",
    [string]$UserDataDir = (Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios"),
    [Parameter(Mandatory = $true)][string]$Save,
    [Parameter(Mandatory = $true)][string]$DataDir,
    [string]$SaveFile,
    [int]$TimeoutMinutes = 40
)

$ErrorActionPreference = "Stop"
$config = Join-Path $DataDir "Config"
$saves = Join-Path $DataDir "Saves"
New-Item -ItemType Directory -Force -Path $config, $saves | Out-Null

Copy-Item (Join-Path $UserDataDir "Config\ModsConfig.xml") $config -Force
Get-ChildItem (Join-Path $UserDataDir "Config") -Filter "Mod_*.xml" | Copy-Item -Destination $config -Force
if (-not $SaveFile) { $SaveFile = Join-Path $UserDataDir "Saves\$Save.rws" }
Copy-Item $SaveFile (Join-Path $saves "$Save.rws") -Force

@"
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <volumeMaster>0</volumeMaster>
  <runInBackground>True</runInBackground>
  <devMode>True</devMode>
  <autosaveIntervalDays>60</autosaveIntervalDays>
  <pauseOnError>False</pauseOnError>
  <pauseOnLoad>False</pauseOnLoad>
  <automaticPauseMode>Never</automaticPauseMode>
  <resetModsConfigOnCrash>False</resetModsConfigOnCrash>
  <langFolderName>English</langFolderName>
  <fullscreen>False</fullscreen>
  <screenWidth>1280</screenWidth>
  <screenHeight>720</screenHeight>
</PrefsData>
"@ | Set-Content -Encoding UTF8 (Join-Path $config "Prefs.xml")

$report = Join-Path $DataDir "report.txt"
$gameLog = Join-Path $DataDir "rw.log"
Remove-Item $report, $gameLog -ErrorAction SilentlyContinue
$arguments = @("-af_savecheck", "-af_loadsave=$Save", "-af_autotest_quit", "-af_report=$report", "-savedatafolder=$DataDir", "-logFile", $gameLog, "-popupwindow")
$process = Start-Process -FilePath (Join-Path $RimWorldDir "RimWorldWin64.exe") -ArgumentList $arguments -PassThru
if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    Write-Warning "Timed out after $TimeoutMinutes minutes; killing RimWorld."
    $process.Kill()
}
if (Test-Path $report) {
    Get-Content $report | ForEach-Object { Write-Host $_ }
    if ((Get-Content $report | Select-Object -Last 1) -match ", 0 failures") { exit 0 }
    exit 1
}
Write-Host "No report (see $gameLog)" -ForegroundColor Red
exit 2

