using System.Text.Json.Serialization;
using PrStatusTracker.Models;

namespace PrStatusTracker.Core;

/// <summary>
/// Source-generated serializer metadata for every document the app persists. Keeps <see cref="JsonFileStore{T}"/>
/// free of reflection so the app trims and compiles under NativeAOT, and skips the reflection warm-up at startup.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<TrackedPr>))]
[JsonSerializable(typeof(List<NotificationRecord>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, DateTimeOffset>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
