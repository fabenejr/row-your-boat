<#
.SYNOPSIS
    Prepares a Viking Oarsmen release: bumps the version, stamps the changelog, builds and packages.

.DESCRIPTION
    1. Checks the working tree is clean and the tag doesn't exist yet.
    2. Moves the "## Unreleased" changelog entries under "## <Version> - <date>".
    3. Writes the version to VikingOarsmen.csproj, Plugin.cs and package/manifest.json.
    4. Builds in Release.
    5. Creates dist/VikingOarsmen-<Version>.zip (Thunderstore layout) and the release notes.
    6. Prints the git/gh commands to publish. Nothing is committed or pushed by this script.

.EXAMPLE
    .\scripts\release.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    # Skip the clean working tree check (useful for a dry run).
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Update-File([string]$RelativePath, [string]$Pattern, [string]$Replacement) {
    $path = Join-Path $root $RelativePath
    $text = [IO.File]::ReadAllText($path)
    if ($text -notmatch $Pattern) {
        throw "Version pattern not found in $RelativePath"
    }
    [IO.File]::WriteAllText($path, ([regex]::Replace($text, $Pattern, $Replacement)), $utf8NoBom)
    Write-Host "  updated $RelativePath"
}

Push-Location $root
try {
    Write-Host "== Viking Oarsmen release $Version ==" -ForegroundColor Cyan

    # 1. Preconditions.
    if (-not $AllowDirty -and (git status --porcelain)) {
        throw "Working tree has uncommitted changes. Commit or stash them first (or pass -AllowDirty)."
    }
    if (git tag --list "v$Version") {
        throw "Tag v$Version already exists."
    }

    # 2. Changelog: the Unreleased section becomes this version.
    $changelogPath = Join-Path $root 'CHANGELOG.md'
    $changelog = [IO.File]::ReadAllText($changelogPath)
    if ($changelog -match "(?m)^## $([regex]::Escape($Version))\b") {
        throw "CHANGELOG.md already has a section for $Version."
    }
    $unreleased = [regex]::Match($changelog, '(?ms)^## Unreleased[ \t]*\r?\n(.*?)(?=^## |\z)')
    $notes = $unreleased.Groups[1].Value.Trim()
    if (-not $unreleased.Success -or -not $notes) {
        throw "CHANGELOG.md has no entries under '## Unreleased'."
    }
    $date = Get-Date -Format 'yyyy-MM-dd'
    $heading = [regex]'(?m)^## Unreleased[ \t]*(?=\r?\n)'
    [IO.File]::WriteAllText($changelogPath, $heading.Replace($changelog, "## Unreleased`n`n## $Version - $date", 1), $utf8NoBom)
    Write-Host "  stamped CHANGELOG.md"

    # 3. Version everywhere it is declared.
    Update-File 'VikingOarsmen\VikingOarsmen.csproj' '<Version>[^<]*</Version>' "<Version>$Version</Version>"
    Update-File 'VikingOarsmen\Plugin.cs' 'PluginVersion = "[^"]*"' "PluginVersion = `"$Version`""
    Update-File 'package\manifest.json' '"version_number":\s*"[^"]*"' "`"version_number`": `"$Version`""

    # 4. Build.
    Write-Host "== Building ==" -ForegroundColor Cyan
    dotnet build (Join-Path $root 'VikingOarsmen.sln') -c Release -nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
    $dll = Join-Path $root 'VikingOarsmen\bin\Release\VikingOarsmen.dll'

    # 5. Package (Thunderstore layout: manifest, icon, README and the plugin at the zip root).
    Write-Host "== Packaging ==" -ForegroundColor Cyan
    $dist = Join-Path $root 'dist'
    $stage = Join-Path $dist "VikingOarsmen-$Version"
    if (Test-Path $stage) {
        Remove-Item $stage -Recurse -Force
    }
    New-Item -ItemType Directory -Force $stage | Out-Null

    Copy-Item (Join-Path $root 'package\manifest.json') $stage
    Copy-Item (Join-Path $root 'package\icon.png') $stage
    Copy-Item (Join-Path $root 'README.md') $stage
    Copy-Item (Join-Path $root 'CHANGELOG.md') $stage
    Copy-Item (Join-Path $root 'LICENSE') $stage
    Copy-Item $dll $stage

    $zip = Join-Path $dist "VikingOarsmen-$Version.zip"
    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Copy-Item $dll (Join-Path $dist 'VikingOarsmen.dll') -Force

    $notesFile = Join-Path $dist "release-notes-$Version.md"
    [IO.File]::WriteAllText($notesFile, $notes + "`n", $utf8NoBom)

    Write-Host "  $zip"
    Write-Host "  $notesFile"

    # 6. Publishing is left to the maintainer.
    Write-Host ""
    Write-Host "== Next steps ==" -ForegroundColor Green
    Write-Host "  1. Test dist\VikingOarsmen.dll in game."
    Write-Host "  2. git add -A"
    Write-Host "  3. git commit -m `"Release v$Version`""
    Write-Host "  4. git tag -a v$Version -m `"Viking Oarsmen v$Version`""
    Write-Host "  5. git push origin main --follow-tags"
    Write-Host "  6. gh release create v$Version dist\VikingOarsmen-$Version.zip dist\VikingOarsmen.dll --title `"Viking Oarsmen v$Version`" --notes-file dist\release-notes-$Version.md"
    Write-Host "  7. (Optional) upload dist\VikingOarsmen-$Version.zip to https://thunderstore.io/c/valheim/create/"
}
finally {
    Pop-Location
}
