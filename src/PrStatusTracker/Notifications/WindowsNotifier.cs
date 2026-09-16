using System.Security;
using System.Text;
using PrStatusTracker.Platform;

namespace PrStatusTracker.Notifications;

/// <summary>
/// Windows toast via the WinRT notification API, driven through Windows PowerShell so the app needs no
/// Windows-specific TFM, no installer and no registered AUMID during development. The toast uses protocol
/// activation, so clicking it opens the PR (or the GitHub PR list for a batched toast) in the browser.
/// </summary>
public sealed class WindowsNotifier : INotifier
{
    // PowerShell's own AUMID; any toast shown under it is attributed to "Windows PowerShell" in Action Center.
    private const string AppId = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";

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
