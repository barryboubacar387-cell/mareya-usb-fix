using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MareyaUSBFix.Core
{
    /// <summary>
    /// Reprend la propriété d'une arborescence protégée (ex. dossier de SYSTEM récupéré par chkdsk),
    /// comme « takeown /r » : on active les privilèges d'administrateur qui l'autorisent, puis on
    /// descend dossier par dossier en donnant le contrôle total aux Administrateurs.
    /// </summary>
    public static class Ownership
    {
        private static readonly SecurityIdentifier Admins =
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        public static void TakeOwnershipRecursive(string path, Action<string> log = null)
        {
            EnablePrivilege("SeTakeOwnershipPrivilege");
            EnablePrivilege("SeRestorePrivilege");
            EnablePrivilege("SeBackupPrivilege");
            Take(path, log);
        }

        private static void Take(string path, Action<string> log)
        {
            bool isDir = Directory.Exists(path);
            try
            {
                if (isDir)
                {
                    var di = new DirectoryInfo(path);
                    var owner = new DirectorySecurity();
                    owner.SetOwner(Admins);
                    di.SetAccessControl(owner);
                    var acl = di.GetAccessControl(AccessControlSections.Access);
                    acl.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    di.SetAccessControl(acl);
                    di.Attributes = FileAttributes.Directory;
                }
                else
                {
                    var fi = new FileInfo(path);
                    var owner = new FileSecurity();
                    owner.SetOwner(Admins);
                    fi.SetAccessControl(owner);
                    var acl = fi.GetAccessControl(AccessControlSections.Access);
                    acl.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, AccessControlType.Allow));
                    fi.SetAccessControl(acl);
                    fi.Attributes = FileAttributes.Normal;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Reprise de propriété impossible pour {path} ({ex.Message})");
            }

            if (!isDir) return;
            string[] children;
            try { children = Directory.GetFileSystemEntries(path); }
            catch (Exception ex)
            {
                log?.Invoke($"Contenu illisible : {path} ({ex.Message})");
                return;
            }
            foreach (string child in children) Take(child, log);
        }

        // ---------- Privilèges du jeton ----------

        private const int SE_PRIVILEGE_ENABLED = 0x2;
        private const int TOKEN_ADJUST_PRIVILEGES = 0x20;
        private const int TOKEN_QUERY = 0x8;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public int Count; public LUID Luid; public int Attributes; }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string system, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state,
            int length, IntPtr previous, IntPtr returnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private static void EnablePrivilege(string name)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!LookupPrivilegeValue(null, name, out LUID luid))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                CloseHandle(token);
            }
        }
    }
}
