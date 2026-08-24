using System.Configuration;
using System.Data;
using System.Windows;
using MotionInput.Core.HidHide;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace MotionInput.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 236KO can't function without both drivers: ViGEmBus creates the virtual controller every
    /// output goes to, and HidHide is what hides the real controller from the game. Rather than
    /// let either failure surface as a confusing crash the first time the engine starts, check for
    /// both up front and refuse to open the main window at all if either is missing.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        var missing = new List<string>();

        if (!IsViGEmBusInstalled())
        {
            missing.Add("ViGEmBus (creates the virtual Xbox 360 controller)\nhttps://github.com/ViGEm/ViGEmBus/releases");
        }

        if (!new HidHideService().IsInstalled)
        {
            missing.Add("HidHide (hides your real controller from the game)\nhttps://github.com/nefarius/HidHide/releases");
        }

        if (missing.Count > 0)
        {
            var message = "236KO requires the following driver(s), which aren't installed:\n\n" +
                          string.Join("\n\n", missing) +
                          "\n\nInstall them (rebooting if prompted), then relaunch 236KO.";
            MessageBox.Show(message, "Missing required driver(s)", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }

    private static bool IsViGEmBusInstalled()
    {
        try
        {
            using var client = new ViGEmClient();
            return true;
        }
        catch (VigemBusNotFoundException)
        {
            return false;
        }
        catch
        {
            // Any other failure (e.g. a transient access issue with the bus already present)
            // shouldn't produce a false "not installed" diagnosis — only a confirmed missing
            // driver should block startup here.
            return true;
        }
    }
}
