using System.IO;
using System.Text.Json;

namespace VTuberStudioAI.App.Furry;

public enum FurryComponentType
{
    Ears,
    Horns,
    Muzzle,
    Tail,
    FurTufts,
    Accessories,
    Custom
}

public enum FurryParameterKind
{
    Number,
    Boolean
}

public sealed record FurryParameterDefinition(
    string Key,
    string Label,
    FurryParameterKind Kind,
    double? Minimum,
    double? Maximum,
    double? DefaultNumber,
    bool? DefaultBoolean);

public sealed record FurryComponent
{
    public required Guid Id { get; init; }

    public required FurryComponentType Type { get; init; }

    public required string Name { get; init; }

    public required bool IsEnabled { get; init; }

    public required Dictionary<string, JsonElement> Parameters { get; init; }
}

public sealed record FurryComponentTypeOption(FurryComponentType Type, string Label);

public static class FurryComponentCatalog
{
    public const string ConfigurationKey = "furry.components";
    public const int MaximumComponentCount = 100;
    public const int MaximumNameLength = 80;

    private static readonly IReadOnlyDictionary<FurryComponentType, FurryComponentTypeOption> TypeOptions =
        new Dictionary<FurryComponentType, FurryComponentTypeOption>
        {
            [FurryComponentType.Ears] = new(FurryComponentType.Ears, "Orelhas"),
            [FurryComponentType.Horns] = new(FurryComponentType.Horns, "Chifres"),
            [FurryComponentType.Muzzle] = new(FurryComponentType.Muzzle, "Focinho"),
            [FurryComponentType.Tail] = new(FurryComponentType.Tail, "Cauda"),
            [FurryComponentType.FurTufts] = new(FurryComponentType.FurTufts, "Pelagem / tufos"),
            [FurryComponentType.Accessories] = new(FurryComponentType.Accessories, "Acessórios"),
            [FurryComponentType.Custom] = new(FurryComponentType.Custom, "Personalizado")
        };

    private static readonly IReadOnlyDictionary<FurryComponentType, IReadOnlyList<FurryParameterDefinition>> Definitions =
        new Dictionary<FurryComponentType, IReadOnlyList<FurryParameterDefinition>>
        {
            [FurryComponentType.Ears] =
            [
                Number("scale", "Escala", 0.1, 3, 1),
                Number("angle", "Ângulo", -90, 90, 0),
                Number("stiffness", "Rigidez", 0, 1, 0.5),
                Number("damping", "Amortecimento", 0, 1, 0.5),
                Boolean("tufts", "Incluir tufos", false)
            ],
            [FurryComponentType.Horns] =
            [
                Number("scale", "Escala", 0.1, 3, 1),
                Number("angle", "Ângulo", -90, 90, 0),
                Boolean("paired", "Em par", true)
            ],
            [FurryComponentType.Muzzle] =
            [
                Number("scale", "Escala", 0.1, 3, 1),
                Number("protrusion", "Projeção", 0, 2, 0.5),
                Boolean("whiskers", "Com bigodes", false)
            ],
            [FurryComponentType.Tail] =
            [
                Number("length", "Comprimento", 0.1, 3, 1),
                Number("angle", "Ângulo", -90, 90, 0),
                Number("movementLimit", "Limite de movimento", 0, 180, 45),
                Number("stiffness", "Rigidez", 0, 1, 0.5),
                Number("damping", "Amortecimento", 0, 1, 0.5)
            ],
            [FurryComponentType.FurTufts] =
            [
                Number("length", "Comprimento dos tufos", 0, 3, 1),
                Number("density", "Densidade", 0, 1, 0.5),
                Boolean("colorVariation", "Variação de cor", false)
            ],
            [FurryComponentType.Accessories] =
            [
                Number("scale", "Escala", 0.1, 3, 1),
                Number("angle", "Ângulo", -180, 180, 0),
                Boolean("attached", "Anexado ao personagem", true)
            ],
            [FurryComponentType.Custom] =
            [
                Number("intensity", "Intensidade", 0, 1, 0.5),
                Number("angle", "Ângulo", -180, 180, 0),
                Number("stiffness", "Rigidez", 0, 1, 0.5),
                Number("damping", "Amortecimento", 0, 1, 0.5),
                Number("movementLimit", "Limite de movimento", 0, 180, 45),
                Boolean("enabledOption", "Opção personalizada", true)
            ]
        };

    public static IReadOnlyList<FurryComponentTypeOption> Types { get; } =
        Enum.GetValues<FurryComponentType>().Select(type => TypeOptions[type]).ToArray();

    public static IReadOnlyList<FurryParameterDefinition> GetParameters(FurryComponentType type) =>
        Definitions.TryGetValue(type, out var definitions)
            ? definitions
            : throw new InvalidDataException($"Tipo de componente furry desconhecido: {type}.");

    public static string GetTypeLabel(FurryComponentType type) =>
        TypeOptions.TryGetValue(type, out var option)
            ? option.Label
            : throw new InvalidDataException($"Tipo de componente furry desconhecido: {type}.");

    public static FurryComponent Create(FurryComponentType type, string name, bool isEnabled = true)
    {
        var normalizedName = ValidateName(name);
        var parameters = GetParameters(type).ToDictionary(
            definition => definition.Key,
            definition => definition.Kind switch
            {
                FurryParameterKind.Number => JsonSerializer.SerializeToElement(definition.DefaultNumber!.Value),
                FurryParameterKind.Boolean => JsonSerializer.SerializeToElement(definition.DefaultBoolean!.Value),
                _ => throw new InvalidDataException("Tipo de parâmetro furry inválido.")
            },
            StringComparer.Ordinal);

        return new FurryComponent
        {
            Id = Guid.NewGuid(),
            Type = type,
            Name = normalizedName,
            IsEnabled = isEnabled,
            Parameters = parameters
        };
    }

    public static string ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0 || normalizedName.Length > MaximumNameLength)
        {
            throw new ArgumentException($"O nome do componente deve ter entre 1 e {MaximumNameLength} caracteres.", nameof(name));
        }

        if (normalizedName.Any(char.IsControl))
        {
            throw new ArgumentException("O nome do componente contém caracteres de controle.", nameof(name));
        }

        return normalizedName;
    }

    public static void Validate(FurryComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.Id == Guid.Empty)
        {
            throw new InvalidDataException("O identificador do componente furry é inválido.");
        }

        if (!Enum.IsDefined(component.Type))
        {
            throw new InvalidDataException("O tipo do componente furry é desconhecido.");
        }

        if (!string.Equals(ValidateName(component.Name), component.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("O nome salvo do componente contém espaços externos ou formato inválido.");
        }

        if (component.Parameters is null)
        {
            throw new InvalidDataException("Os parâmetros do componente furry estão ausentes.");
        }

        var definitions = GetParameters(component.Type);
        if (component.Parameters.Count != definitions.Count ||
            definitions.Any(definition => !component.Parameters.ContainsKey(definition.Key)))
        {
            throw new InvalidDataException($"Os parâmetros de {GetTypeLabel(component.Type)} não correspondem ao esquema suportado.");
        }

        foreach (var definition in definitions)
        {
            var value = component.Parameters[definition.Key];
            switch (definition.Kind)
            {
                case FurryParameterKind.Number:
                    if (value.ValueKind != JsonValueKind.Number ||
                        !value.TryGetDouble(out var number) ||
                        !double.IsFinite(number) ||
                        number < definition.Minimum ||
                        number > definition.Maximum)
                    {
                        throw new InvalidDataException(
                            $"O parâmetro “{definition.Label}” deve estar entre {definition.Minimum} e {definition.Maximum}.");
                    }

                    break;
                case FurryParameterKind.Boolean:
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        throw new InvalidDataException($"O parâmetro “{definition.Label}” deve ser verdadeiro ou falso.");
                    }

                    break;
                default:
                    throw new InvalidDataException("Tipo de parâmetro furry inválido.");
            }
        }
    }

    private static FurryParameterDefinition Number(
        string key,
        string label,
        double minimum,
        double maximum,
        double defaultValue) =>
        new(key, label, FurryParameterKind.Number, minimum, maximum, defaultValue, null);

    private static FurryParameterDefinition Boolean(string key, string label, bool defaultValue) =>
        new(key, label, FurryParameterKind.Boolean, null, null, null, defaultValue);
}
