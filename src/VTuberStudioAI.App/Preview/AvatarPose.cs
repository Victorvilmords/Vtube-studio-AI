namespace VTuberStudioAI.App.Preview;

/// <summary>
/// Pose do avatar vinda do rastreamento. Olhar/inclinação em -1..1 e roll em graus; "esquerda/direita" são lados
/// da TELA do avatar. Fechamento e boca vão de 0 (aberto/fechada) a 1 (fechado/aberta). Sobrancelhas vão de -1
/// (abaixada) a 1 (levantada).
/// </summary>
public sealed record AvatarPose(
    double GazeX,
    double GazeY,
    double RollDegrees,
    double MouthOpen,
    double ScreenLeftEyeClosed,
    double ScreenRightEyeClosed,
    double IrisX = 0,
    double IrisY = 0,
    double ScreenLeftBrow = 0,
    double ScreenRightBrow = 0)
{
    public static AvatarPose Neutral { get; } = new(0, 0, 0, 0, 0, 0);
}
