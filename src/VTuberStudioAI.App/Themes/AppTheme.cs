using System.Globalization;
using System.IO;
using System.Text.Json;

namespace VTuberStudioAI.App.Themes;

/// <summary>Paleta do aplicativo. As cores são aplicadas aos pincéis de recurso de mesmo nome (ex.: SurfaceBrush).</summary>
public sealed record AppTheme(string Id, string Name, string Description, IReadOnlyDictionary<string, string> Colors);

public static class AppThemes
{
    public const string DarkBlueId = "azul-escuro";
    public const string PinkId = "rosa";
    public const string WhiteId = "branco";

    /// <summary>Chaves de recurso (pincéis) que todo tema precisa definir, na mesma ordem das paletas abaixo.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        "AppBackgroundBrush", "SurfaceBrush", "SurfaceRaisedBrush", "BorderBrush", "TextPrimaryBrush", "TextSecondaryBrush",
        "AccentBrush", "SidebarBrush", "InputBrush", "SunkenBrush", "AccentSoftBrush", "AccentChipBrush", "AccentBorderBrush",
        "AccentHoverBrush", "AccentHoverBorderBrush", "AccentDisabledBrush", "AccentStrongBorderBrush", "HoverBrush",
        "HoverBorderBrush", "PressedBrush", "ToggleHoverBrush", "ToggleBrush", "MutedBrush", "ErrorTextBrush", "ErrorBgBrush",
        "ErrorBorderBrush"
    ];

    public static IReadOnlyList<AppTheme> All { get; } =
    [
        Create(DarkBlueId, "Azul escuro", "O tema escuro original, com destaque azul-turquesa.",
        [
            "#10141D", "#171D29", "#202838", "#2B3547", "#F1F5FB", "#9AA8BC",
            "#38C6E8", "#131925", "#121823", "#0D1119", "#173B4B", "#1B3341", "#28576A",
            "#214052", "#32728A", "#1D3543", "#3A6680", "#253145",
            "#35445B", "#2C3B52", "#172231", "#192332", "#596579", "#F28B8B", "#3A252B",
            "#62404A"
        ]),
        Create(PinkId, "Rosa", "Tema claro em tons de rosa.",
        [
            "#FFF0F6", "#FFFFFF", "#FFE3EE", "#F3C4D6", "#3B1426", "#8A5069",
            "#C81E69", "#FFE6F0", "#FFFFFF", "#FFF7FA", "#FFD0E3", "#FFDCEA", "#F29AC0",
            "#FFC2DA", "#E873A5", "#FFE3EE", "#E0679A", "#FFDCEA",
            "#F3B0CB", "#FFCADF", "#FFE9F1", "#FFF3F8", "#C79AB0", "#B3261E", "#FDE4E1",
            "#F0A7A0"
        ]),
        Create(WhiteId, "Branco", "Tema claro neutro, com destaque azul.",
        [
            "#F4F6FA", "#FFFFFF", "#EDF0F6", "#D5DBE6", "#1A2233", "#5B667A",
            "#1F6FEB", "#FFFFFF", "#FFFFFF", "#F7F8FB", "#DCEBFF", "#E8F1FF", "#9CC1F5",
            "#CDE2FF", "#6FA3EA", "#EEF3FA", "#5B93E0", "#E6EAF2",
            "#C3CBDA", "#D9DFEA", "#EEF2F8", "#F6F8FC", "#9AA4B5", "#B3261E", "#FDE7E5",
            "#F0A9A3"
        ])
    ];

    public static AppTheme Default => All[0];

    public static AppTheme? Find(string? id) =>
        All.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.Ordinal));

    public static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' &&
        int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);

    private static AppTheme Create(string id, string name, string description, string[] values)
    {
        if (values.Length != Keys.Count)
        {
            throw new InvalidOperationException($"O tema {id} define {values.Length} cores; esperado {Keys.Count}.");
        }

        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < values.Length; i++)
        {
            colors[Keys[i]] = values[i];
        }

        return new AppTheme(id, name, description, colors);
    }
}

/// <summary>Preferências do aplicativo (independentes de projeto), gravadas em JSON na pasta local do usuário.</summary>
public sealed class AppSettingsStore(string? path = null)
{
    private sealed record Data(string? ThemeId);

    public string FilePath { get; } = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTuberStudioAI", "settings.json");

    /// <summary>Id do tema salvo ou nulo se não houver arquivo, ele estiver ilegível ou o tema for desconhecido.</summary>
    public string? ReadThemeId()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
            return AppThemes.Find(data?.ThemeId)?.Id;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void SaveThemeId(string themeId)
    {
        if (AppThemes.Find(themeId) is null)
        {
            throw new ArgumentException("Tema desconhecido.", nameof(themeId));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Data(themeId)));
        File.Move(temporary, FilePath, overwrite: true);
    }
}

/// <summary>Escolhe, aplica e guarda o tema. A aplicação visual é injetada (<c>apply</c>) para ser testável.</summary>
public sealed class ThemeService(Action<AppTheme> apply, AppSettingsStore? store = null)
{
    public AppTheme Current { get; private set; } = AppThemes.Default;

    /// <summary>Aplica o tema salvo (ou o padrão se não houver nenhum válido).</summary>
    public void Initialize()
    {
        Current = AppThemes.Find(store?.ReadThemeId()) ?? AppThemes.Default;
        apply(Current);
    }

    /// <summary>Aplica o tema na hora e tenta guardá-lo; devolve uma mensagem de erro se não conseguir guardar.</summary>
    public string? Select(string themeId)
    {
        var theme = AppThemes.Find(themeId) ?? throw new ArgumentException("Tema desconhecido.", nameof(themeId));
        Current = theme;
        apply(theme);
        if (store is null)
        {
            return null;
        }

        try
        {
            store.SaveThemeId(theme.Id);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "O tema foi aplicado, mas não foi possível guardar a escolha para a próxima vez.";
        }
    }
}
