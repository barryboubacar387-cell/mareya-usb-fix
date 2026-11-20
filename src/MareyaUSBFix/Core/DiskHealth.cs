using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace MareyaUSBFix.Core
{
    public enum HealthVerdict { Good, Warning, Critical, Unknown }

    /// <summary>Résultat du diagnostic de santé d'un disque physique.</summary>
    public sealed class DiskHealthReport
    {
        public string DriveRoot { get; set; }
        public string Model { get; set; }
        public string Serial { get; set; }
        public string Firmware { get; set; }
        public string Bus { get; set; }
        public string MediaType { get; set; }
        public ulong SizeBytes { get; set; }
        public HealthVerdict Verdict { get; set; } = HealthVerdict.Unknown;
        public List<string> Reasons { get; } = new List<string>();
        public List<KeyValuePair<string, string>> Details { get; } = new List<KeyValuePair<string, string>>();
        public bool HasSmartData { get; set; }

        public string VerdictText
        {
            get
            {
                switch (Verdict)
                {
                    case HealthVerdict.Good: return "Bon état";
                    case HealthVerdict.Warning: return "À surveiller";
                    case HealthVerdict.Critical: return "Risque de panne : sauvegardez vos données au plus vite";
                    default: return "État inconnu : ce disque ne transmet pas ses informations de santé";
                }
            }
        }

        public void Raise(HealthVerdict level, string reason)
        {
            if (Verdict == HealthVerdict.Unknown || level > Verdict) Verdict = level;
            Reasons.Add(reason);
        }

        public void Add(string key, string value) => Details.Add(new KeyValuePair<string, string>(key, value));
    }

    /// <summary>
    /// Lit l'état de santé d'un disque, en lecture seule. Deux sources complémentaires :
    /// l'API de stockage de Windows (MSFT_PhysicalDisk / compteurs de fiabilité, y compris NVMe)
    /// et les attributs SMART bruts (MSStorageDriver_*, disques SATA). Les clés USB et beaucoup de
    /// boîtiers externes n'exposent ni l'une ni l'autre : le verdict est alors « inconnu ».
    /// </summary>
    public static class DiskHealth
    {
        public static DiskHealthReport Check(string driveRoot, Action<string> log = null)
        {
            log = log ?? (_ => { });
            var report = new DiskHealthReport { DriveRoot = driveRoot };
            string letter = driveRoot.Substring(0, 2); // « E: »

            // 1. Retrouver le disque physique qui porte ce lecteur
            ManagementObject disk = FindPhysicalDisk(letter);
            if (disk == null)
            {
                report.Raise(HealthVerdict.Unknown, "Disque physique introuvable pour ce lecteur (lecteur virtuel ou réseau ?).");
                return report;
            }

            int index = Convert.ToInt32(disk["Index"]);
            report.Model = (disk["Model"] as string)?.Trim();
            report.Serial = (disk["SerialNumber"] as string)?.Trim();
            report.Firmware = (disk["FirmwareRevision"] as string)?.Trim();
            report.SizeBytes = Convert.ToUInt64(disk["Size"] ?? 0UL);
            string pnp = disk["PNPDeviceID"] as string ?? "";
            report.Bus = disk["InterfaceType"] as string;

            // 2. API de stockage de Windows
            Safe(() => ReadStorageApi(index, report), "API de stockage", log);
            // 3. SMART brut (complète ou remplace)
            Safe(() => ReadSmart(pnp, report), "SMART", log);

            report.Add("Modèle", report.Model ?? "?");
            if (!string.IsNullOrEmpty(report.Serial)) report.Add("Numéro de série", report.Serial);
            if (!string.IsNullOrEmpty(report.Firmware)) report.Add("Firmware", report.Firmware);
            report.Add("Capacité annoncée", FormatBytes(report.SizeBytes));
            report.Add("Connexion", report.Bus ?? "?");
            if (!string.IsNullOrEmpty(report.MediaType)) report.Add("Type", report.MediaType);

            if (!report.HasSmartData && report.Verdict == HealthVerdict.Unknown)
                report.Reasons.Add("Aucune donnée de santé transmise (fréquent pour les clés USB et boîtiers externes). " +
                                   "Utilisez le test d'authenticité pour vérifier la mémoire elle-même.");
            else if (report.Verdict == HealthVerdict.Unknown)
                report.Verdict = HealthVerdict.Good;
            return report;
        }

        private static ManagementObject FindPhysicalDisk(string letter)
        {
            using (var parts = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
            {
                foreach (ManagementObject part in parts.Get())
                {
                    using (var disks = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                    {
                        foreach (ManagementObject d in disks.Get()) return d;
                    }
                }
            }
            return null;
        }

        // ---------- API de stockage (Windows 8 et plus) ----------

        private static void ReadStorageApi(int index, DiskHealthReport r)
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId='{index}'")))
            {
                foreach (ManagementObject pd in s.Get())
                {
                    r.MediaType = MediaTypeName(Convert.ToInt32(pd["MediaType"] ?? 0));
                    r.Bus = BusTypeName(Convert.ToInt32(pd["BusType"] ?? 0)) ?? r.Bus;
                    int health = Convert.ToInt32(pd["HealthStatus"] ?? 5);
                    if (health == 0) { r.HasSmartData = true; r.Add("État selon Windows", "Sain"); }
                    else if (health == 1) { r.HasSmartData = true; r.Raise(HealthVerdict.Warning, "Windows signale un avertissement sur ce disque."); }
                    else if (health == 2) { r.HasSmartData = true; r.Raise(HealthVerdict.Critical, "Windows signale ce disque comme défaillant."); }
                }
            }
            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM MSFT_StorageReliabilityCounter WHERE DeviceId='{index}'")))
            {
                foreach (ManagementObject c in s.Get())
                {
                    long? temp = Num(c["Temperature"]), tempMax = Num(c["TemperatureMax"]), wear = Num(c["Wear"]);
                    long? hours = Num(c["PowerOnHours"]), readErr = Num(c["ReadErrorsUncorrected"]), writeErr = Num(c["WriteErrorsUncorrected"]);
                    if (temp > 0) { r.HasSmartData = true; r.Add("Température", $"{temp} °C" + (tempMax > 0 ? $" (max. {tempMax} °C)" : "")); CheckTemperature(r, temp.Value); }
                    if (hours > 0) { r.HasSmartData = true; r.Add("Heures d'utilisation", FormatHours(hours.Value)); }
                    if (wear.HasValue && (wear > 0 || r.MediaType == "SSD"))
                    {
                        r.HasSmartData = true;
                        r.Add("Usure (SSD)", $"{wear} %");
                        if (wear >= 90) r.Raise(HealthVerdict.Critical, $"SSD usé à {wear} %.");
                        else if (wear >= 70) r.Raise(HealthVerdict.Warning, $"SSD usé à {wear} %.");
                    }
                    if (readErr > 0) r.Raise(HealthVerdict.Warning, $"{readErr} erreur(s) de lecture non corrigée(s).");
                    if (writeErr > 0) r.Raise(HealthVerdict.Warning, $"{writeErr} erreur(s) d'écriture non corrigée(s).");
                }
            }
        }

        // ---------- SMART brut (disques SATA) ----------

        private static void ReadSmart(string pnp, DiskHealthReport r)
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            scope.Connect();

            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSStorageDriver_FailurePredictStatus")))
            {
                foreach (ManagementObject o in s.Get())
                {
                    if (!Matches(o, pnp)) continue;
                    r.HasSmartData = true;
                    if (o["PredictFailure"] is bool fail && fail)
                        r.Raise(HealthVerdict.Critical, "Le disque lui-même prédit une panne prochaine (SMART).");
                }
            }

            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSStorageDriver_ATAPISmartData")))
            {
                foreach (ManagementObject o in s.Get())
                {
                    if (!Matches(o, pnp) || !(o["VendorSpecific"] is byte[] data)) continue;
                    r.HasSmartData = true;
                    foreach (var (id, raw) in ParseAttributes(data)) Interpret(id, raw, r);
                }
            }
        }

        /// <summary>Attributs SMART : à partir de l'octet 2, 30 entrées de 12 octets (id, drapeaux, valeurs, brut sur 6 octets).</summary>
        public static IEnumerable<(int id, long raw)> ParseAttributes(byte[] data)
        {
            for (int i = 2; i + 12 <= data.Length && i < 2 + 30 * 12; i += 12)
            {
                int id = data[i];
                if (id == 0) continue;
                long raw = 0;
                for (int b = 0; b < 6; b++) raw |= (long)data[i + 5 + b] << (8 * b);
                yield return (id, raw);
            }
        }

        private static void Interpret(int id, long raw, DiskHealthReport r)
        {
            switch (id)
            {
                case 5:
                    r.Add("Secteurs réalloués", raw.ToString());
                    if (raw > 50) r.Raise(HealthVerdict.Critical, $"{raw} secteurs défectueux remplacés : la surface du disque se dégrade.");
                    else if (raw > 0) r.Raise(HealthVerdict.Warning, $"{raw} secteur(s) défectueux remplacé(s).");
                    break;
                case 197:
                    r.Add("Secteurs instables", raw.ToString());
                    if (raw > 0) r.Raise(HealthVerdict.Critical, $"{raw} secteur(s) instable(s) en attente : risque de perte de données.");
                    break;
                case 198:
                    r.Add("Secteurs illisibles", raw.ToString());
                    if (raw > 0) r.Raise(HealthVerdict.Critical, $"{raw} secteur(s) illisible(s).");
                    break;
                case 187:
                    if (raw > 0) r.Raise(HealthVerdict.Warning, $"{raw} erreur(s) non corrigible(s) signalée(s).");
                    break;
                case 9:
                    if (!r.Details.Any(d => d.Key == "Heures d'utilisation"))
                        r.Add("Heures d'utilisation", FormatHours(raw & 0xFFFFFFFF));
                    break;
                case 194:
                case 190:
                    if (!r.Details.Any(d => d.Key == "Température"))
                    {
                        long t = raw & 0xFF;
                        if (t > 0 && t < 120) { r.Add("Température", $"{t} °C"); CheckTemperature(r, t); }
                    }
                    break;
            }
        }

        private static void CheckTemperature(DiskHealthReport r, long t)
        {
            if (t >= 70) r.Raise(HealthVerdict.Critical, $"Température très élevée ({t} °C).");
            else if (t >= 58) r.Raise(HealthVerdict.Warning, $"Température élevée ({t} °C) : vérifiez l'aération.");
        }

        /// <summary>InstanceName SMART = PNPDeviceID du disque suivi de « _0 ».</summary>
        private static bool Matches(ManagementObject o, string pnp)
        {
            string instance = (o["InstanceName"] as string ?? "").ToUpperInvariant();
            string id = pnp.ToUpperInvariant();
            return id.Length > 0 && instance.StartsWith(id, StringComparison.Ordinal);
        }

        private static string MediaTypeName(int t) => t == 3 ? "Disque dur (HDD)" : t == 4 ? "SSD" : t == 5 ? "SCM" : null;

        private static string BusTypeName(int t)
        {
            switch (t)
            {
                case 7: return "USB";
                case 11: return "SATA";
                case 17: return "NVMe";
                case 8: return "RAID";
                case 10: return "SAS";
                case 12: return "Carte SD";
                case 13: return "Carte MMC";
                case 15: return "Fichier virtuel";
                default: return null;
            }
        }

        private static long? Num(object o)
        {
            if (o == null) return null;
            try { return Convert.ToInt64(o); } catch { return null; }
        }

        public static string FormatBytes(ulong b)
        {
            double gb = b / 1e9;
            return gb >= 1 ? $"{gb:0.#} Go ({b / 1073741824.0:0.#} Gio)" : $"{b / 1e6:0} Mo";
        }

        /// <summary>« 182 h (≈ 8 jours de fonctionnement) », « 9 500 h (≈ 1,1 an de fonctionnement) »…</summary>
        public static string FormatHours(long h)
        {
            string hours = h.ToString("#,0").Replace(",", "\u202F");
            if (h < 24) return $"{hours} h";
            double days = h / 24.0;
            string span;
            if (days < 60) span = $"{Math.Round(days):0} jour{(Math.Round(days) >= 2 ? "s" : "")}";
            else if (days < 365) span = $"{Math.Round(days / 30.44):0} mois";
            else
            {
                double years = days / 365.25;
                span = $"{years:0.#} an{(years >= 2 ? "s" : "")}";
            }
            return $"{hours} h (≈ {span} de fonctionnement)";
        }

        private static void Safe(Action a, string what, Action<string> log)
        {
            try { a(); }
            catch (Exception ex) { log($"({what} indisponible : {ex.Message})"); }
        }
    }
}
