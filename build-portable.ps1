$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot
$csproj = Join-Path $projectDir "ScreenSearchOverlay.csproj"
$publishDir = Join-Path $projectDir "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
$outputDir = Join-Path $projectDir "dist"
$enigmaPath = "C:\Program Files (x86)\Enigma Virtual Box\enigmavbconsole.exe"

Write-Host "=== Building Release ===" -ForegroundColor Cyan
dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "=== Packaging with Enigma Virtual Box ===" -ForegroundColor Cyan

if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

$inputExe = Join-Path $publishDir "ScreenSearchOverlay.exe"
$outputExe = Join-Path $outputDir "ScreenSearchOverlay.exe"

# Scan all DLLs to embed
$files = Get-ChildItem $publishDir -File | Where-Object { $_.Name -ne "ScreenSearchOverlay.exe" -and $_.Extension -ne ".pdb" }

$filesXml = ""
foreach ($file in $files) {
    $filesXml += @"
          <File>
            <Type>2</Type>
            <Name>$($file.Name)</Name>
            <File>$($file.FullName)</File>
            <ActiveX>False</ActiveX>
            <ActiveXInstall>False</ActiveXInstall>
            <Action>0</Action>
            <OverwriteDateTime>False</OverwriteDateTime>
            <OverwriteAttributes>False</OverwriteAttributes>
            <PassCommandLine>False</PassCommandLine>
            <HideFromDialogs>0</HideFromDialogs>
          </File>`n
"@
}

$evbContent = @"
<?xml version="1.0" encoding="windows-1252"?>
<>
  <InputFile>$inputExe</InputFile>
  <OutputFile>$outputExe</OutputFile>
  <Files>
    <Enabled>True</Enabled>
    <DeleteExtractedOnExit>False</DeleteExtractedOnExit>
    <CompressFiles>False</CompressFiles>
    <Files>
      <File>
        <Type>3</Type>
        <Name>%DEFAULT FOLDER%</Name>
        <Action>0</Action>
        <OverwriteDateTime>False</OverwriteDateTime>
        <OverwriteAttributes>False</OverwriteAttributes>
        <HideFromDialogs>0</HideFromDialogs>
        <Files>
$filesXml
        </Files>
      </File>
    </Files>
  </Files>
  <Registries>
    <Enabled>False</Enabled>
    <Registries>
      <Registry>
        <Type>1</Type>
        <Virtual>True</Virtual>
        <Name>Classes</Name>
        <ValueType>0</ValueType>
        <Value/>
        <Registries/>
      </Registry>
      <Registry>
        <Type>1</Type>
        <Virtual>True</Virtual>
        <Name>User</Name>
        <ValueType>0</ValueType>
        <Value/>
        <Registries/>
      </Registry>
      <Registry>
        <Type>1</Type>
        <Virtual>True</Virtual>
        <Name>Machine</Name>
        <ValueType>0</ValueType>
        <Value/>
        <Registries/>
      </Registry>
      <Registry>
        <Type>1</Type>
        <Virtual>True</Virtual>
        <Name>Users</Name>
        <ValueType>0</ValueType>
        <Value/>
        <Registries/>
      </Registry>
      <Registry>
        <Type>1</Type>
        <Virtual>True</Virtual>
        <Name>Config</Name>
        <ValueType>0</ValueType>
        <Value/>
        <Registries/>
      </Registry>
    </Registries>
  </Registries>
  <Packaging>
    <Enabled>False</Enabled>
  </Packaging>
  <Options>
    <ShareVirtualSystem>False</ShareVirtualSystem>
    <MapExecutableWithTemporaryFile>False</MapExecutableWithTemporaryFile>
    <TemporaryFileMask/>
    <AllowRunningOfVirtualExeFiles>True</AllowRunningOfVirtualExeFiles>
    <ProcessesOfAnyPlatforms>False</ProcessesOfAnyPlatforms>
  </Options>
  <Storage>
    <Files>
      <Enabled>False</Enabled>
      <Folder>%DEFAULT FOLDER%\</Folder>
      <RandomFileNames>False</RandomFileNames>
      <EncryptContent>False</EncryptContent>
    </Files>
  </Storage>
</>
"@

$evbFile = Join-Path $projectDir "build.evb"
$evbContent | Out-File -FilePath $evbFile -Encoding Default

& $enigmaPath $evbFile
if ($LASTEXITCODE -ne 0) { throw "Enigma Virtual Box failed" }

Remove-Item $evbFile -Force

$size = [math]::Round((Get-Item $outputExe).Length / 1MB, 1)
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "Portable exe: $outputExe ($size MB)" -ForegroundColor Green
