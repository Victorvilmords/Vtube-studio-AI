namespace VTuberStudioAI.App.Tracking;

/// <summary>
/// Converte pontos de malha facial (478 × x,y,z em pixels, z positivo = longe da câmera) em medidas de pose,
/// boca e olhos. Matemática pura, sem dependência de câmera ou modelo.
/// </summary>
public static class FaceLandmarkMath
{
    public const int MinimumLandmarks = 468;

    // Índices da malha MediaPipe. "Esquerda/direita" = lado da imagem (o ponto 33 fica à esquerda na imagem crua).
    private const int CheekImageLeft = 234;
    private const int CheekImageRight = 454;
    private const int EyeOuterImageLeft = 33;
    private const int EyeInnerImageLeft = 133;
    private const int EyeInnerImageRight = 362;
    private const int EyeOuterImageRight = 263;
    private const int Forehead = 10;
    private const int Chin = 152;
    private const int UpperInnerLip = 13;
    private const int LowerInnerLip = 14;
    private const int IrisImageLeft = 468;
    private const int IrisImageRight = 473;
    private const int IrisLandmarks = 478;

    private static readonly (int Upper, int Lower)[] LeftEyeLids = [(159, 145), (160, 144), (158, 153)];
    private static readonly (int Upper, int Lower)[] RightEyeLids = [(386, 374), (385, 380), (387, 373)];

    private static readonly int[] BrowsImageLeft = [105, 66, 107];
    private static readonly int[] BrowsImageRight = [334, 296, 336];

    private readonly record struct V3(double X, double Y, double Z);

    /// <summary>Retorna nulo se faltarem pontos ou houver valores não finitos/rosto degenerado.</summary>
    public static FaceMeasurements? Measure(float[] xyz)
    {
        ArgumentNullException.ThrowIfNull(xyz);
        if (xyz.Length < MinimumLandmarks * 3)
        {
            return null;
        }

        V3 P(int index) => new(xyz[index * 3], xyz[index * 3 + 1], xyz[index * 3 + 2]);

        var across = Normalize(Add(Sub(P(CheekImageRight), P(CheekImageLeft)), Sub(P(EyeOuterImageRight), P(EyeOuterImageLeft))));
        var down = Sub(P(Chin), P(Forehead));
        var faceHeight = Length(down);
        if (across is null || faceHeight < 1e-3)
        {
            return null;
        }

        var r = across.Value;
        var u = Normalize(Sub(down, Scale(r, Dot(down, r))));
        if (u is null)
        {
            return null;
        }

        var yaw = Math.Atan2(r.Z, r.X) * 180 / Math.PI;
        var pitch = Math.Atan2(u.Value.Z, u.Value.Y) * 180 / Math.PI;
        var roll = Math.Atan2(r.Y, r.X) * 180 / Math.PI;
        var mouth = Length(Sub(P(LowerInnerLip), P(UpperInnerLip))) / faceHeight;
        var eyeLeft = EyeRatio(P, LeftEyeLids, EyeOuterImageLeft, EyeInnerImageLeft);
        var eyeRight = EyeRatio(P, RightEyeLids, EyeInnerImageRight, EyeOuterImageRight);
        var (irisX, irisY, hasIris) = xyz.Length >= IrisLandmarks * 3
            ? Iris(P, r, u.Value)
            : (0.0, 0.0, false);
        var browLeft = BrowHeight(P, BrowsImageLeft, EyeOuterImageLeft, EyeInnerImageLeft, u.Value, faceHeight);
        var browRight = BrowHeight(P, BrowsImageRight, EyeInnerImageRight, EyeOuterImageRight, u.Value, faceHeight);
        var result = new FaceMeasurements(yaw, pitch, roll, mouth, eyeLeft, eyeRight, irisX, irisY, hasIris, browLeft, browRight);
        return IsFinite(result) ? result : null;
    }

    /// <summary>
    /// Posição da íris dentro do olho, no eixo do rosto (não muda ao girar a cabeça), em fração da largura do olho.
    /// X positivo = lado direito da imagem; Y positivo = para baixo. Média dos dois olhos.
    /// </summary>
    private static (double X, double Y, bool Valid) Iris(Func<int, V3> point, V3 right, V3 down)
    {
        (double X, double Y)? Eye(int iris, int cornerA, int cornerB, int upper, int lower)
        {
            var width = Length(Sub(point(cornerA), point(cornerB)));
            if (width < 1e-3)
            {
                return null;
            }

            var center = Scale(Add(point(cornerA), point(cornerB)), 0.5);
            var lidMid = Scale(Add(point(upper), point(lower)), 0.5);
            var offset = Sub(point(iris), center);
            var vertical = Sub(point(iris), lidMid);
            return (Dot(offset, right) / width, Dot(vertical, down) / width);
        }

        var a = Eye(IrisImageLeft, EyeOuterImageLeft, EyeInnerImageLeft, 159, 145);
        var b = Eye(IrisImageRight, EyeInnerImageRight, EyeOuterImageRight, 386, 374);
        return a is { } first && b is { } second
            ? ((first.X + second.X) / 2, (first.Y + second.Y) / 2, true)
            : (0, 0, false);
    }

    /// <summary>
    /// Altura da sobrancelha acima do centro do olho, no eixo do rosto, em fração da altura do rosto. Usa os cantos
    /// do olho (e não a pálpebra), então piscar não altera o valor.
    /// </summary>
    private static double BrowHeight(
        Func<int, V3> point, int[] brow, int cornerA, int cornerB, V3 down, double faceHeight)
    {
        var center = Scale(Add(point(cornerA), point(cornerB)), 0.5);
        var average = Scale(brow.Select(point).Aggregate(new V3(0, 0, 0), Add), 1.0 / brow.Length);
        return -Dot(Sub(average, center), down) / faceHeight;
    }

    private static double EyeRatio(Func<int, V3> point, (int Upper, int Lower)[] lids, int cornerA, int cornerB)
    {
        var width = Length(Sub(point(cornerA), point(cornerB)));
        if (width < 1e-3)
        {
            return 0;
        }

        var gap = lids.Average(pair => Length(Sub(point(pair.Upper), point(pair.Lower))));
        return gap / width;
    }

    private static bool IsFinite(FaceMeasurements m) =>
        double.IsFinite(m.YawDegrees) && double.IsFinite(m.PitchDegrees) && double.IsFinite(m.RollDegrees) &&
        double.IsFinite(m.MouthRatio) && double.IsFinite(m.EyeImageLeftRatio) && double.IsFinite(m.EyeImageRightRatio) &&
        double.IsFinite(m.IrisX) && double.IsFinite(m.IrisY) &&
        double.IsFinite(m.BrowImageLeft) && double.IsFinite(m.BrowImageRight);

    private static V3 Sub(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static V3 Add(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static V3 Scale(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    private static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static double Length(V3 a) => Math.Sqrt(Dot(a, a));

    private static V3? Normalize(V3 a)
    {
        var length = Length(a);
        return length < 1e-6 || !double.IsFinite(length) ? null : Scale(a, 1 / length);
    }
}
