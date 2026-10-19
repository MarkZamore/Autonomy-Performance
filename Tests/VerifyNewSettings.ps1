param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
# Current regression suite includes the relocated display controls and removed timers.
& (Join-Path $PSScriptRoot 'VerifyLatestSettings.ps1') -Builder $Builder
