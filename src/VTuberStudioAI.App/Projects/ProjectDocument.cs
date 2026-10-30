using System.Text.Json;

namespace VTuberStudioAI.App.Projects;

public sealed record ProjectDocument
{
    public const int CurrentFormatVersion = 1;

    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required int FormatVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ModifiedAt { get; init; }

    public required string ContentType { get; init; }

    public required List<string> ResourceReferences { get; init; }

    public required Dictionary<string, JsonElement> Configurations { get; init; }
}

public interface IProjectRepository
{
    IReadOnlyList<ProjectDocument> List();

    ProjectDocument Create(string name);

    ProjectDocument Load(Guid id);

    ProjectDocument Save(ProjectDocument project);

    ProjectDocument Rename(Guid id, string name);

    ProjectDocument Duplicate(Guid id);

    string GetResourcePath(Guid id, string relativeReference);

    void Delete(Guid id);
}
