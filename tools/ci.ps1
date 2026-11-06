# Compile check + EditMode/PlayMode tests in batchmode. Close the Unity Editor first.
# Usage: powershell -ExecutionPolicy Bypass -File tools\ci.ps1 [-Tests]
param([string]$UnityVersion = "2021.3.17f1", [switch]$Tests)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$editor = @("C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor","D:\Unity\Hub\Editor\$UnityVersion\Editor") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $editor) { throw "Unity $UnityVersion not found" }
$unity = Join-Path $editor "Unity.exe"
$out = Join-Path $root "Builds\ci"; New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "== compile ($UnityVersion)"
$p = Start-Process $unity -ArgumentList @("-batchmode","-nographics","-quit","-projectPath","`"$root`"","-logFile","`"$out\compile.log`"") -Wait -PassThru
$errs = Select-String "$out\compile.log" -Pattern "error CS\d+" | Select-Object -ExpandProperty Line -Unique
if ($errs) { $errs | Select-Object -First 40; Write-Host "COMPILE FAILED"; exit 1 }
if ($p.ExitCode -ne 0) { Select-String "$out\compile.log" -Pattern "Error|Exception|Aborting" | Select-Object -Last 20; Write-Host "UNITY EXIT $($p.ExitCode)"; exit 1 }
Write-Host "compile OK"

if ($Tests) {
  foreach ($plat in "EditMode","PlayMode") {
    Write-Host "== $plat tests"
    Start-Process $unity -ArgumentList @("-batchmode","-projectPath","`"$root`"","-runTests","-testPlatform",$plat,"-testResults","`"$out\$plat.xml`"","-logFile","`"$out\$plat.log`"") -Wait | Out-Null
    if (Test-Path "$out\$plat.xml") {
      [xml]$x = Get-Content "$out\$plat.xml"; $r = $x.'test-run'
      Write-Host "$plat : total $($r.total) passed $($r.passed) failed $($r.failed)"
      if ([int]$r.failed -gt 0) { exit 1 }
    } else { Write-Host "$plat : no results file (see $plat.log)"; exit 1 }
  }
}
Write-Host "CI OK"
