using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace ExitEcho.Core;

public sealed class ExitEchoMonitor
{
    private readonly record struct ProcessId(int Pid, long StartUtcTicks);
    private readonly record struct ProcessStart(int Pid, int ParentPid, long StartUtcTicks, string Name);
    private readonly record struct ProcessStop(int Pid, long EventUtcTicks);

    private sealed class TrackedProcess(ProcessId id, string name)
    {
        public ProcessId Id { get; } = id;
        public string Name { get; } = name;
        public long? StopUtcTicks { get; set; }
    }

    private readonly ConcurrentQueue<ProcessStart> _starts = new();
    private readonly ConcurrentQueue<ProcessStop> _stops = new();
    private readonly Dictionary<ProcessId, TrackedProcess> _tracked = new();
    private readonly List<ProcessStart> _pending = [];
    private readonly List<ProcessStop> _stopHistory = [];
    private HashSet<ProcessId> _observed = [];

    public async Task RunAsync(string executable, string[] arguments, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ExitEcho requires Windows.");

        var fullPath = Path.GetFullPath(executable);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Executable not found.", fullPath);

        using var startWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
        using var stopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
        startWatcher.EventArrived += OnStarted;
        stopWatcher.EventArrived += OnStopped;
        var usePolling = false;
        try
        {
            startWatcher.Start();
            stopWatcher.Start();
        }
        catch (ManagementException exception) when (exception.ErrorCode == ManagementStatus.AccessDenied)
        {
            startWatcher.Stop();
            stopWatcher.Stop();
            usePolling = true;
            PollProcessSnapshot(); // baseline before launching the target
        }

        try
        {
            var startInfo = new ProcessStartInfo(fullPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(fullPath)!
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using var root = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start executable.");

            var rootId = new ProcessId(root.Id, root.StartTime.ToUniversalTime().Ticks);
            _tracked.Add(rootId, new TrackedProcess(rootId, Path.GetFileNameWithoutExtension(fullPath)));

            var hadVisibleWindow = false;
            DateTimeOffset? closedAt = null;
            DateTimeOffset? emptySince = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (usePolling)
                    PollProcessSnapshot();
                DrainEvents();

                var live = _tracked.Values.Where(item => TryGetLiveProcess(item.Id, out _, out _)).ToArray();
                var visiblePids = VisibleWindowPids();
                var hasVisibleWindow = live.Any(item => visiblePids.Contains(item.Id.Pid));

                if (hasVisibleWindow)
                {
                    hadVisibleWindow = true;
                    closedAt = null;
                }
                else if (hadVisibleWindow)
                {
                    closedAt ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - closedAt >= TimeSpan.FromSeconds(8))
                    {
                        if (usePolling)
                            PollProcessSnapshot();
                        DrainEvents();
                        PrintSurvivors();
                        return;
                    }
                }
                else
                {
                    // A console or short-lived app may never show a visible window.
                    emptySince = live.Length == 0 ? emptySince ?? DateTimeOffset.UtcNow : null;
                    if (emptySince is not null && DateTimeOffset.UtcNow - emptySince >= TimeSpan.FromSeconds(2))
                        return;
                }

                await Task.Delay(200, cancellationToken);
            }
        }
        finally
        {
            startWatcher.Stop();
            stopWatcher.Stop();
        }
    }

    private void OnStarted(object sender, EventArrivedEventArgs args)
    {
        try
        {
            var data = args.NewEvent;
            var pid = Convert.ToInt32(data["ProcessID"]);
            var parentPid = Convert.ToInt32(data["ParentProcessID"]);
            var eventTicks = DateTime.FromFileTimeUtc(Convert.ToInt64(data["TIME_CREATED"])).Ticks;
            var observedStart = TryGetStartTicks(pid);
            // The PID may already belong to a newer process when WMI delivers this event.
            var startTicks = observedStart is not null && observedStart <= eventTicks + TimeSpan.TicksPerMillisecond
                ? observedStart.Value
                : eventTicks;
            _starts.Enqueue(new ProcessStart(pid, parentPid, startTicks, Convert.ToString(data["ProcessName"]) ?? "unknown"));
        }
        catch (Exception exception) when (exception is ManagementException or InvalidOperationException or ArgumentException or Win32Exception)
        {
            // A process may end before WMI delivers its start event.
        }
    }

    private void OnStopped(object sender, EventArrivedEventArgs args)
    {
        try
        {
            var data = args.NewEvent;
            _stops.Enqueue(new ProcessStop(
                Convert.ToInt32(data["ProcessID"]),
                DateTime.FromFileTimeUtc(Convert.ToInt64(data["TIME_CREATED"])).Ticks));
        }
        catch (Exception exception) when (exception is ManagementException or InvalidOperationException or ArgumentException)
        {
        }
    }

    private void DrainEvents()
    {
        var oldestRelevant = DateTime.UtcNow.AddSeconds(-30).Ticks;
        _pending.RemoveAll(start => start.StartUtcTicks < oldestRelevant);
        _stopHistory.RemoveAll(stop => stop.EventUtcTicks < oldestRelevant);

        while (_starts.TryDequeue(out var started))
        {
            if (started.StartUtcTicks >= oldestRelevant)
                _pending.Add(started);
        }

        while (_stops.TryDequeue(out var stopped))
        {
            if (stopped.EventUtcTicks >= oldestRelevant)
                _stopHistory.Add(stopped);
        }

        foreach (var process in _tracked.Values)
            process.StopUtcTicks ??= _stopHistory
                .Where(stop => stop.Pid == process.Id.Pid && stop.EventUtcTicks >= process.Id.StartUtcTicks)
                .Select(stop => (long?)stop.EventUtcTicks)
                .Min();

        // WMI events can arrive out of order, including before the root is registered.
        bool changed;
        do
        {
            changed = false;
            for (var index = _pending.Count - 1; index >= 0; index--)
            {
                var child = _pending[index];
                var parent = _tracked.Values.Any(item => item.Id.Pid == child.ParentPid
                    && item.Id.StartUtcTicks <= child.StartUtcTicks
                    && (item.StopUtcTicks is null || child.StartUtcTicks <= item.StopUtcTicks));
                if (!parent)
                    continue;

                var id = new ProcessId(child.Pid, child.StartUtcTicks);
                var tracked = new TrackedProcess(id, child.Name);
                tracked.StopUtcTicks = _stopHistory
                    .Where(stop => stop.Pid == id.Pid && stop.EventUtcTicks >= id.StartUtcTicks)
                    .Select(stop => (long?)stop.EventUtcTicks)
                    .Min();
                _tracked.TryAdd(id, tracked);
                _pending.RemoveAt(index);
                changed = true;
            }
        } while (changed);
    }

    private void PollProcessSnapshot()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, CreationDate, Name FROM Win32_Process");
        using var results = searcher.Get();
        var current = new HashSet<ProcessId>();
        foreach (ManagementObject process in results)
        {
            using (process)
            {
                if (process["CreationDate"] is not string creationDate)
                    continue;

                var id = new ProcessId(
                    Convert.ToInt32(process["ProcessId"]),
                    ManagementDateTimeConverter.ToDateTime(creationDate).ToUniversalTime().Ticks);
                current.Add(id);
                if (!_observed.Contains(id))
                    _starts.Enqueue(new ProcessStart(id.Pid, Convert.ToInt32(process["ParentProcessId"]),
                        id.StartUtcTicks, Convert.ToString(process["Name"]) ?? "unknown"));
            }
        }

        var stoppedAt = DateTime.UtcNow.Ticks;
        foreach (var old in _observed.Except(current))
            _stops.Enqueue(new ProcessStop(old.Pid, stoppedAt));
        _observed = current;
    }

    private void PrintSurvivors()
    {
        var survivors = new List<(string Name, int Pid, long Ram)>();
        foreach (var item in _tracked.Values)
        {
            if (TryGetLiveProcess(item.Id, out var name, out var ram))
                survivors.Add((name ?? item.Name, item.Id.Pid, ram));
        }

        if (survivors.Count == 0)
            return;

        Console.WriteLine("Related processes still running after the window closed:");
        foreach (var item in survivors.OrderBy(item => item.Pid))
            Console.WriteLine($"{item.Name}  PID {item.Pid}  RAM {item.Ram / 1024.0 / 1024.0:F1} MiB");
    }

    private static long? TryGetStartTicks(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static bool TryGetLiveProcess(ProcessId id, out string? name, out long ram)
    {
        name = null;
        ram = 0;
        try
        {
            using var process = Process.GetProcessById(id.Pid);
            // WMI CreationDate has microsecond precision; Process.StartTime may retain finer ticks.
            if (process.HasExited || Math.Abs(process.StartTime.ToUniversalTime().Ticks - id.StartUtcTicks) > TimeSpan.TicksPerMillisecond)
                return false;

            name = process.ProcessName;
            ram = process.WorkingSet64;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static HashSet<int> VisibleWindowPids()
    {
        var result = new HashSet<int>();
        if (!EnumWindows((window, _) =>
            {
                if (IsWindowVisible(window))
                {
                    GetWindowThreadProcessId(window, out var pid);
                    result.Add(unchecked((int)pid));
                }
                return true;
            }, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
