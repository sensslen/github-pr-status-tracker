using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace PrStatusTracker.Platform;

public interface IAutoStart
{
    bool IsSupported { get; }
    bool IsEnabled();
    Task EnableAsync();
    Task DisableAsync();
}

public static class AutoStart
{
    /// <summary>Argument that makes the app start hidden in the tray.</summary>
    public const string TrayArgument = "--tray";

    public static IAutoStart ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new WindowsAutoStart()
        : OperatingSystem.IsMacOS() ? new MacAutoStart()
        : new UnsupportedAutoStart();

    public static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path");
}

public sealed class UnsupportedAutoStart : IAutoStart
{
    public bool IsSupported => false;
    public bool IsEnabled() => false;
    public Task EnableAsync() => Task.CompletedTask;
    public Task DisableAsync() => Task.CompletedTask;
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStart : IAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PrStatusTracker";

    public bool IsSupported => true;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public Task EnableAsync()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, $"\"{AutoStart.ExecutablePath}\" {AutoStart.TrayArgument}");
        return Task.CompletedTask;
    }

    public Task DisableAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        return Task.CompletedTask;
    }
}

[SupportedOSPlatform("macos")]
public sealed class MacAutoStart : IAutoStart
{
    private const string Label = "com.prstatustracker";
    private static readonly string s_plistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    public bool IsSupported => true;

    public bool IsEnabled() => File.Exists(s_plistPath);

    public async Task EnableAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(s_plistPath)!);
        File.WriteAllText(s_plistPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>Label</key><string>{Label}</string>
              <key>ProgramArguments</key><array>
                <string>{SecurityElement.Escape(AutoStart.ExecutablePath)}</string>
                <string>{AutoStart.TrayArgument}</string>
              </array>
              <key>RunAtLoad</key><true/>
            </dict></plist>
            """);
        await Processes.RunHiddenAsync("/bin/launchctl", ["load", "-w", s_plistPath]);
    }

    public async Task DisableAsync()
    {
        if (!File.Exists(s_plistPath))
        {
            return;
        }

        await Processes.RunHiddenAsync("/bin/launchctl", ["unload", "-w", s_plistPath]);
        File.Delete(s_plistPath);
    }
}
