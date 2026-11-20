using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MareyaUSBFix.Core
{
    public enum CapacityTestMode
    {
        /// <summary>Remplit tout l'espace libre puis relit tout : prouve la capacité réelle.</summary>
        Full,
        /// <summary>Écrit et relit 256 Mo : vitesse réelle et contrôle d'intégrité rapide.</summary>
        Speed
    }

    public sealed class CapacityProgress
    {
        public string Phase { get; set; }
        public long Done { get; set; }
        public long Total { get; set; }
        public double MBps { get; set; }
        public long BadBytes { get; set; }
    }

    public sealed class CapacityTestResult
    {
        public CapacityTestMode Mode { get; set; }
        public long BytesWritten { get; set; }
        public long BytesVerified { get; set; }
        public long BytesBad { get; set; }
        public long FirstBadOffset { get; set; } = -1;
        public long UsedBeforeTest { get; set; }
        public long DriveSize { get; set; }
        public double WriteMBps { get; set; }
        public double ReadMBps { get; set; }
        public bool Cancelled { get; set; }
        public bool Authentic => !Cancelled && BytesBad == 0 && BytesVerified > 0;

        public string Summary()
        {
            string speeds = $"Vitesse réelle : écriture {WriteMBps:0.0} Mo/s, lecture {ReadMBps:0.0} Mo/s.";
            if (Cancelled)
                return $"Test arrêté : {Gb(BytesVerified)} vérifiés, {Gb(BytesBad)} corrompus jusque-là. {speeds}";
            if (BytesBad > 0)
            {
                string estimate = FirstBadOffset >= 0
                    ? $" Capacité réellement utilisable estimée : environ {Gb(UsedBeforeTest + FirstBadOffset)} sur {Gb(DriveSize)} annoncés."
                    : "";
                return $"FAUX OU DÉFECTUEUX : {Gb(BytesBad)} de données ont été perdues ou corrompues sur {Gb(BytesVerified)} écrits.{estimate} " +
                       $"N'y stockez rien d'important. {speeds}";
            }
            return Mode == CapacityTestMode.Full
                ? $"AUTHENTIQUE : les {Gb(BytesVerified)} d'espace libre ont été écrits et relus sans aucune erreur. {speeds}"
                : $"Aucune erreur sur {Gb(BytesVerified)} testés. {speeds} (Pour prouver la capacité réelle, lancez le test complet.)";
        }

        private static string Gb(long b) => b >= 1_000_000_000 ? $"{b / 1e9:0.00} Go" : $"{b / 1e6:0} Mo";
    }

    /// <summary>
    /// Test d'authenticité (principe de H2testw) : remplit l'espace LIBRE de fichiers dont chaque octet
    /// est calculable, puis relit et compare. Une fausse clé (capacité trafiquée) perd ou réécrit les
    /// données au-delà de sa vraie capacité : le test le prouve. Les lectures/écritures contournent la
    /// mémoire cache de Windows (FILE_FLAG_NO_BUFFERING), sinon une fausse clé passerait le test.
    /// Les fichiers existants ne sont jamais touchés ; les fichiers de test sont toujours supprimés.
    /// </summary>
    public static class CapacityTest
    {
        public const string TestFolderName = "Mareya Test Capacite";
        private const int Block = 1 << 20;                    // 1 Mio (multiple de toute taille de secteur)
        private const long FileSize = 1L << 30;               // fichiers de 1 Gio (compatible FAT32)
        private const long Margin = 32L << 20;                // laisse 32 Mio libres
        private const long SpeedTestSize = 256L << 20;

        public static CapacityTestResult Run(string root, CapacityTestMode mode, IProgress<CapacityProgress> progress,
            CancellationToken token, Action<string> log = null, long limitBytes = 0)
        {
            log = log ?? (_ => { });
            if (DriveScanner.IsSystemDrive(root))
                throw new InvalidOperationException("Le test ne peut pas être lancé sur le disque de Windows (il le remplirait).");

            var drive = new DriveInfo(root);
            string folder = Path.Combine(root, TestFolderName);
            DeleteFolder(folder); // restes d'un test interrompu
            Directory.CreateDirectory(folder);

            long free = drive.AvailableFreeSpace - Margin;
            long target = mode == CapacityTestMode.Speed ? Math.Min(SpeedTestSize, free) : free;
            if (limitBytes > 0) target = Math.Min(target, limitBytes);
            target = target / Block * Block;
            if (target < Block) throw new IOException("Pas assez d'espace libre pour tester ce lecteur.");

            var result = new CapacityTestResult
            {
                Mode = mode,
                DriveSize = drive.TotalSize,
                UsedBeforeTest = drive.TotalSize - drive.TotalFreeSpace
            };
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            IntPtr buffer = VirtualAlloc(IntPtr.Zero, (UIntPtr)Block, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (buffer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var words = new long[Block / 8];
            var expected = new long[Block / 8];

            try
            {
                log($"Test {(mode == CapacityTestMode.Full ? "complet" : "de vitesse")} : {target / 1e9:0.00} Go à écrire puis relire.");

                // ---- Écriture ----
                var sw = Stopwatch.StartNew();
                long written = 0;
                int fileIndex = 0;
                bool diskFull = false;
                while (written < target && !diskFull && !token.IsCancellationRequested)
                {
                    long thisFile = Math.Min(FileSize, target - written);
                    string path = Path.Combine(folder, $"test-{fileIndex:D4}.bin");
                    using (var h = Open(path, write: true))
                    {
                        for (long off = 0; off < thisFile && !token.IsCancellationRequested; off += Block)
                        {
                            Fill(words, seed, written);
                            Marshal.Copy(words, 0, buffer, words.Length);
                            if (!WriteFile(h, buffer, Block, out uint n, IntPtr.Zero) || n != Block)
                            {
                                int err = Marshal.GetLastWin32Error();
                                if (err == 112 || err == 39) { diskFull = true; break; } // disque plein
                                throw new Win32Exception(err);
                            }
                            written += Block;
                            if ((written & ((32L << 20) - 1)) == 0)
                                progress?.Report(new CapacityProgress { Phase = "Écriture", Done = written, Total = target, MBps = written / 1e6 / sw.Elapsed.TotalSeconds });
                        }
                    }
                    fileIndex++;
                }
                result.BytesWritten = written;
                result.WriteMBps = written / 1e6 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
                log($"Écriture terminée : {written / 1e9:0.00} Go à {result.WriteMBps:0.0} Mo/s.");

                // ---- Relecture et comparaison ----
                sw.Restart();
                long verified = 0;
                for (int f = 0; f < fileIndex && verified < written && !token.IsCancellationRequested; f++)
                {
                    string path = Path.Combine(folder, $"test-{f:D4}.bin");
                    long thisFile = Math.Min(FileSize, written - verified);
                    using (var h = Open(path, write: false))
                    {
                        for (long off = 0; off < thisFile && !token.IsCancellationRequested; off += Block)
                        {
                            bool ok = ReadFile(h, buffer, Block, out uint n, IntPtr.Zero) && n == Block;
                            if (ok)
                            {
                                Marshal.Copy(buffer, words, 0, words.Length);
                                Fill(expected, seed, verified);
                                ok = Same(words, expected);
                            }
                            if (!ok)
                            {
                                result.BytesBad += Block;
                                if (result.FirstBadOffset < 0) result.FirstBadOffset = verified;
                            }
                            verified += Block;
                            if ((verified & ((32L << 20) - 1)) == 0)
                                progress?.Report(new CapacityProgress { Phase = "Vérification", Done = verified, Total = written, MBps = verified / 1e6 / sw.Elapsed.TotalSeconds, BadBytes = result.BytesBad });
                        }
                    }
                }
                result.BytesVerified = verified;
                result.ReadMBps = verified / 1e6 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
                result.Cancelled = token.IsCancellationRequested;
                log(result.Summary());
                return result;
            }
            finally
            {
                VirtualFree(buffer, UIntPtr.Zero, MEM_RELEASE);
                DeleteFolder(folder);
                log("Fichiers de test supprimés.");
            }
        }

        /// <summary>Contenu calculable : chaque mot de 8 octets dépend de sa position absolue (splitmix64).</summary>
        private static void Fill(long[] words, ulong seed, long absoluteOffset)
        {
            ulong pos = (ulong)absoluteOffset / 8;
            for (int i = 0; i < words.Length; i++)
            {
                ulong z = seed + (pos + (ulong)i) * 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                words[i] = (long)(z ^ (z >> 31));
            }
        }

        private static bool Same(long[] a, long[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void DeleteFolder(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch { /* réessayé au prochain test */ }
        }

        // ---------- E/S sans cache ----------

        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        private const uint CREATE_ALWAYS = 2, OPEN_EXISTING = 3;
        private const uint FILE_FLAG_NO_BUFFERING = 0x20000000, FILE_FLAG_WRITE_THROUGH = 0x80000000;
        private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000, PAGE_READWRITE = 0x04;

        private static SafeFileHandle Open(string path, bool write)
        {
            var h = CreateFile(path, write ? GENERIC_WRITE : GENERIC_READ, 0, IntPtr.Zero,
                write ? CREATE_ALWAYS : OPEN_EXISTING, FILE_FLAG_NO_BUFFERING | (write ? FILE_FLAG_WRITE_THROUGH : 0), IntPtr.Zero);
            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            return h;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int count, out uint written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int count, out uint read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);
    }
}
