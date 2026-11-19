using System;
using System.IO;
using System.Reflection;

namespace MareyaUSBFix.Core
{
    /// <summary>
    /// Protection automatique : le logiciel se copie dans Program Files et démarre avec Windows
    /// (tâche planifiée avec droits élevés, sans demande de confirmation à chaque démarrage).
    /// Il surveille alors chaque clé branchée.
    /// </summary>
    public static class AutoProtection
    {
        public const string TrayArgument = "/tray";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mareya USB Fix");

        public static string InstalledExe => Path.Combine(InstallDir, "MareyaUSBFix.exe");

        private static string TaskFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks", SystemCleaner.OwnTaskName);

        public static bool IsEnabled => File.Exists(TaskFile);

        public static void Enable()
        {
            string current = Assembly.GetExecutingAssembly().Location;
            Directory.CreateDirectory(InstallDir);
            if (!string.Equals(Path.GetFullPath(current), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(current, InstalledExe, overwrite: true);

            int code = SystemCleaner.RunQuiet("schtasks.exe",
                $"/Create /TN \"{SystemCleaner.OwnTaskName}\" /TR \"\\\"{InstalledExe}\\\" {TrayArgument}\" /SC ONLOGON /RL HIGHEST /F");
            if (code != 0)
                throw new InvalidOperationException($"Impossible de créer le lancement automatique (code {code}).");
        }

        public static void Disable()
        {
            SystemCleaner.RunQuiet("schtasks.exe", $"/Delete /TN \"{SystemCleaner.OwnTaskName}\" /F");
        }
    }
}
