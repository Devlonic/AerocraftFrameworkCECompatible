<#
.SYNOPSIS
    Runs the in-game end-to-end self test of Aerocraft Framework in a separate RimWorld data folder.

.DESCRIPTION
    Creates <DataDir>\Config\ModsConfig.xml and Prefs.xml (your own config and saves are never touched),
    starts RimWorld with -quicktest -af_autotest, waits for it to quit and prints the PASS/FAIL report.
    The mod must be linked into RimWorld\Mods (see README).

.EXAMPLE
    .\tools\Run-AutoTest.ps1 -DataDir C:\Temp\af-test-ce
    .\tools\Run-AutoTest.ps1 -DataDir C:\Temp\af-test-vanilla -NoCombatExtended
#>
param(
    [string]$RimWorldDir = "D:\Games\Steam\steamapps\common\RimWorld",
    [Parameter(Mandatory = $true)][string]$DataDir,
    [switch]$NoCombatExtended,
    [switch]$NoAddons,
    [string]$Language = "English",
    [int]$TimeoutMinutes = 25
)

$ErrorActionPreference = "Stop"
$config = Join-Path $DataDir "Config"
New-Item -ItemType Directory -Force -Path $config | Out-Null

$mods = @("brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology", "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly")
if (-not $NoCombatExtended) { $mods += "ceteam.combatextended" }
if (-not $NoAddons) { $mods += @("smashphil.vehicleframework", "rimthunder.core") }
$mods += "myde.aerocraftframework"
if (-not $NoAddons) { $mods += "layla.rimthunder.gruppakrovi" }

$modLines = ($mods | ForEach-Object { "    <li>$_</li>" }) -join "`n"
@"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>1.5.4409 rev1120</version>
  <activeMods>
$modLines
  </activeMods>
  <knownExpansions>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.anomaly</li>
  </knownExpansions>
</ModsConfigData>
"@ | Set-Content -Encoding UTF8 (Join-Path $config "ModsConfig.xml")

# Autosaves must not be every tick (autosaveIntervalDays 0 makes the game crawl).
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
  <disableQuickStartCryptoSickness>True</disableQuickStartCryptoSickness>
  <langFolderName>$Language</langFolderName>
  <fullscreen>False</fullscreen>
  <screenWidth>1280</screenWidth>
  <screenHeight>720</screenHeight>
</PrefsData>
"@ | Set-Content -Encoding UTF8 (Join-Path $config "Prefs.xml")

$report = Join-Path $DataDir "report.txt"
$gameLog = Join-Path $DataDir "rw.log"
Remove-Item $report, $gameLog -ErrorAction SilentlyContinue

$exe = Join-Path $RimWorldDir "RimWorldWin64.exe"
$arguments = @("-quicktest", "-af_autotest", "-af_autotest_quit", "-af_report=$report", "-savedatafolder=$DataDir", "-logFile", $gameLog, "-popupwindow")
Write-Host "Starting RimWorld with mods: $($mods -join ', ')"
$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    Write-Warning "Timed out after $TimeoutMinutes minutes; killing RimWorld."
    $process.Kill()
}

if (Test-Path $report) {
    $lines = Get-Content $report
    $lines | Where-Object { $_ -match "FAIL|DONE|error:" } | ForEach-Object { Write-Host $_ }
    $summary = $lines | Where-Object { $_ -match "DONE:" } | Select-Object -Last 1
    if ($summary -match ", 0 failures") { Write-Host "RESULT: PASS" -ForegroundColor Green; exit 0 }
    Write-Host "RESULT: FAIL (see $report)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: no report (see $gameLog)" -ForegroundColor Red
exit 2
