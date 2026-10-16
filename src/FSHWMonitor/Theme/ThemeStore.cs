using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows.Media;
using FSHWMonitor.Core;

namespace FSHWMonitor.Theme;

/// <summary>
/// Built-in themes plus the user's own from %APPDATA%\FS HW Monitor\themes\*.json. A theme file only needs
/// the colors it changes: <c>{ "Name": "Mine", "Base": "Neon", "Cpu": "#FF7A00" }</c>.
/// </summary>
public static class ThemeStore
{
    static List<AppTheme> _custom = [];

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new ColorJsonConverter() },
    };

    static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string Folder { get; } = Path.Combine(AppSettings.Folder, "themes");

    public static IReadOnlyList<AppTheme> All => [.. Themes.All, .. _custom];

    public static AppTheme Find(string? name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Themes.Default;

    public const string StarterFileName = "OceanTemplate.json";

    /// <summary>
    /// Puts the commented starter theme in the themes folder the first time the app runs, so people
    /// have something to copy. Only once: if they delete it, it stays deleted.
    /// </summary>
    public static void InstallStarterTemplate(AppSettings settings)
    {
        if (settings.StarterThemeInstalled)
            return;
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, StarterFileName);
            if (!File.Exists(path))
            {
                var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{StarterFileName}"));
                using var source = resource.Stream;
                using var target = File.Create(path);
                source.CopyTo(target);
            }
            settings.StarterThemeInstalled = true;
            settings.Save();
        }
        catch (Exception ex)
        {
            Log.Write("Could not add the starter theme: " + ex.Message);
        }
    }

    /// <summary>Re-reads the themes folder. Broken files are skipped and logged.</summary>
    public static void Reload()
    {
        var loaded = new List<AppTheme>();
        if (Directory.Exists(Folder))
        {
            foreach (string file in Directory.GetFiles(Folder, "*.json").Order())
            {
                try
                {
                    var theme = LoadFile(file);
                    if (Themes.All.Concat(loaded).Any(t => string.Equals(t.Name, theme.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException($"a theme named \"{theme.Name}\" already exists");
                    loaded.Add(theme);
                }
                catch (Exception ex)
                {
                    Log.Write($"Theme file {Path.GetFileName(file)} skipped: {ex.Message}");
                }
            }
        }
        _custom = loaded;
    }

    /// <summary>Writes the given theme as a complete, editable JSON file and returns its path.</summary>
    public static string SaveTemplate(AppTheme theme)
    {
        Directory.CreateDirectory(Folder);
        var node = JsonSerializer.SerializeToNode(theme, Json)!.AsObject();
        string name = $"{theme.Name} custom";
        node["Name"] = name;
        node.Remove("Base");

        string path = Path.Combine(Folder, $"{name}.json");
        for (int i = 2; File.Exists(path); i++)
        {
            node["Name"] = $"{name} {i}";
            path = Path.Combine(Folder, $"{name} {i}.json");
        }

        File.WriteAllText(path, node.ToJsonString(Json));
        return path;
    }

    static AppTheme LoadFile(string path)
    {
        var overrides = JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject
                        ?? throw new InvalidDataException("expected a JSON object");

        string baseName = overrides["Base"]?.GetValue<string>() ?? Themes.Default.Name;
        var baseTheme = Themes.All.FirstOrDefault(t => string.Equals(t.Name, baseName, StringComparison.OrdinalIgnoreCase)) ?? Themes.Default;

        // Start from the base theme's values and lay the file's values over them.
        var merged = JsonSerializer.SerializeToNode(baseTheme, Json)!.AsObject();
        foreach (var (key, value) in overrides)
        {
            if (string.Equals(key, "Base", StringComparison.OrdinalIgnoreCase))
                continue;
            string target = merged.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
            merged[target] = value?.DeepClone();
        }
        if (overrides["Name"] is null)
            merged["Name"] = Path.GetFileNameWithoutExtension(path);

        var theme = merged.Deserialize<AppTheme>(Json) ?? throw new InvalidDataException("empty theme");
        theme.IsCustom = true;
        return theme;
    }

    /// <summary>Colors as "#RRGGBB" or "#AARRGGBB" strings.</summary>
    sealed class ColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? text = reader.GetString();
            try
            {
                return (Color)ColorConverter.ConvertFromString(text);
            }
            catch (Exception)
            {
                throw new JsonException($"\"{text}\" is not a color (use #RRGGBB or #AARRGGBB)");
            }
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.A == 255
                ? $"#{value.R:X2}{value.G:X2}{value.B:X2}"
                : $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}");
    }
}
