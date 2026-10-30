using System.Text.Json;
using VTuberStudioAI.App.Projects;

namespace VTuberStudioAI.App.Preview;

/// <summary>Guarda a calibração do rig no projeto (<c>rig.settings</c>); dados inválidos voltam ao padrão.</summary>
public static class RigSettingsStore
{
    public const string ConfigurationKey = "rig.settings";

    public static RigSettings Read(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.Configurations.TryGetValue(ConfigurationKey, out var element))
        {
            return RigSettings.Default;
        }

        try
        {
            var settings = element.Deserialize<RigSettings>();
            return settings is { IsValid: true } ? settings : RigSettings.Default;
        }
        catch (JsonException)
        {
            return RigSettings.Default;
        }
    }

    public static void Save(IProjectRepository repository, Guid projectId, RigSettings settings)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new ArgumentException($"Os valores de calibração devem estar entre 0 e {RigSettings.MaxIntensity}.");
        }

        var project = repository.Load(projectId);
        var configurations = new Dictionary<string, JsonElement>(project.Configurations, StringComparer.Ordinal)
        {
            [ConfigurationKey] = JsonSerializer.SerializeToElement(settings)
        };
        repository.Save(project with { Configurations = configurations });
    }
}
