$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path "$PSScriptRoot\.."
$releaseDirectory = Join-Path $projectRoot 'bin\Release'
$outputPath = Join-Path $projectRoot 'QuickLook.Plugin.FolderViewer.For.Everything.qlplugin'
$temporaryZip = Join-Path $projectRoot ('package-' + [guid]::NewGuid().ToString('N') + '.zip')

if (-not (Test-Path (Join-Path $releaseDirectory 'QuickLook.Plugin.FolderViewer.For.Everything.dll'))) {
    throw 'Release build output was not found.'
}

$files = @(
    Get-Item (Join-Path $releaseDirectory 'QuickLook.Plugin.FolderViewer.For.Everything.dll')
    Get-Item (Join-Path $releaseDirectory 'QuickLook.Plugin.Metadata.config')
    Get-Item (Join-Path $releaseDirectory 'THIRD_PARTY_NOTICES.txt')
    Get-Item (Join-Path $releaseDirectory 'Translations.config')
)
try {
    Compress-Archive -Path $files.FullName -DestinationPath $temporaryZip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
    try {
        $expected = @($files.Name | Sort-Object)
        $actual = @($archive.Entries.FullName | Sort-Object)
        if (@(Compare-Object $expected $actual).Count -ne 0) {
            throw 'Package contents do not match the four expected plugin files.'
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination $outputPath -Force
} finally {
    if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip }
}

Write-Host "Created $outputPath"
