using System.Diagnostics;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var childInfo = new ProcessStartInfo(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        childInfo.ArgumentList.Add("-NoProfile");
        childInfo.ArgumentList.Add("-Command");
        childInfo.ArgumentList.Add("Start-Sleep -Seconds 180");
        using var child = Process.Start(childInfo) ?? throw new InvalidOperationException("Child did not start.");
        if (args.Length > 0)
            File.WriteAllText(args[0], child.Id.ToString());

        Application.EnableVisualStyles();
        using var form = new Form
        {
            Text = "ExitEcho smoke host",
            Width = 420,
            Height = 180,
            StartPosition = FormStartPosition.CenterScreen
        };
        using var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) => { timer.Stop(); form.Close(); };
        form.Shown += (_, _) =>
        {
            if (args.Length > 0)
                File.WriteAllText(args[0] + ".shown", DateTimeOffset.Now.ToString("O"));
            timer.Start();
        };
        Application.Run(form);
    }
}
