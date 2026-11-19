using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace MareyaUSBFix.Core
{
    /// <summary>
    /// « Vaccine » une clé : un dossier autorun.inf protégé empêche un virus d'y écrire
    /// son propre fichier autorun.inf. Le sous-dossier au nom réservé (LPT1) rend ce dossier
    /// impossible à supprimer depuis l'Explorateur, donc aussi par la plupart des virus.
    /// </summary>
    public static class Vaccine
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveDirectoryW(string path);

        public static bool IsVaccinated(string root) =>
            Directory.Exists(Path.Combine(root, DriveScanner.VaccineFolderName));

        public static void Apply(string root)
        {
            if (DriveScanner.IsSystemDrive(root))
                throw new InvalidOperationException("Le disque système ne peut pas être vacciné.");

            string folder = Path.Combine(root, DriveScanner.VaccineFolderName);
            if (File.Exists(folder))
                FileOps.DeleteFile(folder); // un autorun.inf de virus prend la place

            Directory.CreateDirectory(folder);
            string reserved = @"\\?\" + Path.Combine(Path.GetFullPath(folder), "LPT1");
            if (!Directory.Exists(reserved) && !CreateDirectoryW(reserved, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 183) // ERROR_ALREADY_EXISTS
                    throw new Win32Exception(err);
            }
            File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
        }

        public static void Remove(string root)
        {
            string folder = Path.Combine(root, DriveScanner.VaccineFolderName);
            if (!Directory.Exists(folder)) return;
            File.SetAttributes(folder, FileAttributes.Directory);
            RemoveDirectoryW(@"\\?\" + Path.Combine(Path.GetFullPath(folder), "LPT1"));
            Directory.Delete(folder, true);
        }
    }
}
