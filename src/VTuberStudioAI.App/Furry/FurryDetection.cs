using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;

namespace VTuberStudioAI.App.Furry;

public enum CharacterKind
{
    NoLayers,
    Unrecognized,
    Human,
    Furry
}

public sealed record DetectedPart(
    string LayerName, string Role, string Behavior, bool IsFurryPart, bool IsDeformable, bool IsHidden);

public sealed record FurryDetectionResult(CharacterKind Kind, int FurryPartCount, IReadOnlyList<DetectedPart> Parts)
{
    public string Summary => Kind switch
    {
        CharacterKind.NoLayers => "Nenhuma camada encontrada. Importe um PSD separado por camadas para detectar as partes do personagem.",
        CharacterKind.Unrecognized => "Nenhuma camada segue o molde de nomes. Renomeie as camadas (ex.: F.ear.L, eyewhite.R) para o app reconhecer as partes.",
        CharacterKind.Furry => $"Personagem furry detectado · {FurryPartCount} camada(s) com prefixo F/focinho · {Parts.Count} parte(s) reconhecida(s), {Parts.Count(part => part.IsDeformable)} deformável(is).",
        _ => $"Personagem humano detectado (sem prefixo F nem focinho) · {Parts.Count} parte(s) reconhecida(s), {Parts.Count(part => part.IsDeformable)} deformável(is)."
    };
}

/// <summary>Deduz, só pelos nomes das camadas, se o personagem é furry e quais partes o rig automático controla.</summary>
public static class FurryDetection
{
    public static FurryDetectionResult Analyze(IReadOnlyList<PsdLayerInfo> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            return new FurryDetectionResult(CharacterKind.NoLayers, 0, []);
        }

        var plan = LayerRigger.Plan(layers);
        var parts = new List<DetectedPart>();
        var furryCount = 0;
        for (var index = 0; index < layers.Count; index++)
        {
            var role = plan[index].Role;
            if (!role.IsRecognized)
            {
                continue;
            }

            var furryPart = role.IsFurry || role.Part == LayerPart.Snout;
            if (furryPart)
            {
                furryCount++;
            }

            parts.Add(new DetectedPart(
                layers[index].Name,
                LayerNaming.Describe(role),
                BehaviorOf(plan[index], role.IsFurry),
                role.IsFurry || role.Part is LayerPart.Snout or LayerPart.Nose or LayerPart.Ear or LayerPart.Horn or LayerPart.Tail,
                LayerDeform.IsDeformable(plan[index]),
                !layers[index].Visible));
        }

        var kind = parts.Count == 0 ? CharacterKind.Unrecognized : furryCount > 0 ? CharacterKind.Furry : CharacterKind.Human;
        return new FurryDetectionResult(kind, furryCount, parts);
    }

    private static string BehaviorOf(LayerRigEntry entry, bool furry) => entry.Motion switch
    {
        LayerMotion.EyeBase => "Pisca, segue o olhar e recorta íris/pupila/brilho",
        LayerMotion.EyeContent => entry.MaskIndex is null
            ? "Segue o olhar (sem olho .eyewhite para recortar)"
            : "Segue o olhar dentro do olho",
        LayerMotion.UpperLash or LayerMotion.LowerLash => "Acompanha o piscar",
        LayerMotion.Ear => "Deforma ao virar a cabeça e balança",
        LayerMotion.MouthOpen => "Aparece e se deforma ao abrir a boca",
        LayerMotion.Eyebrow => "Sobe e desce com a sobrancelha",
        LayerMotion.Static => entry.Role.Part is LayerPart.Body or LayerPart.Neck or LayerPart.Tail
            ? "Corpo: acompanha a cabeça de leve"
            : "Fixa",
        _ => entry.Role.Part switch
        {
            LayerPart.Mouth => furry ? "Deforma ao virar a cabeça; fecha em V invertido" : "Deforma ao virar a cabeça; fecha em C deitado",
            LayerPart.Snout or LayerPart.Nose or LayerPart.Head => "Deforma ao virar a cabeça (volume extra no furry)",
            _ => LayerDeform.IsDeformable(entry) ? "Deforma ao virar a cabeça" : "Acompanha a cabeça"
        }
    };
}
