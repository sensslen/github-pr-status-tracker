using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using PrStatusTracker.GitHub;

namespace PrStatusTracker.Core;

/// <summary>
/// Looks for a newer GitHub release and installs it. The repository is private, so fetching a release asset
/// needs credentials; going through `gh` means the user's own, the same ones every other call uses, and no
/// token has to be shipped inside the application.
/// </summary>
public sealed class UpdateService(GhCommandRunner gh, Action shutdown)
{
    /// <summary>Releases come from here since the renewedvision repository was archived after v0.0.3.</summary>
    public const string Repo = "sensslen/github-pr-status-tracker";

    private static readonly TimeSpan s_checkInterval = TimeSpan.FromHours(24);
    private static readonly Version s_sourceBuild = new(0, 0, 0);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;

    /// <summary>What a release tag has to beat. Only a build made from a tag carries one; see the csproj.</summary>
    public static Version Current { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static string CurrentDisplay => Current.ToString(3);

    /// <summary>The tag of the newer release, once a check has found one.</summary>
    public string? AvailableTag { get; private set; }

    /// <summary>
    /// Ask GitHub for the newest release. Unforced calls do nothing more than once a day, so the poll loop can
    /// call this on every successful poll.
    /// </summary>
    public async Task<bool> CheckAsync(bool force, CancellationToken ct = default)
    {
        if (!force && DateTimeOffset.Now - _lastCheck < s_checkInterval)
        {
            return AvailableTag is not null;
        }

        if (!await _gate.WaitAsync(0, ct))
        {
            return AvailableTag is not null; // a check is already in flight
        }

        try
        {
            _lastCheck = DateTimeOffset.Now;
            // Naming no tag asks for the latest full release, so a prerelease never offers itself as an update.
            var result = await gh.RunAsync(["release", "view", "-R", Repo, "--json", "tagName"], ct);
            var tag = JsonNode.Parse(result.StdOut)?["tagName"]?.GetValue<string>();
            AvailableTag = tag is not null && IsNewer(tag, Current) ? tag : null;
            return AvailableTag is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Download this platform's installer and hand over to it: Windows installs silently once the app has quit and relaunches, macOS
    /// opens the disk image for the drag to Applications.
    /// </summary>
    public async Task InstallAsync(CancellationToken ct = default)
    {
        var tag = AvailableTag ?? throw new InvalidOperationException("No update has been found to install.");

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PrStatusTracker-update"));
        // Naming the file rather than reading the directory back: whatever an abandoned download left behind
        // cannot be picked up as this release's installer, and `gh` fails outright if the pattern is ambiguous.
        var installer = Path.Combine(dir.FullName, OperatingSystem.IsWindows() ? "setup.exe" : "update.dmg");

        await gh.RunAsync(["release", "download", tag, "-R", Repo, "-p", AssetPattern, "-O", installer, "--clobber"], ct);

        if (!OperatingSystem.IsWindows())
        {
            // The bundle is ad-hoc signed and lives in a directory this process may not own, so replacing it in
            // place is the user's move to make. Opening the image is the documented install.
            Process.Start(new ProcessStartInfo("open", [installer]) { UseShellExecute = false });
            return;
        }

        Process.Start(new ProcessStartInfo("powershell.exe", ["-NoProfile", "-WindowStyle", "Hidden", "-Command", HandOverScript(Environment.ProcessId, installer, Environment.ProcessPath!)])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        shutdown();
    }

    /// <summary>
    /// A silent setup that finds the single-instance mutex taken exits with code 2 and installs nothing, and the
    /// script used to relaunch the old app over that without a word. So setup starts only once this process is
    /// gone and the mutex is free. If the silent run still fails, setup runs again with its UI, which says why
    /// and offers a retry, and its finish page starts the new version.
    /// </summary>
    internal static string HandOverScript(int processId, string installer, string app) =>
        $"Wait-Process -Id {processId} -Timeout 60 -ErrorAction SilentlyContinue; " +
        "$deadline = (Get-Date).AddSeconds(30); " +
        "while ((Get-Date) -lt $deadline) { " +
        "$m = $null; " +
        $"if (-not [System.Threading.Mutex]::TryOpenExisting({PowerShellLiteral(SingleInstanceMutex)}, [ref]$m)) {{ break }}; " +
        "$m.Dispose(); Start-Sleep -Milliseconds 250 }; " +
        $"$setup = Start-Process -Wait -PassThru -FilePath {PowerShellLiteral(installer)} -ArgumentList '/S'; " +
        $"if ($setup.ExitCode -eq 0) {{ Start-Process -FilePath {PowerShellLiteral(app)} }} " +
        $"else {{ Start-Process -FilePath {PowerShellLiteral(installer)} }}";

    /// <summary>Shared with Program and the NSIS script, which both take it by this name.</summary>
    public const string SingleInstanceMutex = "PrStatusTracker-SingleInstance";

    /// <summary>Release assets carry the RID: `PrStatusTracker-1.2.3-win-x64-setup.exe`, `PrStatusTracker-1.2.3-osx-arm64.dmg`.</summary>
    private static string AssetPattern =>
        OperatingSystem.IsWindows() ? "*win-x64-setup.exe"
        : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "*osx-arm64.dmg"
        : "*osx-x64.dmg";

    private static string PowerShellLiteral(string path) => $"'{path.Replace("'", "''")}'";

    /// <summary>Tags are `v1.2.3`. A prerelease or build suffix is dropped, so `v1.2.3-beta.1` reads as 1.2.3.</summary>
    internal static Version? ParseTag(string tag)
    {
        var text = tag.TrimStart('v', 'V');
        var suffix = text.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            text = text[..suffix];
        }

        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    // A source build carries no version, so every release would otherwise look like an upgrade to it.
    internal static bool IsNewer(string tag, Version current) =>
        Normalize(current) != s_sourceBuild && ParseTag(tag) is { } tagged && tagged > Normalize(current);

    // Version orders an absent component below a zero one, so the assembly's 1.2.3.0 outranks the tag's 1.2.3
    // until both are cut to three parts.
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
