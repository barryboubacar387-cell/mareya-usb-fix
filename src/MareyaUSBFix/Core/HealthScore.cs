using System;
using System.Collections.Generic;

namespace MareyaUSBFix.Core
{
    /// <summary>Mesures brutes utiles au calcul du pourcentage de santé (null = inconnu).</summary>
    public sealed class HealthInputs
    {
        public long? Wear;                 // usure consommée, en % (SSD / NVMe)
        public int? LifeLeft;              // durée de vie restante annoncée par le SSD, en %
        public long? Reallocated;          // SMART 5
        public long? Pending;              // SMART 197
        public long? Uncorrectable;        // SMART 198
        public long? ReportedUncorrect;    // SMART 187
        public long? ReadErrors;           // erreurs de lecture non corrigées (API Windows)
        public long? WriteErrors;          // erreurs d'écriture non corrigées (API Windows)
        public long? Temperature;          // °C
        public bool PredictFailure;        // le disque prédit lui-même sa panne
        public int? WindowsHealth;         // 0 sain, 1 avertissement, 2 défaillant
    }

    /// <summary>
    /// Pourcentage de santé, à la manière de Hard Disk Sentinel / CrystalDiskInfo :
    /// SSD : 100 % − usure (donnée fournie par le disque) ; ensuite, pour tous les disques,
    /// des points sont retirés pour chaque signe de dégradation, et la note est plafonnée si
    /// Windows ou le disque signalent une défaillance. Chaque retenue est expliquée.
    /// </summary>
    public static class HealthScore
    {
        public static (int? percent, List<string> breakdown) Compute(HealthInputs i)
        {
            var notes = new List<string>();
            if (i == null) return (null, notes);

            // Il faut au moins une vraie mesure : « Windows dit sain » seul ne suffit pas à afficher 100 %.
            bool measured = i.Wear.HasValue || i.LifeLeft.HasValue || i.Reallocated.HasValue || i.Pending.HasValue
                            || i.Uncorrectable.HasValue || i.ReportedUncorrect.HasValue || i.ReadErrors.HasValue
                            || i.WriteErrors.HasValue || i.Temperature.HasValue;
            if (!measured && !i.PredictFailure && i.WindowsHealth != 2) return (null, notes);

            double score = 100;
            if (i.Wear.HasValue && i.Wear >= 0)
            {
                long w = Math.Min(100, i.Wear.Value);
                score = 100 - w;
                if (w > 0) notes.Add($"−{w} % : usure du SSD (durée de vie consommée)");
            }
            else if (i.LifeLeft.HasValue && i.LifeLeft > 0 && i.LifeLeft <= 100)
            {
                score = i.LifeLeft.Value;
                if (score < 100) notes.Add($"−{100 - score:0} % : usure du SSD (durée de vie restante {score:0} %)");
            }

            void Penalty(long? count, int baseCost, double perUnit, int max, string what)
            {
                if (!(count > 0)) return;
                int p = (int)Math.Min(max, baseCost + count.Value * perUnit);
                score -= p;
                notes.Add($"−{p} % : {count} {what}");
            }

            Penalty(i.Reallocated, 5, 0.5, 40, "secteur(s) défectueux remplacé(s)");
            Penalty(i.Pending, 15, 2, 50, "secteur(s) instable(s)");
            Penalty(i.Uncorrectable, 15, 2, 50, "secteur(s) illisible(s)");
            Penalty(i.ReportedUncorrect, 5, 1, 20, "erreur(s) non corrigible(s)");
            Penalty(i.ReadErrors, 5, 1, 20, "erreur(s) de lecture non corrigée(s)");
            Penalty(i.WriteErrors, 5, 1, 20, "erreur(s) d'écriture non corrigée(s)");

            if (i.Temperature >= 70) { score -= 15; notes.Add($"−15 % : température très élevée ({i.Temperature} °C)"); }
            else if (i.Temperature >= 58) { score -= 5; notes.Add($"−5 % : température élevée ({i.Temperature} °C)"); }

            void Cap(int max, string why)
            {
                if (score <= max) return;
                notes.Add($"plafonné à {max} % : {why}");
                score = max;
            }
            if (i.WindowsHealth == 1) Cap(60, "Windows signale un avertissement");
            if (i.WindowsHealth == 2) Cap(20, "Windows signale le disque comme défaillant");
            if (i.PredictFailure) Cap(10, "le disque prédit lui-même une panne prochaine");

            int result = (int)Math.Round(Math.Max(1, Math.Min(100, score)));
            return (result, notes);
        }

        /// <summary>Vérifie le calcul sur des cas connus (utilisé par les tests automatiques : /selftest).</summary>
        public static List<string> SelfTest()
        {
            var failures = new List<string>();
            void Expect(string name, HealthInputs input, Func<int?, bool> ok, string expected)
            {
                var (p, _) = Compute(input);
                if (!ok(p)) failures.Add($"{name} : obtenu {(p.HasValue ? p + " %" : "inconnu")}, attendu {expected}");
            }
            Expect("aucune donnée", new HealthInputs(), p => p == null, "inconnu");
            Expect("Windows « sain » seul", new HealthInputs { WindowsHealth = 0 }, p => p == null, "inconnu");
            Expect("disque neuf", new HealthInputs { Reallocated = 0, Pending = 0, Temperature = 35 }, p => p == 100, "100 %");
            Expect("SSD usé à 3 %", new HealthInputs { Wear = 3, Temperature = 40 }, p => p == 97, "97 %");
            Expect("SSD 90 % de vie restante", new HealthInputs { LifeLeft = 90 }, p => p == 90, "90 %");
            Expect("4 secteurs remplacés", new HealthInputs { Reallocated = 4 }, p => p == 93, "93 %");
            Expect("3 secteurs instables", new HealthInputs { Pending = 3 }, p => p == 79, "79 %");
            Expect("surchauffe 60 °C + usure 10 %", new HealthInputs { Wear = 10, Temperature = 60 }, p => p == 85, "85 %");
            Expect("panne prédite", new HealthInputs { Reallocated = 0, PredictFailure = true }, p => p == 10, "10 %");
            Expect("disque en ruine", new HealthInputs { Reallocated = 2000, Pending = 500, Uncorrectable = 300 }, p => p == 1, "1 %");
            return failures;
        }

        /// <summary>Appréciation du pourcentage (mêmes seuils que le code couleur de la jauge).</summary>
        public static string Label(int percent) =>
            percent >= 90 ? "excellent" : percent >= 70 ? "bon" : percent >= 50 ? "moyen" : percent >= 25 ? "mauvais" : "critique";
    }
}
