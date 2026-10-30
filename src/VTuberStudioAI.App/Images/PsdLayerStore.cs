using System.IO;
using System.Text.Json;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;

namespace VTuberStudioAI.App.Images;

public sealed record PsdLayerInfo(string Name, int X, int Y, int Width, int Height, bool Visible, string File, byte Opacity = 255);

public sealed record PsdLayerSet(string Image, string Source, IReadOnlyList<PsdLayerInfo> Layers);

/// <summary>Metadados das camadas de um PSD importado, guardados em <c>psd.layers</c>.</summary>
public static class PsdLayerStore
{
    public const string ConfigurationKey = "psd.layers";

    private static readonly string[] EyeWords = ["olho", "eye"];
    private static readonly string[] ExcludedWords =
        ["fechad", "closed", "blink", "pisc", "brow", "sobrancelha", "lash", "cilio", "cílio", "lid", "palpebra", "pálpebra"];

    /// <summary>Lê as camadas da imagem atual; dados ausentes ou inválidos viram lista vazia.</summary>
    public static IReadOnlyList<PsdLayerInfo> Read(ProjectDocument project, string? imageReference)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (imageReference is null || !project.Configurations.TryGetValue(ConfigurationKey, out var element))
        {
            return [];
        }

        try
        {
            var set = element.Deserialize<PsdLayerSet>();
            return set is { Layers: not null } && string.Equals(set.Image, imageReference, StringComparison.Ordinal)
                ? set.Layers.Where(layer => layer is not null && !string.IsNullOrWhiteSpace(layer.Name)).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Renomeia uma camada (identificada pelo arquivo) e grava no projeto; os PNGs não são tocados.</summary>
    public static IReadOnlyList<PsdLayerInfo> Rename(
        IProjectRepository repository, Guid projectId, string imageReference, string file, string newName)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var error = LayerNaming.Validate(newName);
        if (error is not null)
        {
            throw new ArgumentException(error);
        }

        var project = repository.Load(projectId);
        if (!project.Configurations.TryGetValue(ConfigurationKey, out var element))
        {
            throw new InvalidDataException("O projeto não tem camadas importadas.");
        }

        PsdLayerSet? set;
        try
        {
            set = element.Deserialize<PsdLayerSet>();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Os dados das camadas do projeto estão inválidos.", exception);
        }

        if (set is not { Layers: not null } || !string.Equals(set.Image, imageReference, StringComparison.Ordinal))
        {
            throw new InvalidDataException("As camadas não pertencem à imagem atual do projeto.");
        }

        var layers = set.Layers.ToList();
        var position = layers.FindIndex(layer => string.Equals(layer.File, file, StringComparison.Ordinal));
        if (position < 0)
        {
            throw new InvalidDataException("A camada não foi encontrada no projeto.");
        }

        layers[position] = layers[position] with { Name = newName.Trim() };
        var configurations = new Dictionary<string, JsonElement>(project.Configurations, StringComparer.Ordinal)
        {
            [ConfigurationKey] = JsonSerializer.SerializeToElement(set with { Layers = layers })
        };
        repository.Save(project with { Configurations = configurations });
        return layers;
    }

    /// <summary>Sugere áreas de olho: camadas com papel eyewhite e, sem o molde de nomes, camadas cujo nome indica olho aberto.</summary>
    public static IReadOnlyList<EyeRegion> DetectEyes(PsdDocument psd)
    {
        var eyes = new List<EyeRegion>();
        foreach (var layer in psd.Layers.Where(layer => layer.Visible))
        {
            var role = LayerNaming.Parse(layer.Name);
            var name = layer.Name.ToLowerInvariant();
            var isEye = role.IsRecognized
                ? role.Part == LayerPart.EyeWhite
                : EyeWords.Any(name.Contains) && !ExcludedWords.Any(name.Contains);
            if (!isEye)
            {
                continue;
            }

            var left = Math.Max(0, layer.Left);
            var top = Math.Max(0, layer.Top);
            var right = Math.Min(psd.Width, layer.Left + layer.Width);
            var bottom = Math.Min(psd.Height, layer.Top + layer.Height);
            var eye = new EyeRegion(
                (double)left / psd.Width,
                (double)top / psd.Height,
                (double)(right - left) / psd.Width,
                (double)(bottom - top) / psd.Height);
            if (EyePreviewService.IsValid(eye) && eyes.Count < EyePreviewService.MaxEyes)
            {
                eyes.Add(eye);
            }
        }

        return eyes;
    }
}
