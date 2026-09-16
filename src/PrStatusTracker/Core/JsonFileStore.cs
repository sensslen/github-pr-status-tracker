using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PrStatusTracker.Core;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrStatusTracker");

    public static string Settings => Path.Combine(DataDirectory, "settings.json");
    public static string Notifications => Path.Combine(DataDirectory, "notifications.json");
    public static string Snapshot => Path.Combine(DataDirectory, "snapshot.json");
}

/// <summary>Load/save one JSON document. Writes are atomic; a corrupt file is set aside, not fatal.</summary>
/// <remarks><typeparamref name="T"/> must be registered on <see cref="AppJsonContext"/>; the type initializer fails fast otherwise.</remarks>
public sealed class JsonFileStore<T>(string path) where T : class
{
    private static readonly JsonTypeInfo<T> s_typeInfo =
        AppJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException($"{typeof(T)} is not registered on {nameof(AppJsonContext)}");

    private readonly Lock _gate = new();

    public T Load(Func<T> fallback) => LoadOrNull() ?? fallback();

    /// <summary>Null when there is no usable file: missing, unreadable, or corrupt (the last is set aside).</summary>
    public T? LoadOrNull()
    {
        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize(File.ReadAllText(path), s_typeInfo);
            }
            catch (JsonException)
            {
                try { File.Move(path, path + ".corrupt", overwrite: true); } catch { /* best effort */ }
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not read {path}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>Best effort: a failed write is logged, never thrown into UI handlers or the poll loop.</summary>
    public bool Save(T value)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                using (var stream = File.Create(tmp))
                {
                    JsonSerializer.Serialize(stream, value, s_typeInfo);
                }

                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not write {path}: {ex.Message}");
                return false;
            }
        }
    }
}
