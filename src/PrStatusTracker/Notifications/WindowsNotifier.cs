using System.Security;
using System.Text;
using PrStatusTracker.Platform;

namespace PrStatusTracker.Notifications;

/// <summary>
/// Windows toast via the WinRT notification API, driven through Windows PowerShell so the app needs no
/// Windows-specific TFM and no installer. The toast uses protocol activation, so clicking it opens the PR
/// (or the GitHub PR list for a batched toast) in the browser.
/// </summary>
public sealed class WindowsNotifier : INotifier
{
    // The AUMID a toast is shown under is what Action Center attributes it to, and Windows takes the name it
    // displays from the Start Menu shortcut carrying that ID. There is no such shortcut until the app is
    // installed, so Windows falls back to rendering the ID itself - which is why this is the bare app name and
    // not a dotted identifier. Borrowing PowerShell's own AUMID, as this used to, read as "Windows PowerShell".
    // Packaging (section 8) adds the shortcut, and with it a spaced display name and the app's icon.
    private const string AppId = "PrStatusTracker";

    public Task ShowAsync(Toast toast, CancellationToken ct = default)
    {
        var toastXml = $"""
            <toast activationType="protocol" launch="{SecurityElement.Escape(toast.Url)}">
              <visual><binding template="ToastGeneric">
                <text>{SecurityElement.Escape(toast.Title)}</text>
                <text>{SecurityElement.Escape(toast.Body)}</text>
              </binding></visual>
            </toast>
            """;

        var script = $$"""
            [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
            [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
            $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
            $xml.LoadXml(@'
            {{toastXml}}
            '@)
            $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
            [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{{AppId}}').Show($toast)
            """;

        return Processes.RunHiddenAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
             "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], ct);
    }
}
