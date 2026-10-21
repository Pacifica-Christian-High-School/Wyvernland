param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.0.32f1\Editor\Unity.exe',
    [string]$OutputPath = '',
    [string]$PythonPath = '',
    [switch]$Fresh
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $PythonPath) {
    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    $bundledPython = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    if ($pythonCommand -and $pythonCommand.Source -notlike '*\WindowsApps\python.exe') { $PythonPath = $pythonCommand.Source }
    elseif (Test-Path -LiteralPath $bundledPython) { $PythonPath = $bundledPython }
    else { throw 'Python 3.9 or newer is required. Pass its executable with -PythonPath.' }
}
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
if (-not $OutputPath) { $OutputPath = Join-Path $projectRoot 'Sites\Wyvernland\public' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Artifacts'))
$cacheMarker = Join-Path $artifactsRoot 'WebBuildProject.txt'
$snapshot = ''
if (-not $Fresh -and (Test-Path -LiteralPath $cacheMarker)) {
    $snapshot = [IO.Path]::GetFullPath([IO.File]::ReadAllText($cacheMarker).Trim())
    if (-not $snapshot.StartsWith($artifactsRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $snapshot) -notmatch '^WebBuild-[0-9a-f]{12}$') { throw 'Invalid browser build cache path.' }
    if (-not (Test-Path -LiteralPath $snapshot)) { $snapshot = '' }
}
if (-not $snapshot) { $snapshot = Join-Path $artifactsRoot ('WebBuild-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)) }
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
if ((Get-Item -LiteralPath $snapshot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The browser build cache must not be a link.' }
if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like ('*' + $snapshot + '*') }) { throw 'The browser build cache is already open in Unity.' }
foreach ($directory in @('Assets', 'Packages', 'ProjectSettings')) {
    $destination = [IO.Path]::GetFullPath((Join-Path $snapshot $directory))
    if (-not $destination.StartsWith($snapshot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe snapshot destination.' }
    if ((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Snapshot directories must not be links.' }
    & robocopy (Join-Path $projectRoot $directory) $destination /MIR /MT:16 /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not synchronize $directory." }
}
# Reuse packages already installed for this project instead of downloading them again.
$packageCache = Join-Path $projectRoot 'Library\PackageCache'
if (Test-Path -LiteralPath $packageCache) {
    New-Item -ItemType Directory -Path (Join-Path $snapshot 'Library') -Force | Out-Null
    $cacheDestination = Join-Path $snapshot 'Library\PackageCache'
    & robocopy $packageCache $cacheDestination /E /MT:16 /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy the installed Unity packages (robocopy exit $LASTEXITCODE)." }
}
$logPath = Join-Path $snapshot 'web-build.log'
Write-Output "Building browser game from $snapshot"
Write-Output "Build log: $logPath"
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $snapshot + '"'),
    '-buildTarget', 'WebGL', '-executeMethod', 'Wyvernland.Editor.WebBrowserBuild.Run',
    '-wyvernlandWebOutput', ('"' + $OutputPath + '"'), '-logFile', ('"' + $logPath + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
# Unity can leave a compiler server running after its editor process exits.
# Wait for the actual export process rather than all of its descendants.
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $OutputPath 'index.html'))) {
    Get-Content -LiteralPath $logPath -Tail 65
    throw "Browser build failed (exit $($process.ExitCode)). Log: $logPath"
}
[IO.File]::WriteAllText($cacheMarker, $snapshot, [Text.UTF8Encoding]::new($false))
& $PythonPath (Join-Path $PSScriptRoot 'prepare_web.py') $OutputPath
if ($LASTEXITCODE -ne 0) { throw 'Browser build completed, but preparing the website failed. Run Tools/prepare_web.py against the export.' }
Write-Output "Browser build ready: $OutputPath"
