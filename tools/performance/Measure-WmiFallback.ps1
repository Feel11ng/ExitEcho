param(
    [ValidateRange(10, 1000)][int]$Queries = 100,
    [ValidateRange(100, 1000)][int]$IntervalMilliseconds = 200,
    [string]$OutputPath = 'artifacts/performance/wmi-query-proxy.json'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Management
$latency = [System.Collections.Generic.List[double]]::new()
$processCounts = [System.Collections.Generic.List[int]]::new()
$caller = Get-Process -Id $PID
$cpuStart = $caller.CPU
$total = [System.Diagnostics.Stopwatch]::StartNew()

for ($index = 0; $index -lt $Queries; $index++) {
    $query = [System.Diagnostics.Stopwatch]::StartNew()
    $searcher = [System.Management.ManagementObjectSearcher]::new(
        'SELECT ProcessId, ParentProcessId, CreationDate, Name FROM Win32_Process')
    try {
        $results = $searcher.Get()
        try {
            $count = 0
            foreach ($item in $results) {
                try {
                    if ($item['CreationDate'] -is [string]) { $count++ }
                }
                finally { $item.Dispose() }
            }
            $processCounts.Add($count)
        }
        finally { $results.Dispose() }
    }
    finally { $searcher.Dispose() }
    $query.Stop()
    $latency.Add($query.Elapsed.TotalMilliseconds)
    $remaining = $IntervalMilliseconds - $query.Elapsed.TotalMilliseconds
    if ($remaining -gt 0) { Start-Sleep -Milliseconds ([int]$remaining) }
}

$total.Stop()
$caller.Refresh()
$ordered = @($latency | Sort-Object)
$summary = [ordered]@{
    Queries = $Queries
    IntendedIntervalMilliseconds = $IntervalMilliseconds
    DurationSeconds = [Math]::Round($total.Elapsed.TotalSeconds, 2)
    ProcessCountMean = [Math]::Round(($processCounts | Measure-Object -Average).Average, 1)
    QueryMillisecondsMean = [Math]::Round(($latency | Measure-Object -Average).Average, 2)
    QueryMillisecondsP95 = [Math]::Round($ordered[[Math]::Min($ordered.Count - 1, [int][Math]::Floor($ordered.Count * .95))], 2)
    CallerCpuOneCorePercent = [Math]::Round(100 * ($caller.CPU - $cpuStart) / $total.Elapsed.TotalSeconds, 2)
    Note = 'Proxy for fallback query and enumeration only; WMI provider CPU and full monitor path are not included.'
}
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$summary | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 $OutputPath
$summary | ConvertTo-Json -Depth 3
