using System.IO;
using System.Text.Json;
using VTuberStudioAI.App.Projects;

namespace VTuberStudioAI.App.Furry;

public sealed class FurryStudioService(IProjectRepository projectRepository)
{
    private readonly IProjectRepository _projectRepository =
        projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));

    public IReadOnlyList<FurryComponent> Load(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var savedProject = _projectRepository.Load(project.Id);
        return ReadComponents(savedProject);
    }

    public FurryComponent Add(ProjectDocument project, FurryComponentType type, string name)
    {
        var components = Load(project).ToList();
        if (components.Count >= FurryComponentCatalog.MaximumComponentCount)
        {
            throw new InvalidOperationException(
                $"Um projeto pode ter até {FurryComponentCatalog.MaximumComponentCount} componentes furry.");
        }

        var component = FurryComponentCatalog.Create(type, name);
        components.Add(component);
        SaveComponents(project.Id, components);
        return component;
    }

    public FurryComponent Rename(ProjectDocument project, Guid componentId, string name)
    {
        var components = Load(project).ToList();
        var index = FindComponentIndex(components, componentId);
        var renamed = components[index] with { Name = FurryComponentCatalog.ValidateName(name) };
        FurryComponentCatalog.Validate(renamed);
        components[index] = renamed;
        SaveComponents(project.Id, components);
        return renamed;
    }

    public FurryComponent SetEnabled(ProjectDocument project, Guid componentId, bool isEnabled)
    {
        var components = Load(project).ToList();
        var index = FindComponentIndex(components, componentId);
        var updated = components[index] with { IsEnabled = isEnabled };
        FurryComponentCatalog.Validate(updated);
        components[index] = updated;
        SaveComponents(project.Id, components);
        return updated;
    }

    public FurryComponent SetNumberParameter(
        ProjectDocument project,
        Guid componentId,
        string key,
        double value)
    {
        var component = Load(project).SingleOrDefault(item => item.Id == componentId)
            ?? throw new InvalidOperationException("O componente selecionado não pertence ao projeto.");
        var definition = GetDefinition(component, key, FurryParameterKind.Number);
        if (!double.IsFinite(value) || value < definition.Minimum || value > definition.Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"O parâmetro “{definition.Label}” deve estar entre {definition.Minimum} e {definition.Maximum}.");
        }

        var updated = ReplaceParameter(component, key, JsonSerializer.SerializeToElement(value));
        SaveComponent(project.Id, updated);
        return updated;
    }

    public FurryComponent SetBooleanParameter(
        ProjectDocument project,
        Guid componentId,
        string key,
        bool value)
    {
        var component = Load(project).SingleOrDefault(item => item.Id == componentId)
            ?? throw new InvalidOperationException("O componente selecionado não pertence ao projeto.");
        _ = GetDefinition(component, key, FurryParameterKind.Boolean);
        var updated = ReplaceParameter(component, key, JsonSerializer.SerializeToElement(value));
        SaveComponent(project.Id, updated);
        return updated;
    }

    public void Remove(ProjectDocument project, Guid componentId)
    {
        var components = Load(project).ToList();
        var index = FindComponentIndex(components, componentId);
        components.RemoveAt(index);
        SaveComponents(project.Id, components);
    }

    public FurryComponent AddPreset(ProjectDocument project, string presetKey)
    {
        var (type, name, values) = presetKey switch
        {
            "ears-neutral" => (
                FurryComponentType.Ears,
                "Orelhas — neutro (configuração)",
                new Dictionary<string, object>
                {
                    ["scale"] = 1d,
                    ["angle"] = 0d,
                    ["stiffness"] = 0.5d,
                    ["damping"] = 0.5d,
                    ["tufts"] = false
                }),
            "ears-alert" => (
                FurryComponentType.Ears,
                "Orelhas — alerta (configuração)",
                new Dictionary<string, object>
                {
                    ["scale"] = 1d,
                    ["angle"] = 15d,
                    ["stiffness"] = 0.7d,
                    ["damping"] = 0.4d,
                    ["tufts"] = false
                }),
            "tail-soft" => (
                FurryComponentType.Tail,
                "Cauda — suave (configuração)",
                new Dictionary<string, object>
                {
                    ["length"] = 1d,
                    ["angle"] = 0d,
                    ["movementLimit"] = 35d,
                    ["stiffness"] = 0.3d,
                    ["damping"] = 0.7d
                }),
            _ => throw new ArgumentException("Configuração inicial desconhecida.", nameof(presetKey))
        };
        var component = FurryComponentCatalog.Create(type, name);
        var parameters = component.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            parameters[key] = JsonSerializer.SerializeToElement(value, value.GetType());
        }

        component = component with { Parameters = parameters };
        FurryComponentCatalog.Validate(component);
        var components = Load(project).ToList();
        if (components.Count >= FurryComponentCatalog.MaximumComponentCount)
        {
            throw new InvalidOperationException(
                $"Um projeto pode ter até {FurryComponentCatalog.MaximumComponentCount} componentes furry.");
        }

        components.Add(component);
        SaveComponents(project.Id, components);
        return component;
    }

    public static IReadOnlyList<FurryComponent> ReadComponents(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.Configurations.TryGetValue(FurryComponentCatalog.ConfigurationKey, out var element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("A configuração de componentes furry deve ser uma lista JSON.");
        }

        List<FurryComponent>? components;
        try
        {
            components = element.Deserialize<List<FurryComponent>>();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A lista de componentes furry está corrompida.", exception);
        }

        if (components is null || components.Count > FurryComponentCatalog.MaximumComponentCount)
        {
            throw new InvalidDataException("A lista de componentes furry está ausente ou excede o limite.");
        }

        var ids = new HashSet<Guid>();
        foreach (var component in components)
        {
            FurryComponentCatalog.Validate(component);
            if (!ids.Add(component.Id))
            {
                throw new InvalidDataException("A lista contém identificadores de componente duplicados.");
            }
        }

        return components;
    }

    public static void ValidateProjectConfiguration(ProjectDocument project) => _ = ReadComponents(project);

    private void SaveComponent(Guid projectId, FurryComponent component)
    {
        var components = Load(_projectRepository.Load(projectId)).ToList();
        var index = FindComponentIndex(components, component.Id);
        components[index] = component;
        SaveComponents(projectId, components);
    }

    private void SaveComponents(Guid projectId, List<FurryComponent> components)
    {
        var project = _projectRepository.Load(projectId);
        var configurations = new Dictionary<string, JsonElement>(project.Configurations, StringComparer.Ordinal)
        {
            [FurryComponentCatalog.ConfigurationKey] = JsonSerializer.SerializeToElement(components)
        };
        _projectRepository.Save(project with { Configurations = configurations });
    }

    private static FurryComponent ReplaceParameter(
        FurryComponent component,
        string key,
        JsonElement value)
    {
        var parameters = new Dictionary<string, JsonElement>(component.Parameters, StringComparer.Ordinal)
        {
            [key] = value
        };
        var updated = component with { Parameters = parameters };
        FurryComponentCatalog.Validate(updated);
        return updated;
    }

    private static FurryParameterDefinition GetDefinition(
        FurryComponent component,
        string key,
        FurryParameterKind expectedKind)
    {
        var definition = FurryComponentCatalog.GetParameters(component.Type)
            .SingleOrDefault(parameter => parameter.Key == key)
            ?? throw new ArgumentException(
                $"O componente {FurryComponentCatalog.GetTypeLabel(component.Type)} não oferece o parâmetro “{key}”.",
                nameof(key));
        if (definition.Kind != expectedKind)
        {
            throw new ArgumentException($"O parâmetro “{definition.Label}” não aceita esse tipo de valor.", nameof(key));
        }

        return definition;
    }

    private static int FindComponentIndex(IReadOnlyList<FurryComponent> components, Guid componentId)
    {
        var index = -1;
        for (var candidate = 0; candidate < components.Count; candidate++)
        {
            if (components[candidate].Id == componentId)
            {
                index = candidate;
                break;
            }
        }

        return index >= 0
            ? index
            : throw new InvalidOperationException("O componente selecionado não pertence ao projeto.");
    }
}
