using System.IO;
using System.Reflection;

namespace VTuberStudioAI.App;

internal static class AppInfo
{
    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0")
            .Split('+')[0];

    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTuberStudioAI");

    public static string LogsFolder { get; } = Path.Combine(DataFolder, "logs");
}
