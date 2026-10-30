using System.IO;
using System.Text.Json;
using VTuberStudioAI.App.Projects;

namespace VTuberStudioAI.App.Preview;

/// <summary>Região de um olho em coordenadas normalizadas (0 a 1) da imagem.</summary>
public sealed record EyeRegion(double X, double Y, double Width, double Height);

public sealed record EyeLayout(string Image, IReadOnlyList<EyeRegion> Eyes);

/// <summary>Guarda no projeto as áreas dos olhos marcadas pelo usuário para o preview.</summary>
public sealed class EyePreviewService(IProjectRepository projectRepository)
{
    public const string ConfigurationKey = "preview.eyes";
    public const int MaxEyes = 4;
    public const double MinSize = 0.02;

    public static bool IsValid(EyeRegion eye) =>
        double.IsFinite(eye.X) && double.IsFinite(eye.Y) &&
        double.IsFinite(eye.Width) && double.IsFinite(eye.Height) &&
        eye.X >= 0 && eye.Y >= 0 &&
        eye.Width >= MinSize && eye.Height >= MinSize &&
        eye.X + eye.Width <= 1.0000001 && eye.Y + eye.Height <= 1.0000001;

    /// <summary>Lê os olhos da imagem indicada; dados ausentes, de outra imagem ou inválidos viram lista vazia.</summary>
    public static IReadOnlyList<EyeRegion> ReadEyes(ProjectDocument project, string? imageReference)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (imageReference is null ||
            !project.Configurations.TryGetValue(ConfigurationKey, out var element))
        {
            return [];
        }

        try
        {
            var layout = element.Deserialize<EyeLayout>();
            if (layout is null || layout.Eyes is null ||
                !string.Equals(layout.Image, imageReference, StringComparison.Ordinal) ||
                layout.Eyes.Count > MaxEyes ||
                layout.Eyes.Any(eye => eye is null || !IsValid(eye)))
            {
                return [];
            }

            return layout.Eyes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public IReadOnlyList<EyeRegion> AddEye(Guid projectId, string imageReference, EyeRegion eye)
    {
        if (!IsValid(eye))
        {
            throw new ArgumentException("A área do olho é muito pequena ou está fora da imagem.");
        }

        var project = projectRepository.Load(projectId);
        if (!project.ResourceReferences.Contains(imageReference, StringComparer.Ordinal))
        {
            throw new InvalidDataException("A imagem não pertence ao projeto.");
        }

        var eyes = ReadEyes(project, imageReference).ToList();
        if (eyes.Count >= MaxEyes)
        {
            throw new InvalidOperationException($"Você pode marcar no máximo {MaxEyes} olhos. Limpe as marcações para recomeçar.");
        }

        eyes.Add(eye);
        var configurations = new Dictionary<string, JsonElement>(project.Configurations, StringComparer.Ordinal)
        {
            [ConfigurationKey] = JsonSerializer.SerializeToElement(new EyeLayout(imageReference, eyes))
        };
        projectRepository.Save(project with { Configurations = configurations });
        return eyes;
    }

    public void ClearEyes(Guid projectId)
    {
        var project = projectRepository.Load(projectId);
        if (!project.Configurations.ContainsKey(ConfigurationKey))
        {
            return;
        }

        var configurations = new Dictionary<string, JsonElement>(project.Configurations, StringComparer.Ordinal);
        configurations.Remove(ConfigurationKey);
        projectRepository.Save(project with { Configurations = configurations });
    }
}
