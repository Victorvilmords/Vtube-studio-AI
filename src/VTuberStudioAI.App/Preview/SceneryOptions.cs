using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using VTuberStudioAI.App.Projects;

namespace VTuberStudioAI.App.Preview;

public enum SceneryMode
{
    Transparent,
    Solid
}

public sealed record SceneryPreset(string Name, string Hex);

/// <summary>Cenário da aba Play: janela transparente ou cor sólida (#RRGGBB).</summary>
public sealed record SceneryOptions(SceneryMode Mode, string Color)
{
    public const string ConfigurationKey = "scenery.settings";

    public static SceneryOptions Default { get; } = new(SceneryMode.Solid, "#00B140");

    public static IReadOnlyList<SceneryPreset> Presets { get; } =
    [
        new("Verde chroma", "#00B140"),
        new("Azul chroma", "#0047BB"),
        new("Roxo", "#5B3FA6"),
        new("Grafite", "#1B2230"),
        new("Preto", "#000000"),
        new("Branco", "#FFFFFF")
    ];

    public bool IsValid => Enum.IsDefined(Mode) && TryParseColor(Color, out _);

    public static bool TryParseColor(string? text, out System.Windows.Media.Color color)
    {
        color = default;
        if (text is null || !Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$"))
        {
            return false;
        }

        color = System.Windows.Media.Color.FromRgb(
            Convert.ToByte(text.Substring(1, 2), 16),
            Convert.ToByte(text.Substring(3, 2), 16),
            Convert.ToByte(text.Substring(5, 2), 16));
        return true;
    }

    /// <summary>Pincel de fundo; no modo transparente usa alfa 1/255 para a janela continuar recebendo cliques.</summary>
    public Brush ToBrush()
    {
        if (Mode == SceneryMode.Transparent)
        {
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
        }

        TryParseColor(Color, out var color);
        return new SolidColorBrush(color);
    }
}

public static class SceneryStore
{
    public static SceneryOptions Read(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.Configurations.TryGetValue(SceneryOptions.ConfigurationKey, out var element))
        {
            return SceneryOptions.Default;
        }

        try
        {
            var options = element.Deserialize<SceneryOptions>(Json);
            return options is { IsValid: true } ? options : SceneryOptions.Default;
        }
        catch (JsonException)
        {
            return SceneryOptions.Default;
        }
    }

    public static void Save(IProjectRepository repository, Guid projectId, SceneryOptions options)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid)
        {
            throw new ArgumentException("O cenário precisa de uma cor no formato #RRGGBB.");
        }

        var project = repository.Load(projectId);
        var configurations = new Dictionary<string, JsonElement>(project.Configurations, StringComparer.Ordinal)
        {
            [SceneryOptions.ConfigurationKey] = JsonSerializer.SerializeToElement(options, Json)
        };
        repository.Save(project with { Configurations = configurations });
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
