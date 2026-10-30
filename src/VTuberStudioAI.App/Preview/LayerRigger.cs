using VTuberStudioAI.App.Images;

namespace VTuberStudioAI.App.Preview;

public enum LayerMotion
{
    Static,
    FollowsHead,
    Ear,
    EyeBase,
    EyeContent,
    UpperLash,
    LowerLash,
    MouthOpen,
    Eyebrow
}

/// <summary>Plano de movimento de uma camada; <paramref name="MaskIndex"/> aponta para o eyewhite que recorta íris/pupila/brilho.</summary>
public sealed record LayerRigEntry(int Index, LayerRole Role, LayerMotion Motion, int? MaskIndex);

/// <summary>Monta o rig automático por camada a partir da nomenclatura. Não deforma malhas nem usa IA.</summary>
public static class LayerRigger
{
    public static IReadOnlyList<LayerRigEntry> Plan(IReadOnlyList<PsdLayerInfo> layers)
    {
        var roles = layers.Select(layer => LayerNaming.Parse(layer.Name)).ToList();
        var entries = new List<LayerRigEntry>(layers.Count);
        for (var index = 0; index < layers.Count; index++)
        {
            var role = roles[index];
            var motion = role.Part switch
            {
                LayerPart.EyeWhite => LayerMotion.EyeBase,
                LayerPart.Iris or LayerPart.Pupil or LayerPart.EyeHighlight => LayerMotion.EyeContent,
                LayerPart.EyelashUpper => LayerMotion.UpperLash,
                LayerPart.EyelashLower => LayerMotion.LowerLash,
                LayerPart.Ear => LayerMotion.Ear,
                LayerPart.MouthOpen => LayerMotion.MouthOpen,
                LayerPart.Eyebrow => LayerMotion.Eyebrow,
                LayerPart.Body or LayerPart.Neck or LayerPart.Background or LayerPart.Tail => LayerMotion.Static,
                _ => LayerMotion.FollowsHead
            };

            var needsEye = motion is LayerMotion.EyeContent or LayerMotion.UpperLash or LayerMotion.LowerLash;
            int? mask = needsEye ? FindMask(layers, roles, index, allowNearest: motion != LayerMotion.EyeContent) : null;
            entries.Add(new LayerRigEntry(index, role, motion, mask));
        }

        return entries;
    }

    public static string Summarize(IReadOnlyList<LayerRigEntry> plan)
    {
        var recognized = plan.Count(entry => entry.Role.IsRecognized);
        if (recognized == 0)
        {
            return "Nenhuma camada segue o molde de nomes ainda; o preview usa a imagem composta.";
        }

        var eyes = plan.Count(entry => entry.Motion == LayerMotion.EyeBase);
        var masked = plan.Count(entry => entry.MaskIndex is not null);
        var ears = plan.Count(entry => entry.Motion == LayerMotion.Ear);
        var brows = plan.Count(entry => entry.Motion == LayerMotion.Eyebrow);
        var lashes = plan.Count(entry => entry.Motion is LayerMotion.UpperLash or LayerMotion.LowerLash && entry.MaskIndex is not null);
        var furry = plan.Any(entry => entry.Role.IsFurry);
        var text = $"{recognized} de {plan.Count} camadas reconhecidas · {eyes} olho(s) com máscara em {masked} camada(s) interna(s) · {lashes} cílio(s) ligado(s) ao piscar · {ears} orelha(s) · {brows} sobrancelha(s)";
        return furry ? text + " · prefixo F: personagem furry" : text;
    }

    private static int? FindMask(
        IReadOnlyList<PsdLayerInfo> layers, IReadOnlyList<LayerRole> roles, int contentIndex, bool allowNearest)
    {
        var content = layers[contentIndex];
        var side = roles[contentIndex].Side;
        int? best = null;
        long bestArea = 0;
        int? nearest = null;
        double nearestDistance = double.MaxValue;
        for (var index = 0; index < layers.Count; index++)
        {
            if (roles[index].Part != LayerPart.EyeWhite || !layers[index].Visible)
            {
                continue;
            }

            var sideMismatch = side != LayerSide.None && roles[index].Side != LayerSide.None && roles[index].Side != side;
            if (sideMismatch)
            {
                continue;
            }

            var area = OverlapArea(content, layers[index]);
            if (area > bestArea)
            {
                best = index;
                bestArea = area;
            }

            // Cílios podem ficar acima do olho sem sobrepor; nesses casos vale o olho do mesmo lado mais próximo.
            var sameSide = side != LayerSide.None && roles[index].Side == side;
            var distance = Math.Abs(content.X + content.Width / 2.0 - (layers[index].X + layers[index].Width / 2.0)) +
                           Math.Abs(content.Y + content.Height / 2.0 - (layers[index].Y + layers[index].Height / 2.0));
            if (sameSide && distance < nearestDistance)
            {
                nearest = index;
                nearestDistance = distance;
            }
        }

        return best ?? (allowNearest ? nearest : null);
    }

    private static long OverlapArea(PsdLayerInfo a, PsdLayerInfo b)
    {
        var width = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        var height = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? (long)width * height : 0;
    }
}
