#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Rebuilds the MatNotes container and redeploys the dev stack (the project's live-reload / test workflow).
.DESCRIPTION
    Always rebuilds the image from the current source and recreates the containers. The data (database, configuration) stays in the volumes;
    -Clean removes them first and starts with an empty database and the setup assistant.
    The version of a local build is "local-<builddate>".
.PARAMETER Port
    Host port of the web interface (default 2301).
.PARAMETER Clean
    Remove the volumes of the dev stack first.
.EXAMPLE
    ./scripts/redeploy.ps1
.EXAMPLE
    ./scripts/redeploy.ps1 -Clean
#>
param(
    [int]$Port = 2301,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $env:MATNOTE_CHANNEL = 'local'
    $env:MATNOTE_BUILD_DATE = (Get-Date).ToUniversalTime().ToString('yyyyMMdd')
    $env:MATNOTE_PORT = "$Port"

    $compose = @('compose', '-f', 'docker-compose.dev.yml')

    # Windows PowerShell 5.1 turns the progress lines docker writes to stderr into errors; judge by the exit code instead.
    $ErrorActionPreference = 'Continue'

    if ($Clean) {
        docker @compose down -v --remove-orphans
    }

    docker @compose up -d --build --remove-orphans
    if ($LASTEXITCODE -ne 0) { throw 'docker compose failed.' }

    Write-Host ''
    Write-Host "MatNotes local-$($env:MATNOTE_BUILD_DATE) is running on http://localhost:$Port (pgAdmin on http://localhost:2302)" -ForegroundColor Green
}
finally {
    Pop-Location
}
