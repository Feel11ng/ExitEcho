using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace ExitEcho.Core;

public sealed record LeftoverProcess(string Name, int Pid, long StartUtcTicks, long WorkingSetBytes);
public sealed record LeftoverEvent(string AppName, IReadOnlyList<LeftoverProcess> Processes);

public sealed class PassiveMonitor
{
    public event Action<LeftoverEvent>? LeftoversFound;
    public bool WriteToConsole { get; set; } = true;

    private readonly record struct ProcessId(int Pid, long StartUtcTicks);
    private readonly record struct StartEvent(ProcessId Id, int ParentPid, string Name);
    private readonly record struct StopEvent(int Pid, long UtcTicks);

    private sealed class ProcessRecord(StartEvent start)
    {
        public StartEvent Start { get; set; } = start;
        public long? StoppedUtcTicks { get; set; }
    }

    private sealed class AppSession(ProcessId root, string name)
    {
        public ProcessId Root { get; } = root;
        public string Name { get; } = name;
        public HashSet<ProcessId> Related { get; } = [root];
        public DateTimeOffset? LastWindowGone { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "SearchApp", "RuntimeBroker", "TextInputHost", "LockApp",
        "dwm", "csrss", "winlogon", "services", "svchost", "conhost", "smss",
        "System", "Registry", "fontdrvhost", "WmiPrvSE", "sihost", "taskhostw",
        "SystemSettings", "SecurityHealthSystray"
    };

    // The desktop test controller is tooling, not an application being watched.
    private static readonly HashSet<string> TestInfrastructureNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "codex-computer-use"
    };

    private readonly ConcurrentQueue<StartEvent> _starts = new();
    private readonly ConcurrentQueue<StopEvent> _stops = new();
    private readonly Dictionary<ProcessId, ProcessRecord> _known = new();
    private readonly List<StopEvent> _stopHistory = [];
    private readonly List<AppSession> _sessions = [];
    private readonly HashSet<ProcessId> _seenRoots = [];
    private readonly HashSet<ProcessId> _internalProcesses = [];
    private HashSet<ProcessId> _observed = [];

    private static ProcessId Identity(int pid, long startUtcTicks) =>
        new(pid, startUtcTicks - startUtcTicks % TimeSpan.TicksPerMillisecond);

    public async Task WatchAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ExitEcho requires Windows.");

        using var self = Process.GetCurrentProcess();
        var selfPid = self.Id;
        var selfId = Identity(self.Id, self.StartTime.ToUniversalTime().Ticks);
        _internalProcesses.Add(selfId);
        _known.TryAdd(selfId, new ProcessRecord(new StartEvent(selfId, 0, self.ProcessName)));
        var sessionId = self.SessionId;
        var baseline = VisibleWindows(sessionId).Keys.ToHashSet();

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
            PollProcessSnapshot();
            DrainEvents();
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (usePolling)
                    PollProcessSnapshot();
                DrainEvents();

                var windows = VisibleWindows(sessionId);
                foreach (var (id, _) in windows)
                {
                    if (id.Pid == selfPid || baseline.Contains(id) || _seenRoots.Contains(id))
                        continue;
                    if (_internalProcesses.Contains(id) || IsDescendantOf(id, _internalProcesses))
                    {
                        _internalProcesses.Add(id);
                        continue;
                    }
                    if (_sessions.Any(app => app.Related.Contains(id)))
                        continue;
                    var parentSession = _sessions.FirstOrDefault(app => IsDescendantOf(id, app.Related));
                    if (parentSession is not null)
                    {
                        parentSession.Related.Add(id);
                        continue;
                    }
                    if (!TryGetLiveProcess(id, out var name, out _) || name is null)
                        continue;
                    if (TestInfrastructureNames.Contains(name))
                    {
                        _internalProcesses.Add(id);
                        continue;
                    }
                    if (SystemNames.Contains(name))
                        continue;

                    var app = new AppSession(id, name);
                    _sessions.Add(app);
                    _seenRoots.Add(id);
                    _known.TryAdd(id, new ProcessRecord(new StartEvent(id, 0, name)));
                    ResolveDescendants(); // include children started before the first window appeared
                    MergeRelatedSessions();
                }

                foreach (var app in _sessions.ToArray())
                {
                    if (app.CompletedAt is not null)
                    {
                        if (DateTimeOffset.UtcNow - app.CompletedAt >= TimeSpan.FromSeconds(30)
                            && app.Related.All(id => !TryGetLiveProcess(id, out _, out _)))
                            _sessions.Remove(app);
                        continue;
                    }

                    if (app.Related.Any(windows.ContainsKey))
                    {
                        app.LastWindowGone = null;
                        continue;
                    }

                    app.LastWindowGone ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - app.LastWindowGone < TimeSpan.FromSeconds(8))
                        continue;

                    if (usePolling)
                        PollProcessSnapshot();
                    DrainEvents();
                    if (!_sessions.Contains(app))
                        continue;
                    var currentWindows = VisibleWindows(sessionId);
                    if (app.Related.Any(currentWindows.ContainsKey))
                    {
                        app.LastWindowGone = null;
                        continue;
                    }
                    PrintSurvivors(app);
                    app.CompletedAt = DateTimeOffset.UtcNow;
                }

                PruneHistory();
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
            var eventTicks = DateTime.FromFileTimeUtc(Convert.ToInt64(data["TIME_CREATED"])).Ticks;
            var actualTicks = TryGetStartTicks(pid);
            var startTicks = actualTicks is not null && actualTicks <= eventTicks + TimeSpan.TicksPerMillisecond
                ? actualTicks.Value : eventTicks;
            _starts.Enqueue(new StartEvent(Identity(pid, startTicks),
                Convert.ToInt32(data["ParentProcessID"]), Convert.ToString(data["ProcessName"]) ?? "unknown"));
        }
        catch (Exception exception) when (exception is ManagementException or InvalidOperationException or ArgumentException or Win32Exception)
        {
        }
    }

    private void OnStopped(object sender, EventArrivedEventArgs args)
    {
        try
        {
            var data = args.NewEvent;
            _stops.Enqueue(new StopEvent(Convert.ToInt32(data["ProcessID"]),
                DateTime.FromFileTimeUtc(Convert.ToInt64(data["TIME_CREATED"])).Ticks));
        }
        catch (Exception exception) when (exception is ManagementException or InvalidOperationException or ArgumentException)
        {
        }
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
                var id = Identity(Convert.ToInt32(process["ProcessId"]),
                    ManagementDateTimeConverter.ToDateTime(creationDate).ToUniversalTime().Ticks);
                current.Add(id);
                if (!_observed.Contains(id))
                    _starts.Enqueue(new StartEvent(id, Convert.ToInt32(process["ParentProcessId"]),
                        Convert.ToString(process["Name"]) ?? "unknown"));
            }
        }

        foreach (var old in _observed.Except(current))
            _stops.Enqueue(new StopEvent(old.Pid, DateTime.UtcNow.Ticks));
        _observed = current;
    }

    private void DrainEvents()
    {
        while (_starts.TryDequeue(out var started))
        {
            if (TestInfrastructureNames.Contains(started.Name))
                _internalProcesses.Add(started.Id);
            if (_known.TryGetValue(started.Id, out var existing))
            {
                if (existing.Start.ParentPid == 0 && started.ParentPid != 0)
                    existing.Start = started;
            }
            else
                _known.Add(started.Id, new ProcessRecord(started));
        }
        while (_stops.TryDequeue(out var stopped))
            _stopHistory.Add(stopped);

        foreach (var record in _known.Values)
            record.StoppedUtcTicks ??= _stopHistory
                .Where(stop => stop.Pid == record.Start.Id.Pid && stop.UtcTicks >= record.Start.Id.StartUtcTicks)
                .Select(stop => (long?)stop.UtcTicks).Min();

        ResolveInternalDescendants();
        _sessions.RemoveAll(app => _internalProcesses.Contains(app.Root));
        ResolveDescendants();
        MergeRelatedSessions();
    }

    private void ResolveInternalDescendants()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var child in _known.Values)
            {
                if (_internalProcesses.Contains(child.Start.Id))
                    continue;
                if (HasParentIn(child.Start, _internalProcesses))
                    changed |= _internalProcesses.Add(child.Start.Id);
            }
        } while (changed);
    }

    private bool IsDescendantOf(ProcessId id, HashSet<ProcessId> ancestors)
    {
        var visited = new HashSet<ProcessId>();
        for (var depth = 0; depth < 32 && visited.Add(id); depth++)
        {
            if (ancestors.Contains(id))
                return true;
            if (!_known.TryGetValue(id, out var record) || record.Start.ParentPid == 0)
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT ParentProcessId, CreationDate, Name FROM Win32_Process WHERE ProcessId = {id.Pid}");
                using var results = searcher.Get();
                var process = results.Cast<ManagementObject>().FirstOrDefault();
                if (process is null)
                    return false;
                using (process)
                {
                    if (process["CreationDate"] is not string creationDate ||
                        Identity(id.Pid, ManagementDateTimeConverter.ToDateTime(creationDate).ToUniversalTime().Ticks) != id)
                        return false;
                    var started = new StartEvent(id, Convert.ToInt32(process["ParentProcessId"]),
                        Convert.ToString(process["Name"]) ?? "unknown");
                    if (record is null)
                        _known.Add(id, new ProcessRecord(started));
                    else
                        record.Start = started;
                    record = _known[id];
                }
            }
            if (HasParentIn(record.Start, ancestors))
                return true;
            var parent = _known.Values
                .Where(item => item.Start.Id.Pid == record.Start.ParentPid
                    && item.Start.Id.StartUtcTicks <= id.StartUtcTicks
                    && (item.StoppedUtcTicks is null || item.StoppedUtcTicks >= id.StartUtcTicks))
                .OrderByDescending(item => item.Start.Id.StartUtcTicks)
                .FirstOrDefault();
            if (parent is null)
            {
                var parentTicks = TryGetStartTicks(record.Start.ParentPid);
                if (parentTicks is null)
                    return false;
                id = Identity(record.Start.ParentPid, parentTicks.Value);
            }
            else
                id = parent.Start.Id;
        }
        return false;
    }

    private bool HasParentIn(StartEvent child, HashSet<ProcessId> ancestors) =>
        ancestors.Any(id => id.Pid == child.ParentPid
            && id.StartUtcTicks <= child.Id.StartUtcTicks
            && (!_known.TryGetValue(id, out var parent)
                || parent.StoppedUtcTicks is null
                || parent.StoppedUtcTicks >= child.Id.StartUtcTicks));

    private void MergeRelatedSessions()
    {
        foreach (var child in _sessions.ToArray())
        {
            var parent = _sessions.FirstOrDefault(app => app != child && app.Related.Contains(child.Root));
            if (parent is null)
                continue;
            parent.Related.UnionWith(child.Related);
            _sessions.Remove(child);
        }
    }

    private void ResolveDescendants()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var child in _known.Values)
            {
                foreach (var app in _sessions)
                {
                    if (app.Related.Contains(child.Start.Id))
                        continue;
                    var parent = app.Related.Any(id => id.Pid == child.Start.ParentPid
                        && id.StartUtcTicks <= child.Start.Id.StartUtcTicks
                        && (!_known.TryGetValue(id, out var parentRecord)
                            || parentRecord.StoppedUtcTicks is null
                            || child.Start.Id.StartUtcTicks <= parentRecord.StoppedUtcTicks));
                    if (parent)
                        changed |= app.Related.Add(child.Start.Id);
                }
            }
        } while (changed);
    }

    private void PruneHistory()
    {
        var oldest = DateTime.UtcNow.AddSeconds(-60).Ticks;
        var related = _sessions.SelectMany(app => app.Related).ToHashSet();
        foreach (var id in _known.Keys.Where(id => id.StartUtcTicks < oldest && !related.Contains(id)).ToArray())
            _known.Remove(id);
        _stopHistory.RemoveAll(stop => stop.UtcTicks < oldest);
        _seenRoots.RemoveWhere(id => id.StartUtcTicks < oldest && !TryGetLiveProcess(id, out _, out _));
        _internalProcesses.RemoveWhere(id => id.StartUtcTicks < oldest && !TryGetLiveProcess(id, out _, out _));
    }

    private void PrintSurvivors(AppSession app)
    {
        var survivors = new List<LeftoverProcess>();
        foreach (var id in app.Related)
        {
            if (TryGetLiveProcess(id, out var name, out var ram))
                survivors.Add(new LeftoverProcess(name ?? "unknown", id.Pid, id.StartUtcTicks, ram));
        }
        if (survivors.Count == 0)
            return;

        LeftoversFound?.Invoke(new LeftoverEvent(app.Name, survivors));
        if (!WriteToConsole)
            return;

        Console.WriteLine($"{app.Name} left {survivors.Count} processes running");
        foreach (var item in survivors.OrderBy(item => item.Pid))
            Console.WriteLine($"{item.Name} | {item.Pid} | {item.WorkingSetBytes / 1024.0 / 1024.0:F1} MiB");
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

    private static Dictionary<ProcessId, int> VisibleWindows(int sessionId)
    {
        var pids = new Dictionary<ProcessId, int>();
        var identities = new Dictionary<int, ProcessId?>();
        if (!EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window) ||
                    (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0))
                    return true;
                GetWindowThreadProcessId(window, out var rawPid);
                var pid = unchecked((int)rawPid);
                if (!identities.TryGetValue(pid, out var id))
                {
                    id = null;
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        if (!process.HasExited && process.SessionId == sessionId)
                            id = Identity(pid, process.StartTime.ToUniversalTime().Ticks);
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
                    {
                    }
                    identities.Add(pid, id);
                }
                if (id is not null)
                    pids[id.Value] = pids.GetValueOrDefault(id.Value) + 1;
                return true;
            }, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return pids;
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int valueSize);
}
