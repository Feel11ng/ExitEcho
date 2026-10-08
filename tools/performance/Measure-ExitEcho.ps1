param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [ValidateRange(5, 3600)][int]$DurationSeconds = 300,
    [Parameter(Mandatory = $true)][string]$OutputPrefix,
    [string]$Scenario = 'idle'
)

$ErrorActionPreference = 'Stop'
$outputDirectory = Split-Path -Parent $OutputPrefix
if ($outputDirectory) { New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null }

$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$logicalProcessors = [Environment]::ProcessorCount
$samples = [System.Collections.Generic.List[object]]::new()
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$previousCpu = $null
$previousElapsed = $null

while ($timer.Elapsed.TotalSeconds -lt $DurationSeconds) {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    $elapsed = $timer.Elapsed.TotalSeconds
    $cpuSeconds = if ($process.CPU) { [double]$process.CPU } else { 0.0 }
    $cpuOneCorePercent = 0.0
    if ($null -ne $previousCpu -and $elapsed -gt $previousElapsed) {
        $cpuOneCorePercent = [Math]::Max(0, 100 * ($cpuSeconds - $previousCpu) / ($elapsed - $previousElapsed))
    }
    $samples.Add([pscustomobject]@{
        Seconds = [Math]::Round($elapsed, 2)
        CpuOneCorePercent = [Math]::Round($cpuOneCorePercent, 2)
        CpuMachinePercent = [Math]::Round($cpuOneCorePercent / $logicalProcessors, 3)
        WorkingSetMiB = [Math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateMiB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        Handles = $process.HandleCount
        Threads = $process.Threads.Count
    })
    $previousCpu = $cpuSeconds
    $previousElapsed = $elapsed
    Start-Sleep -Milliseconds 1000
}

$samples | Export-Csv -NoTypeInformation -Encoding UTF8 -Path "$OutputPrefix.csv"
$validCpu = @($samples | Select-Object -Skip 1 | ForEach-Object CpuOneCorePercent)
$workingSet = @($samples | ForEach-Object WorkingSetMiB)
$private = @($samples | ForEach-Object PrivateMiB)
$handles = @($samples | ForEach-Object Handles)
$threads = @($samples | ForEach-Object Threads)
$summary = [ordered]@{
    Scenario = $Scenario
    ProcessId = $ProcessId
    DurationSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 1)
    Samples = $samples.Count
    Machine = [ordered]@{
        OS = $os.Caption
        OSVersion = $os.Version
        Architecture = $os.OSArchitecture
        CPU = $cpu.Name
        LogicalProcessors = $logicalProcessors
        InstalledRamGiB = [Math]::Round([double]$os.TotalVisibleMemorySize / 1MB, 2)
    }
    CpuOneCorePercentMean = [Math]::Round(($validCpu | Measure-Object -Average).Average, 2)
    CpuOneCorePercentMax = [Math]::Round(($validCpu | Measure-Object -Maximum).Maximum, 2)
    WorkingSetMiBStart = $workingSet[0]
    WorkingSetMiBEnd = $workingSet[-1]
    WorkingSetMiBMax = ($workingSet | Measure-Object -Maximum).Maximum
    PrivateMiBStart = $private[0]
    PrivateMiBEnd = $private[-1]
    PrivateMiBMax = ($private | Measure-Object -Maximum).Maximum
    HandlesStart = $handles[0]
    HandlesEnd = $handles[-1]
    HandlesMax = ($handles | Measure-Object -Maximum).Maximum
    ThreadsStart = $threads[0]
    ThreadsEnd = $threads[-1]
    ThreadsMax = ($threads | Measure-Object -Maximum).Maximum
}
$summary | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 "$OutputPrefix.json"
$summary | ConvertTo-Json -Depth 4
