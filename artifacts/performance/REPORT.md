# ExitEcho 0.3.0 / .NET 10 performance check

Machine: Windows 11 Pro 10.0.26200 x64, Intel Core i5-14600KF (20 logical processors), 31.79 GiB RAM. Debug x64 GUI, same user account, WMI event watchers available. CPU is reported as a percentage of **one logical core**; divide by 20 for the approximate whole-machine percentage. Samples were taken once per second with `tools/performance/Measure-ExitEcho.ps1`. All measurements are local and include normal desktop activity; they are not lab-grade benchmarks.

| Scenario | Duration | CPU mean / peak, one core | Working set start → end (max), MiB | Private start → end (max), MiB | Handles start → end (max) | Threads start → end (max) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| .NET 8 baseline, idle | 300 s | 5.81 / 12.00% | 110.15 → 112.15 (114.08) | 42.19 → 43.25 (46.14) | 439 → 432 (439) | 19 → 17 (19) |
| .NET 10, idle | 300 s | 5.74 / 12.00% | 109.64 → 110.23 (113.05) | 42.99 → 42.87 (46.24) | 464 → 457 (464) | 19 → 17 (19) |
| .NET 10, five GUI SmokeHosts opened and closed | 61 s | 11.46 / 40.00% | 154.20 → 158.38 (159.89) | 70.26 → 71.93 (74.58) | 605 → 605 (608) | 29 → 27 (29) |
| .NET 10, 50 Pause/Resume cycles | 90 s | 12.76 / 62.00% | 153.09 → 159.39 (170.07) | 66.78 → 73.91 (82.37) | 605 → 610 (622) | 27 → 30 (31) |
| .NET 10, post-activity soak | 601 s | 12.70 / 37.00% | 162.35 → 166.58 (168.08) | 77.00 → 82.11 (83.77) | 607 → 596 (613) | 29 → 25 (30) |

The comparable idle measurements show no meaningful CPU change from the framework migration and no five-minute memory or handle leak. The 10-minute soak's private-memory medians across consecutive two-minute blocks were 75.06, 74.42, 75.61, 76.06 and 76.10 MiB; end-point variation did not show a steady leak, while handle count fell. A longer overnight run would be needed to rule out slow growth. The 200 ms window enumeration remains unchanged; increasing it without a detection-accuracy experiment is not justified.

The WMI fallback performs a full `Win32_Process` query on every 200 ms loop when event subscriptions are denied. A 100-query proxy with the same query and enumeration over ~302 processes averaged **113.71 ms/query** (p95 134.76 ms) and used **43.48% of one core** in the calling PowerShell process; WMI provider CPU was not included. This is an expensive emergency path. Access-denied fallback could not be forced safely on this machine, so this is not a measurement of ExitEcho's full fallback behavior. Changing the polling interval or process API would require a separate detection-accuracy test and is outside this migration.

Reproduce the sampling portion with a running ExitEcho PID:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/performance/Measure-ExitEcho.ps1 -ProcessId <PID> -DurationSeconds 300 -OutputPrefix artifacts/performance/my-run -Scenario idle
```

For the active scenario, launch and close five `ExitEchoSmokeHost` windows while sampling. For Pause/Resume, invoke the actual MainWindow button repeatedly (the local check used Windows UI Automation). Raw samples and JSON summaries are beside this report.

Reproduce the WMI query proxy with `powershell -NoProfile -ExecutionPolicy Bypass -File tools/performance/Measure-WmiFallback.ps1`.
