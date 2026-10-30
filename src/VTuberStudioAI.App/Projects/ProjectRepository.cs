using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VTuberStudioAI.App.Furry;

namespace VTuberStudioAI.App.Projects;

public sealed class ProjectRepository : IProjectRepository
{
    private const string MetadataFileName = "project.json";
    private const int MaximumNameLength = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _rootDirectory;

    public ProjectRepository(string? rootDirectory = null)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VTuberStudioAI",
            "Projects"));
    }

    private static string BuildDuplicateName(string sourceName, int suffix)
    {
        var suffixText = suffix == 1 ? " (cópia)" : $" (cópia {suffix})";
        var availableNameLength = MaximumNameLength - suffixText.Length;
        var name = sourceName.Length > availableNameLength
            ? sourceName[..availableNameLength]
            : sourceName;
        return $"{name}{suffixText}";
    }

    public IReadOnlyList<ProjectDocument> List()
    {
        if (File.Exists(_rootDirectory))
        {
            throw new IOException("O caminho da biblioteca existe como arquivo, não como pasta.");
        }

        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        EnsureNotReparsePoint(_rootDirectory);
        var projects = new List<ProjectDocument>();
        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            if (Guid.TryParseExact(Path.GetFileName(directory), "D", out var id))
            {
                projects.Add(Load(id));
            }
        }

        return projects
            .OrderByDescending(project => project.ModifiedAt)
            .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public ProjectDocument Create(string name)
    {
        var now = DateTimeOffset.UtcNow;
        var project = new ProjectDocument
        {
            Id = Guid.NewGuid(),
            Name = ValidateName(name),
            FormatVersion = ProjectDocument.CurrentFormatVersion,
            CreatedAt = now,
            ModifiedAt = now,
            ContentType = "2D",
            ResourceReferences = [],
            Configurations = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        };

        Directory.CreateDirectory(_rootDirectory);
        EnsureNotReparsePoint(_rootDirectory);
        var projectDirectory = GetProjectDirectory(project.Id);
        Directory.CreateDirectory(projectDirectory);
        try
        {
            WriteDocument(project);
            return project;
        }
        catch (Exception creationException)
        {
            try
            {
                EnsureTreeHasNoReparsePoints(projectDirectory);
                Directory.Delete(projectDirectory, recursive: true);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Não foi possível criar o projeto e a pasta temporária também não pôde ser removida.",
                    creationException,
                    cleanupException);
            }

            throw;
        }
    }

    public ProjectDocument Load(Guid id)
    {
        var projectDirectory = GetProjectDirectory(id);
        EnsureNotReparsePoint(projectDirectory);
        var metadataPath = Path.Combine(projectDirectory, MetadataFileName);
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("O arquivo de dados do projeto não foi encontrado.", metadataPath);
        }

        EnsureNotReparsePoint(metadataPath);
        ProjectDocument? project;
        try
        {
            project = JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(metadataPath), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("O arquivo JSON do projeto está corrompido ou em formato inválido.", exception);
        }

        if (project is null)
        {
            throw new InvalidDataException("O arquivo do projeto não contém dados válidos.");
        }

        ValidateProject(project, id);
        ValidateResourceReferences(projectDirectory, project.ResourceReferences, requireFiles: true);
        return project;
    }

    public ProjectDocument Save(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var existing = Load(project.Id);
        if (project.CreatedAt != existing.CreatedAt)
        {
            throw new InvalidDataException("A data de criação do projeto não pode ser alterada.");
        }

        var updatedProject = project with
        {
            Name = ValidateName(project.Name),
            FormatVersion = ProjectDocument.CurrentFormatVersion,
            ModifiedAt = DateTimeOffset.UtcNow
        };
        ValidateProject(updatedProject, existing.Id);
        ValidateResourceReferences(GetProjectDirectory(updatedProject.Id), updatedProject.ResourceReferences, requireFiles: true);
        WriteDocument(updatedProject);
        return updatedProject;
    }

    public ProjectDocument Rename(Guid id, string name)
    {
        var project = Load(id);
        return Save(project with { Name = ValidateName(name) });
    }

    public ProjectDocument Duplicate(Guid id)
    {
        var source = Load(id);
        var existingNames = List().Select(project => project.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateName = BuildDuplicateName(source.Name, 1);
        for (var suffix = 2; existingNames.Contains(duplicateName); suffix++)
        {
            duplicateName = BuildDuplicateName(source.Name, suffix);
        }

        var duplicate = source with
        {
            Id = Guid.NewGuid(),
            Name = ValidateName(duplicateName),
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            ResourceReferences = [.. source.ResourceReferences],
            Configurations = new Dictionary<string, JsonElement>(source.Configurations, StringComparer.Ordinal)
        };
        var sourceDirectory = GetProjectDirectory(source.Id);
        var duplicateDirectory = GetProjectDirectory(duplicate.Id);
        Directory.CreateDirectory(duplicateDirectory);
        try
        {
            foreach (var reference in source.ResourceReferences)
            {
                var sourceFile = ResolveResourcePath(sourceDirectory, reference);
                var destinationFile = ResolveResourcePath(duplicateDirectory, reference);
                var destinationFolder = Path.GetDirectoryName(destinationFile)!;
                Directory.CreateDirectory(destinationFolder);
                File.Copy(sourceFile, destinationFile);
            }

            WriteDocument(duplicate);
            return duplicate;
        }
        catch (Exception duplicateException)
        {
            try
            {
                EnsureTreeHasNoReparsePoints(duplicateDirectory);
                Directory.Delete(duplicateDirectory, recursive: true);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Não foi possível duplicar o projeto e a pasta temporária também não pôde ser removida.",
                    duplicateException,
                    cleanupException);
            }

            throw;
        }
    }

    public string GetResourcePath(Guid id, string relativeReference)
    {
        var projectDirectory = GetProjectDirectory(id);
        EnsureNotReparsePoint(projectDirectory);
        ValidateRelativeResourceReference(relativeReference);
        var path = ResolveResourcePath(projectDirectory, relativeReference);
        EnsureNoReparsePointsInPath(projectDirectory, path);
        return path;
    }

    public void Delete(Guid id)
    {
        _ = Load(id);
        var projectDirectory = GetProjectDirectory(id);
        EnsureTreeHasNoReparsePoints(projectDirectory);
        Directory.Delete(projectDirectory, recursive: true);
    }

    public static string ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("Informe um nome para o projeto.", nameof(name));
        }

        if (normalizedName.Length > MaximumNameLength)
        {
            throw new ArgumentException($"O nome do projeto deve ter no máximo {MaximumNameLength} caracteres.", nameof(name));
        }

        if (normalizedName.EndsWith('.') ||
            normalizedName.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
        {
            throw new ArgumentException("O nome contém caracteres não permitidos.", nameof(name));
        }

        var deviceName = normalizedName.Split('.')[0];
        if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (deviceName.Length == 4 &&
             (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             deviceName[3] is >= '1' and <= '9'))
        {
            throw new ArgumentException("Esse nome é reservado pelo Windows.", nameof(name));
        }

        return normalizedName;
    }

    private string GetProjectDirectory(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O identificador do projeto é inválido.", nameof(id));
        }

        var path = Path.GetFullPath(Path.Combine(_rootDirectory, id.ToString("D")));
        var expectedParent = Path.TrimEndingDirectorySeparator(_rootDirectory);
        if (!Path.GetDirectoryName(path)!.Equals(expectedParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O caminho do projeto está fora da biblioteca local.");
        }

        return path;
    }

    private void WriteDocument(ProjectDocument project)
    {
        var projectDirectory = GetProjectDirectory(project.Id);
        EnsureNotReparsePoint(projectDirectory);
        var metadataPath = Path.Combine(projectDirectory, MetadataFileName);
        var temporaryPath = Path.Combine(projectDirectory, $".{MetadataFileName}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(project, JsonOptions);

        try
        {
            if (File.Exists(metadataPath))
            {
                EnsureNotReparsePoint(metadataPath);
            }

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        catch (Exception writeException)
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Não foi possível salvar o projeto e o arquivo temporário também não pôde ser removido.",
                    writeException,
                    cleanupException);
            }

            throw;
        }
    }

    private static void ValidateProject(ProjectDocument project, Guid expectedId)
    {
        if (project.Id == Guid.Empty || project.Id != expectedId)
        {
            throw new InvalidDataException("O identificador no arquivo não corresponde à pasta do projeto.");
        }

        if (project.FormatVersion != ProjectDocument.CurrentFormatVersion)
        {
            throw new InvalidDataException($"A versão {project.FormatVersion} do formato de projeto não é compatível.");
        }

        if (!string.Equals(ValidateName(project.Name), project.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("O nome salvo do projeto contém espaços externos ou formato inválido.");
        }
        if (project.CreatedAt == default ||
            project.ModifiedAt == default ||
            project.ModifiedAt < project.CreatedAt)
        {
            throw new InvalidDataException("As datas do projeto estão ausentes ou são inválidas.");
        }

        if (string.IsNullOrWhiteSpace(project.ContentType))
        {
            throw new InvalidDataException("O tipo de conteúdo do projeto não foi informado.");
        }

        if (project.ResourceReferences is null || project.Configurations is null)
        {
            throw new InvalidDataException("A lista de recursos ou as configurações do projeto estão ausentes.");
        }

        foreach (var (key, value) in project.Configurations)
        {
            if (string.IsNullOrWhiteSpace(key) || value.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException("Uma configuração do projeto está inválida.");
            }
        }

        FurryStudioService.ValidateProjectConfiguration(project);
    }

    private static void ValidateResourceReferences(
        string projectDirectory,
        IEnumerable<string> references,
        bool requireFiles)
    {
        var uniqueReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            ValidateRelativeResourceReference(reference);
            var filePath = ResolveResourcePath(projectDirectory, reference);
            if (!uniqueReferences.Add(filePath))
            {
                throw new InvalidDataException("O projeto contém referências de recursos duplicadas.");
            }

            EnsureNoReparsePointsInPath(projectDirectory, filePath);
            if (requireFiles && !File.Exists(filePath))
            {
                throw new FileNotFoundException($"Um recurso referenciado pelo projeto não foi encontrado: {reference}", filePath);
            }
        }
    }

    private static void ValidateRelativeResourceReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) ||
            Path.IsPathRooted(reference) ||
            reference.Contains(':') ||
            reference.Split(['/', '\\']).Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("O projeto contém uma referência de recurso inválida.");
        }
    }

    private static string ResolveResourcePath(string projectDirectory, string reference)
    {
        var path = Path.GetFullPath(Path.Combine(projectDirectory, reference.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Uma referência de recurso aponta para fora da pasta do projeto.");
        }

        return path;
    }

    private static void EnsureNoReparsePointsInPath(string root, string target)
    {
        EnsureNotReparsePoint(root);
        var relativePath = Path.GetRelativePath(root, target);
        var current = root;
        var segments = relativePath.Split(Path.DirectorySeparatorChar);
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (File.Exists(current) || Directory.Exists(current))
            {
                EnsureNotReparsePoint(current);
            }
            else if (index < segments.Length - 1)
            {
                return;
            }
        }
    }

    private static void EnsureTreeHasNoReparsePoints(string directory)
    {
        EnsureNotReparsePoint(directory);
        var directories = new Stack<string>();
        directories.Push(directory);
        while (directories.TryPop(out var currentDirectory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                EnsureNotReparsePoint(entry);
                if (Directory.Exists(entry))
                {
                    directories.Push(entry);
                }
            }
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("A biblioteca contém um link de sistema de arquivos não permitido.");
        }
    }
}
