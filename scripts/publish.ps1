param(
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$projectPath = Join-Path $repositoryRoot "src\KeyStats.App\KeyStats.App.csproj"
$readmePath = Join-Path $repositoryRoot "docs\USER_GUIDE.md"
$fullOutput = Join-Path $artifactsRoot "v1.0-full"
$liteOutput = Join-Path $artifactsRoot "v1.0-lite"

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
foreach ($target in @($fullOutput, $liteOutput)) {
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 artifacts 目录以外的路径：$resolvedTarget"
    }

    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $fullOutput

New-Item -ItemType Directory -Path (Join-Path $fullOutput "Data") -Force | Out-Null
Copy-Item -LiteralPath $readmePath -Destination (Join-Path $fullOutput "README.md")

dotnet publish $projectPath -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $liteOutput

New-Item -ItemType Directory -Path (Join-Path $liteOutput "Data") -Force | Out-Null
Copy-Item -LiteralPath $readmePath -Destination (Join-Path $liteOutput "README.md")

$fullZip = Join-Path $artifactsRoot "KeyStats-V1.0-full-win-x64.zip"
$liteZip = Join-Path $artifactsRoot "KeyStats-V1.0-lite-win-x64.zip"
Compress-Archive -Path (Join-Path $fullOutput "*") -DestinationPath $fullZip -CompressionLevel Optimal -Force
Compress-Archive -Path (Join-Path $liteOutput "*") -DestinationPath $liteZip -CompressionLevel Optimal -Force

$checksums = @($fullZip, $liteZip) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_))"
}
[System.IO.File]::WriteAllLines((Join-Path $artifactsRoot "SHA256SUMS.txt"), $checksums)

Write-Host "发布完成：$artifactsRoot"

