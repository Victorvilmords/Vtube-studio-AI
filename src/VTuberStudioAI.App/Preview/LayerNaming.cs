using System.Globalization;
using System.Text;

namespace VTuberStudioAI.App.Preview;

public enum LayerSide { None, Left, Right }

public enum LayerPart
{
    Unknown,
    EyeWhite,
    Iris,
    Pupil,
    EyeHighlight,
    Eyelid,
    EyelashUpper,
    EyelashLower,
    Eyebrow,
    Head,
    Neck,
    Ear,
    Horn,
    Snout,
    Nose,
    Hair,
    HairBack,
    HairSide,
    HairFront,
    Mouth,
    Body,
    Background,
    Tail,
    MouthOpen
}

/// <summary>Papel de uma camada deduzido somente do nome (ex.: <c>F.ear.L</c>).</summary>
public sealed record LayerRole(LayerPart Part, LayerSide Side, bool IsFurry)
{
    public bool IsRecognized => Part != LayerPart.Unknown;
}

/// <summary>
/// Molde de nomenclatura: <c>[F.]parte[.L|.R]</c>. Separadores aceitos: ponto, sublinhado, hífen e espaço.
/// O prefixo F marca partes de personagem furry; L/R (ou left/right, esquerda/direita) define o lado.
/// </summary>
public static class LayerNaming
{
    public const int MaxNameLength = 64;

    private static readonly char[] Separators = ['.', '_', '-', ' '];

    private static readonly Dictionary<string, LayerPart> Aliases = BuildAliases();

    private static readonly Dictionary<LayerPart, string> Labels = new()
    {
        [LayerPart.EyeWhite] = "Olho (fundo/máscara)",
        [LayerPart.Iris] = "Íris",
        [LayerPart.Pupil] = "Pupila",
        [LayerPart.EyeHighlight] = "Brilho do olho",
        [LayerPart.Eyelid] = "Pálpebra",
        [LayerPart.EyelashUpper] = "Cílios superiores",
        [LayerPart.EyelashLower] = "Cílios inferiores",
        [LayerPart.Eyebrow] = "Sobrancelha",
        [LayerPart.Head] = "Cabeça",
        [LayerPart.Neck] = "Pescoço",
        [LayerPart.Ear] = "Orelha",
        [LayerPart.Horn] = "Chifre",
        [LayerPart.Snout] = "Focinho",
        [LayerPart.Nose] = "Nariz",
        [LayerPart.Hair] = "Cabelo",
        [LayerPart.HairBack] = "Cabelo de trás",
        [LayerPart.HairSide] = "Cabelo lateral",
        [LayerPart.HairFront] = "Franja",
        [LayerPart.Mouth] = "Boca",
        [LayerPart.Body] = "Corpo",
        [LayerPart.Background] = "Fundo",
        [LayerPart.Tail] = "Cauda",
        [LayerPart.MouthOpen] = "Boca aberta (aparece ao abrir a boca na câmera)"
    };

    public static LayerRole Parse(string? name)
    {
        var unknown = new LayerRole(LayerPart.Unknown, LayerSide.None, false);
        if (string.IsNullOrWhiteSpace(name))
        {
            return unknown;
        }

        var tokens = RemoveDiacritics(name)
            .ToLowerInvariant()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Sufixos de duplicação ("copy", "001") não fazem parte do papel.
        while (tokens.Count > 1 && (tokens[^1] is "copy" or "copia" || tokens[^1].All(char.IsAsciiDigit)))
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        var furry = false;
        if (tokens.Count > 1 && tokens[0] == "f")
        {
            furry = true;
            tokens.RemoveAt(0);
        }

        var side = LayerSide.None;
        if (tokens.Count > 1)
        {
            side = tokens[^1] switch
            {
                "l" or "left" or "esq" or "esquerdo" or "esquerda" => LayerSide.Left,
                "r" or "right" or "dir" or "direito" or "direita" => LayerSide.Right,
                _ => LayerSide.None
            };
            if (side != LayerSide.None)
            {
                tokens.RemoveAt(tokens.Count - 1);
            }
        }

        return Aliases.TryGetValue(string.Concat(tokens), out var part)
            ? new LayerRole(part, side, furry)
            : unknown;
    }

    public static string Describe(LayerRole role)
    {
        if (!role.IsRecognized)
        {
            return "Sem papel reconhecido (acompanha a cabeça)";
        }

        var text = new StringBuilder(Labels[role.Part]);
        if (role.Side != LayerSide.None)
        {
            text.Append(role.Side == LayerSide.Left ? " esquerda(o)" : " direita(o)");
        }

        if (role.IsFurry)
        {
            text.Append(" · furry");
        }

        return text.ToString();
    }

    /// <summary>Retorna uma mensagem de erro ou nulo quando o nome de camada é aceitável.</summary>
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "O nome da camada não pode ficar vazio.";
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return $"O nome da camada pode ter no máximo {MaxNameLength} caracteres.";
        }

        return name.Any(char.IsControl) ? "O nome da camada não pode ter caracteres de controle." : null;
    }

    private static Dictionary<string, LayerPart> BuildAliases()
    {
        var map = new Dictionary<string, LayerPart>(StringComparer.Ordinal);
        void Add(LayerPart part, params string[] names)
        {
            foreach (var name in names)
            {
                map[name] = part;
            }
        }

        Add(LayerPart.EyeWhite, "eyewhite", "sclera", "esclera", "eyebase", "eye", "olho", "olhobranco");
        Add(LayerPart.Iris, "iris");
        Add(LayerPart.Pupil, "pupil", "pupila");
        Add(LayerPart.EyeHighlight, "eyehighlight", "highlight", "eyelight", "catchlight", "brilho", "brilhodoolho");
        Add(LayerPart.Eyelid, "eyelid", "eyelids", "lid", "palpebra");
        Add(LayerPart.EyelashUpper, "eyelash", "eyelashes", "lash", "lashes", "cilio", "cilios",
            "eyelashupper", "eyelashup", "eyelashtop", "uppereyelash", "upperlash", "upperlashes", "lashupper",
            "ciliosuperior", "ciliossuperiores", "cilioupper");
        Add(LayerPart.EyelashLower, "eyelashlower", "eyelashlow", "eyelashbottom", "lowereyelash", "lowerlash",
            "lowerlashes", "lashlower", "cilioinferior", "ciliosinferiores", "ciliolower");
        Add(LayerPart.Eyebrow, "eyebrow", "eyebrows", "brow", "brows", "sobrancelha");
        Add(LayerPart.Head, "head", "face", "cabeca", "rosto");
        Add(LayerPart.Neck, "neck", "pescoco");
        Add(LayerPart.Ear, "ear", "ears", "orelha");
        Add(LayerPart.Horn, "horn", "horns", "chifre");
        Add(LayerPart.Snout, "snout", "muzzle", "focinho");
        Add(LayerPart.Nose, "nose", "nariz");
        Add(LayerPart.Hair, "hair", "cabelo");
        Add(LayerPart.HairBack, "hairback", "backhair", "cabelotras", "cabelodetras", "hairbehind");
        Add(LayerPart.HairSide, "hairside", "sidehair", "cabelolateral", "hairsides");
        Add(LayerPart.HairFront, "hairfront", "fronthair", "hairbangs", "bangs", "bang", "franja", "cabelofrente");
        Add(LayerPart.Mouth, "mouth", "boca");
        Add(LayerPart.MouthOpen, "mouthopen", "openmouth", "mouthopened", "mouthopening", "bocaaberta", "abrirboca");
        Add(LayerPart.Body, "body", "torso", "corpo", "arm", "braco", "leg", "perna", "clothes", "roupa");
        Add(LayerPart.Background, "background", "fundo", "planodefundo", "backdrop", "bg");
        Add(LayerPart.Tail, "tail", "cauda");
        return map;
    }

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
