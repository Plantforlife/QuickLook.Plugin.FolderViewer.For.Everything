$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path "$PSScriptRoot\.."
$releaseDirectory = Join-Path $projectRoot 'bin\Release'
$outputPath = Join-Path $projectRoot 'QuickLook.Plugin.FolderViewer.For.Everything.qlplugin'
$temporaryZip = [IO.Path]::ChangeExtension($outputPath, '.zip')

if (-not (Test-Path (Join-Path $releaseDirectory 'QuickLook.Plugin.FolderViewer.For.Everything.dll'))) {
    throw 'Release build output was not found.'
}

Remove-Item $outputPath, $temporaryZip -ErrorAction SilentlyContinue
$files = @(
    Get-Item (Join-Path $releaseDirectory 'QuickLook.Plugin.FolderViewer.For.Everything.dll')
    Get-Item (Join-Path $releaseDirectory 'QuickLook.Plugin.Metadata.config')
    Get-Item (Join-Path $releaseDirectory 'THIRD_PARTY_NOTICES.txt')
    Get-Item (Join-Path $releaseDirectory 'Translations.config')
)
Compress-Archive -Path $files.FullName -DestinationPath $temporaryZip
Move-Item $temporaryZip $outputPath

Write-Host "Created $outputPath"
