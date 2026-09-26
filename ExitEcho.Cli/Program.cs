using ExitEcho.Core;

var run = args.Length >= 2 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase);
var watch = args.Length == 1 && string.Equals(args[0], "watch", StringComparison.OrdinalIgnoreCase);
if (!run && !watch)
{
    Console.Error.WriteLine("Usage: exitecho run \"<path-to-exe>\" [args] | exitecho watch");
    return 2;
}

try
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    if (run)
        await new ExitEchoMonitor().RunAsync(args[1], args[2..], cancellation.Token);
    else
        await new PassiveMonitor().WatchAsync(cancellation.Token);
    return 0;
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ExitEcho: {exception.Message}");
    return 1;
}
