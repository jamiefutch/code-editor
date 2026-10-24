using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace CodeEditor;

/// <summary>
///     Persisted editor settings. Loaded from ~/.tui/codeeditor.config.json on startup,
///     saved when View menu settings change. Uses Microsoft.Extensions.Configuration for the read path.
/// </summary>
internal static class EditorConfig
{
    internal const string SectionName = "EditorSettings";

    // ── Public properties with defaults matching ted ──────────────────────────────

    public static bool LineNumbers { get; set; } = true;
    public static bool FoldIndicators { get; set; } = true;
    public static bool WordWrap { get; set; }
    public static bool ShowTabs { get; set; }
    public static int IndentSize { get; set; } = 4;
    public static bool ConvertTabsToSpaces { get; set; } = true;
    public static bool AutoIndent { get; set; } = true;
    public static bool Scrollbars { get; set; } = true;
    public static bool VirtualSpace { get; set; } = true;

    // ── Config path ───────────────────────────────────────────────────────────────

    internal static string GetConfigPath()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".tui", "codeeditor.config.json");
    }

    // ── Load ──────────────────────────────────────────────────────────────────────

    internal static void LoadAndApply()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
            return;

        try
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(path, true, false)
                .Build();

            Apply(config);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EditorConfig.Load: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void Apply(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var values = new EditorSettingsValues();
        ApplyLegacyDottedKeys(configuration, values);
        configuration.GetSection(SectionName).Bind(values);
        Apply(values);
    }

    private static void Apply(EditorSettingsValues v)
    {
        LineNumbers = v.LineNumbers;
        FoldIndicators = v.FoldIndicators;
        WordWrap = v.WordWrap;
        ShowTabs = v.ShowTabs;
        IndentSize = v.IndentSize;
        ConvertTabsToSpaces = v.ConvertTabsToSpaces;
        AutoIndent = v.AutoIndent;
        Scrollbars = v.Scrollbars;
        VirtualSpace = v.VirtualSpace;
    }

    // ── Save ──────────────────────────────────────────────────────────────────────

    internal static void Save()
    {
        var path = GetConfigPath();

        try
        {
            JsonObject root = ReadRoot(path);

            foreach (var key in root
                         .Where(kvp => kvp.Key.StartsWith($"{SectionName}.", StringComparison.Ordinal))
                         .Select(kvp => kvp.Key)
                         .ToList())
            {
                root.Remove(key);
            }

            if (root["AppSettings"] is JsonObject appSettings)
            {
                foreach (var key in appSettings
                             .Where(kvp => kvp.Key.StartsWith($"{SectionName}.", StringComparison.Ordinal))
                             .Select(kvp => kvp.Key)
                             .ToList())
                {
                    appSettings.Remove(key);
                }

                if (appSettings.Count == 0)
                    root.Remove("AppSettings");
            }

            root[SectionName] = new JsonObject
            {
                [nameof(LineNumbers)] = LineNumbers,
                [nameof(FoldIndicators)] = FoldIndicators,
                [nameof(WordWrap)] = WordWrap,
                [nameof(ShowTabs)] = ShowTabs,
                [nameof(IndentSize)] = IndentSize,
                [nameof(ConvertTabsToSpaces)] = ConvertTabsToSpaces,
                [nameof(AutoIndent)] = AutoIndent,
                [nameof(Scrollbars)] = Scrollbars,
                [nameof(VirtualSpace)] = VirtualSpace
            };

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EditorConfig.Save: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static JsonObject ReadRoot(string path)
    {
        if (!File.Exists(path)) return new JsonObject();

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();

        JsonNode? node = JsonNode.Parse(
            text,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

        return node as JsonObject ?? new JsonObject();
    }

    private static void ApplyLegacyDottedKeys(IConfiguration config, EditorSettingsValues values)
    {
        if (config[$"{SectionName}.LineNumbers"] is string v1 && bool.TryParse(v1, out var b1))
            values.LineNumbers = b1;
        if (config[$"{SectionName}.FoldIndicators"] is string v2 && bool.TryParse(v2, out var b2))
            values.FoldIndicators = b2;
        if (config[$"{SectionName}.WordWrap"] is string v3 && bool.TryParse(v3, out var b3))
            values.WordWrap = b3;
        if (config[$"{SectionName}.ShowTabs"] is string v4 && bool.TryParse(v4, out var b4))
            values.ShowTabs = b4;
        if (config[$"{SectionName}.IndentSize"] is string v5 && int.TryParse(v5, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i5))
            values.IndentSize = i5;
        if (config[$"{SectionName}.ConvertTabsToSpaces"] is string v6 && bool.TryParse(v6, out var b6))
            values.ConvertTabsToSpaces = b6;
        if (config[$"{SectionName}.AutoIndent"] is string v7 && bool.TryParse(v7, out var b7))
            values.AutoIndent = b7;
        if (config[$"{SectionName}.Scrollbars"] is string v8 && bool.TryParse(v8, out var b8))
            values.Scrollbars = b8;
        if (config[$"{SectionName}.VirtualSpace"] is string v9 && bool.TryParse(v9, out var b9))
            values.VirtualSpace = b9;
    }
}

internal sealed class EditorSettingsValues
{
    public bool LineNumbers { get; set; } = true;
    public bool FoldIndicators { get; set; } = true;
    public bool WordWrap { get; set; }
    public bool ShowTabs { get; set; }
    public int IndentSize { get; set; } = 4;
    public bool ConvertTabsToSpaces { get; set; } = true;
    public bool AutoIndent { get; set; } = true;
    public bool Scrollbars { get; set; } = true;
    public bool VirtualSpace { get; set; } = true;
}
