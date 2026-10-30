using System.Windows.Markup;
using System.Windows.Media;

namespace VTuberStudioAI.App.Themes;

/// <summary>
/// Entrega a cor de uma chave do tema salvo ao carregar o XAML. Como estilos e templates guardam
/// os pincéis no momento da leitura, o tema escolhido vale a partir da próxima abertura do app.
/// </summary>
[MarkupExtensionReturnType(typeof(Color))]
public sealed class ThemeColorExtension(string key) : MarkupExtension
{
    private static readonly Lazy<AppTheme> Startup = new(() =>
        AppThemes.Find(new AppSettingsStore().ReadThemeId()) ?? AppThemes.Default);

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (!Startup.Value.Colors.TryGetValue(key, out var hex) || !AppThemes.IsHexColor(hex))
        {
            throw new InvalidOperationException($"Chave de tema desconhecida: {key}.");
        }

        return (Color)ColorConverter.ConvertFromString(hex);
    }
}
