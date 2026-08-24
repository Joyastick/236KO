# Compiles CloakBootstrap.dll directly with cl.exe/link.exe (no vcvarsall.bat, no vcxproj/MSBuild
# integration for C++ needed) — just sets up the include/lib paths by hand and invokes the MSVC
# toolset found under Visual Studio 2019 Community.

$ErrorActionPreference = "Stop"

$MsvcRoot = "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Tools\MSVC\14.29.30133"
$WinSdkInclude = "C:\Program Files (x86)\Windows Kits\10\Include\10.0.19041.0"
$WinSdkLib = "C:\Program Files (x86)\Windows Kits\10\Lib\10.0.19041.0"
$NetHostDir = "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\9.0.10\runtimes\win-x64\native"

$ClExe = Join-Path $MsvcRoot "bin\Hostx64\x64\cl.exe"
$env:PATH = (Join-Path $MsvcRoot "bin\Hostx64\x64") + ";" + $env:PATH

$OutDir = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Kill anything that might have the previous CloakBootstrap.dll/payload loaded, or the linker
# can't overwrite it (LNK1104).
Get-Process -Name "FakeGame" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "MotionInput.App" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

$includeArgs = @(
    "/I$MsvcRoot\include",
    "/I$WinSdkInclude\ucrt",
    "/I$WinSdkInclude\um",
    "/I$WinSdkInclude\shared",
    "/I$NetHostDir"
)

$libArgs = @(
    "/LIBPATH:$MsvcRoot\lib\x64",
    "/LIBPATH:$WinSdkLib\ucrt\x64",
    "/LIBPATH:$WinSdkLib\um\x64",
    "/LIBPATH:$NetHostDir"
)

$source = Join-Path $PSScriptRoot "CloakBootstrap.cpp"
$outDll = Join-Path $OutDir "CloakBootstrap.dll"

$clArgs = @(
    "/nologo", "/LD", "/EHsc", "/std:c++17", "/MD", "/W3"
) + $includeArgs + @(
    $source,
    "/Fo:$OutDir\",
    "/link"
) + $libArgs + @(
    "nethost.lib", "kernel32.lib", "user32.lib",
    "/OUT:$outDll"
)

Write-Host "Running: cl.exe $($clArgs -join ' ')"
Push-Location $PSScriptRoot
try {
    & $ClExe @clArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "cl.exe failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

Write-Host "Built: $outDll"

# Copy the nethost.dll dependency alongside (dynamically loaded by CloakBootstrap.dll via
# GetProcAddress after LoadLibraryW inside the target process, so it must be findable).
Copy-Item -Force (Join-Path $NetHostDir "nethost.dll") $OutDir

# --- Test-harness staging (everything below is throwaway dev-loop plumbing, not part of the
# actual 236KO app) -----------------------------------------------------------------------

$PayloadOutDir = "C:\Users\aldri\Documents\GitHub\236KO\src\MotionInput.Cloak.Payload\bin\Debug\net9.0-windows\win-x64"
$FakeGameDir = "C:\Users\aldri\AppData\Local\Temp\claude\c--Users-aldri-Documents-GitHub-MotionInputs2XKO\cd5808f5-d276-4bcb-9af6-48acbe50f28c\scratchpad\cloaktest\FakeGame\bin\Debug\net9.0-windows"
$DebugLogManaged = "C:\Temp\236KO_cloak_debug_managed.log"
$DebugLogNative = "C:\Temp\236KO_cloak_debug.log"

# Kill any running FakeGame.exe so its previously-injected DLL isn't locked when we overwrite it.
Get-Process -Name "FakeGame" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $PayloadOutDir) {
    Write-Host "Staging payload output from $PayloadOutDir into $OutDir ..."
    Get-ChildItem $PayloadOutDir -Filter "*.dll" | Copy-Item -Destination $OutDir -Force
    Get-ChildItem $PayloadOutDir -Filter "*.json" | Copy-Item -Destination $OutDir -Force

    if (Test-Path $FakeGameDir) {
        Write-Host "Staging nethost.dll + FASM DLLs into FakeGame's own directory (needed for its own hostfxr/Reloaded.Hooks resolution) ..."
        Copy-Item -Force (Join-Path $NetHostDir "nethost.dll") $FakeGameDir
        Copy-Item -Force (Join-Path $PayloadOutDir "FASM.DLL") $FakeGameDir
        Copy-Item -Force (Join-Path $PayloadOutDir "FASMX64.DLL") $FakeGameDir
    }
}

foreach ($log in @($DebugLogManaged, $DebugLogNative)) {
    if (Test-Path $log) { Remove-Item -Force $log }
}

Write-Host "Staging complete. Debug logs cleared."

# --- Ship the cloak runtime alongside the real app ----------------------------------------

$AppCloakDir = "C:\Users\aldri\Documents\GitHub\236KO\src\MotionInput.App\Cloak"
New-Item -ItemType Directory -Force -Path $AppCloakDir | Out-Null

Write-Host "Staging cloak runtime into $AppCloakDir ..."
Get-ChildItem $OutDir -Filter "*.dll" | Copy-Item -Destination $AppCloakDir -Force
Get-ChildItem $OutDir -Filter "*.DLL" | Copy-Item -Destination $AppCloakDir -Force
Get-ChildItem $OutDir -Filter "*.json" | Copy-Item -Destination $AppCloakDir -Force
Write-Host "App cloak runtime staged."

# --- Also stage into the CloakHost test harness's own "Cloak" subfolder, so it exercises the
#     exact same AppContext.BaseDirectory + "Cloak" lookup ProcessCloakService uses in the real app.
$CloakHostCloakDir = "C:\Users\aldri\AppData\Local\Temp\claude\c--Users-aldri-Documents-GitHub-MotionInputs2XKO\cd5808f5-d276-4bcb-9af6-48acbe50f28c\scratchpad\cloaktest\CloakHost\bin\Debug\net9.0-windows\Cloak"
if (Test-Path (Split-Path $CloakHostCloakDir -Parent)) {
    New-Item -ItemType Directory -Force -Path $CloakHostCloakDir | Out-Null
    Get-ChildItem $OutDir -Filter "*.dll" | Copy-Item -Destination $CloakHostCloakDir -Force
    Get-ChildItem $OutDir -Filter "*.DLL" | Copy-Item -Destination $CloakHostCloakDir -Force
    Get-ChildItem $OutDir -Filter "*.json" | Copy-Item -Destination $CloakHostCloakDir -Force
    Write-Host "CloakHost test harness Cloak folder staged."
}
