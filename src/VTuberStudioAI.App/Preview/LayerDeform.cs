namespace VTuberStudioAI.App.Preview;

/// <summary>Ajustes de calibração da deformação do preview; cada intensidade vai de 0 (nenhuma) a 2.</summary>
public sealed record RigSettings(
    bool Enabled, double Overall, double Face, double Ears, double Hair, double Head, double EyeLook,
    double Body = 1, bool CloseMouth = true, Dictionary<string, LayerModifier>? LayerModifiers = null)
{
    public const double MaxIntensity = 2;

    public static RigSettings Default { get; } = new(true, 1, 1, 1, 1, 1, 1);

    public bool IsValid => new[] { Overall, Face, Ears, Hair, Head, EyeLook, Body }
        .All(value => double.IsFinite(value) && value >= 0 && value <= MaxIntensity) &&
        (LayerModifiers is null || LayerModifiers.Values.All(modifier => modifier is not null && modifier.IsValid));

    public LayerModifier ModifierFor(string layerName) =>
        LayerModifiers is not null && LayerModifiers.TryGetValue(layerName, out var modifier)
            ? modifier
            : LayerModifier.Default;
}

/// <summary>Ajustes simples por camada: escala, deslocamento (px do documento) e intensidade da deformação (0 a 2).</summary>
public sealed record LayerModifier(double Scale = 1, double OffsetX = 0, double OffsetY = 0, double Intensity = 1)
{
    public const double MinScale = 0.2;
    public const double MaxScale = 3;
    public const double MaxOffset = 300;

    public static LayerModifier Default { get; } = new();

    public bool IsDefault => this == Default;

    public bool IsValid =>
        double.IsFinite(Scale) && Scale >= MinScale && Scale <= MaxScale &&
        double.IsFinite(OffsetX) && Math.Abs(OffsetX) <= MaxOffset &&
        double.IsFinite(OffsetY) && Math.Abs(OffsetY) <= MaxOffset &&
        double.IsFinite(Intensity) && Intensity >= 0 && Intensity <= RigSettings.MaxIntensity;
}

public readonly record struct LayerPose(double Dx, double Dy, double ScaleX, double ScaleY, double PivotX, double PivotY)
{
    public static LayerPose Identity { get; } = new(0, 0, 1, 1, 0, 0);

    public bool IsIdentity => Dx == 0 && Dy == 0 && ScaleX == 1 && ScaleY == 1;
}

/// <summary>
/// Pseudo-3D por camada: cada parte tem uma profundidade fictícia e gira em torno de um eixo vertical/horizontal
/// da cabeça. O resultado é deslocamento e achatamento 2D; não há malha, 3D real nem deformação de pixels.
/// </summary>
public static class LayerDeform
{
    public const double MaxYawDegrees = 28;
    public const double MaxPitchDegrees = 16;
    public const double MaxAngleDegrees = 45;
    public const double FurryMuzzleBoost = 1.3;

    private enum Group { None, Face, Head, Ears, Hair }

    private readonly record struct Profile(Group Group, double Z, double Facing, double Spread);

    private static Profile ProfileFor(LayerPart part) => part switch
    {
        LayerPart.Nose => new(Group.Face, 0.42, 1, 0.10),
        LayerPart.Snout => new(Group.Face, 0.50, 1, 0.12),
        LayerPart.Mouth or LayerPart.MouthOpen => new(Group.Face, 0.38, 1, 0.10),
        LayerPart.EyeWhite or LayerPart.Iris or LayerPart.Pupil or LayerPart.EyeHighlight or
            LayerPart.EyelashUpper or LayerPart.EyelashLower or LayerPart.Eyelid => new(Group.Face, 0.30, 1, 0.15),
        LayerPart.Eyebrow => new(Group.Face, 0.30, 1, 0.12),
        LayerPart.Head => new(Group.Head, 0.10, 0.5, 0.15),
        LayerPart.Ear or LayerPart.Horn => new(Group.Ears, 0.0, 0, 0.7),
        LayerPart.HairFront => new(Group.Hair, 0.36, 0.5, 0.2),
        LayerPart.HairSide => new(Group.Hair, 0.08, 0, 0.5),
        LayerPart.HairBack => new(Group.Hair, -0.30, 0.3, 0.1),
        LayerPart.Hair => new(Group.Hair, 0.10, 0.5, 0.15),
        _ => new(Group.None, 0, 0, 0)
    };

    /// <summary>Indica se a camada recebe deformação pseudo-3D ao virar a cabeça.</summary>
    public static bool IsDeformable(LayerRigEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Motion != LayerMotion.Static && ProfileFor(entry.Role.Part).Group != Group.None;
    }

    /// <summary>Pose da camada <paramref name="index"/> para o olhar (-1 a 1) dado; coordenadas do documento.</summary>
    public static LayerPose Pose(LayerScene scene, int index, double yaw, double pitch, RigSettings rig)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(rig);
        var layer = scene.Layers[index];
        var modifier = rig.Enabled ? rig.ModifierFor(layer.Info.Name) : LayerModifier.Default;
        var pose = BasePose(scene, index, yaw, pitch, rig, modifier.Intensity);
        if (modifier.IsDefault)
        {
            return pose;
        }

        var pivotX = pose.PivotX;
        var pivotY = pose.PivotY;
        if (pose.PivotX == 0 && pose.PivotY == 0)
        {
            pivotX = layer.Info.X + layer.Info.Width / 2.0;
            pivotY = layer.Info.Y + layer.Info.Height / 2.0;
        }

        return new LayerPose(
            pose.Dx + modifier.OffsetX, pose.Dy + modifier.OffsetY,
            pose.ScaleX * modifier.Scale, pose.ScaleY * modifier.Scale, pivotX, pivotY);
    }

    private static LayerPose BasePose(LayerScene scene, int index, double yaw, double pitch, RigSettings rig, double intensity)
    {
        var layer = scene.Layers[index];
        if (!rig.Enabled || layer.Rig.Motion == LayerMotion.Static || !double.IsFinite(yaw) || !double.IsFinite(pitch))
        {
            return LayerPose.Identity;
        }

        var profile = ProfileFor(layer.Rig.Role.Part);
        if (profile.Group == Group.None)
        {
            return LayerPose.Identity;
        }

        // Itens dentro do olho e cílios usam o eyewhite como âncora, para se moverem exatamente como ele.
        var attached = layer.Rig.Motion is LayerMotion.EyeContent or LayerMotion.UpperLash or LayerMotion.LowerLash;
        var anchor = attached && layer.Rig.MaskIndex is { } maskIndex ? scene.Layers[maskIndex].Info : layer.Info;
        var strength = rig.Overall * intensity * profile.Group switch
        {
            Group.Face => rig.Face,
            Group.Head => rig.Head,
            Group.Ears => rig.Ears,
            _ => rig.Hair
        };

        var theta = Math.Clamp(yaw * MaxYawDegrees * strength, -MaxAngleDegrees, MaxAngleDegrees) * Math.PI / 180;
        var phi = Math.Clamp(pitch * MaxPitchDegrees * strength, -MaxAngleDegrees, MaxAngleDegrees) * Math.PI / 180;
        var (headX, headY, headWidth) = scene.Head;
        var depth = profile.Z;
        if (layer.Rig.Role.IsFurry && layer.Rig.Role.Part is LayerPart.Nose or LayerPart.Snout or LayerPart.Mouth or LayerPart.MouthOpen or LayerPart.Head)
        {
            depth *= FurryMuzzleBoost;
        }

        var anchorX = anchor.X + anchor.Width / 2.0;
        var anchorY = anchor.Y + anchor.Height / 2.0;
        var u = anchorX - headX;
        var v = anchorY - headY;
        var rel = Math.Clamp(u / (headWidth / 2), -1.2, 1.2);

        var dx = u * (Math.Cos(theta) - 1) + depth * headWidth * Math.Sin(theta);
        var dy = v * (Math.Cos(phi) - 1) + depth * headWidth * Math.Sin(phi) * 0.8;
        var scaleX = Math.Pow(Math.Cos(theta), profile.Facing) * (1 - profile.Spread * Math.Sin(theta) * rel);
        var scaleY = Math.Pow(Math.Cos(phi), profile.Facing);
        return new LayerPose(dx, dy, Math.Clamp(scaleX, 0.35, 1.6), Math.Clamp(scaleY, 0.35, 1.2), anchorX, anchorY);
    }
}
