using System.Configuration;
using System.Data;
using System.Windows;
using MotionInput.Core.Input;

namespace MotionInput.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Driver rebinding needs Administrator privileges, but the app shouldn't require elevation
    /// just to launch (most users never touch this feature). Instead, MainWindow re-launches this
    /// same exe with "--rebind-driver &lt;instanceId&gt; &lt;hid|xinput&gt;" via ShellExecute with
    /// verb "runas" to get a one-time UAC prompt for just this action. When started that way, run
    /// headlessly (no window) and exit with a status code instead of showing the normal UI.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length >= 2 && e.Args[0] == "--rebind-driver")
        {
            var instanceId = e.Args[1];
            var mode = e.Args.Length >= 3 ? e.Args[2] : "hid";

            var success = mode == "xinput"
                ? ControllerDriverInspector.TryRebindToXInputDriver(instanceId, out _)
                : ControllerDriverInspector.TryRebindToGenericHid(instanceId, out _);

            Environment.Exit(success ? 0 : 1);
            return;
        }

        base.OnStartup(e);
    }
}

