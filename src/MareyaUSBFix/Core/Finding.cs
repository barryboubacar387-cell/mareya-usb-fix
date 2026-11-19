using System;

namespace MareyaUSBFix.Core
{
    public enum Severity
    {
        /// <summary>Élément malveillant : coché par défaut, supprimé lors de la réparation.</summary>
        Malicious,
        /// <summary>Élément douteux : affiché mais non coché, l'utilisateur décide.</summary>
        Suspect
    }

    /// <summary>Un problème trouvé pendant l'analyse, avec l'action qui le corrige.</summary>
    public sealed class Finding
    {
        public Finding(Severity severity, string category, string description, string location, Action fix, int order = 0)
        {
            Order = order;
            Severity = severity;
            Category = category;
            Description = description;
            Location = location;
            Fix = fix ?? throw new ArgumentNullException(nameof(fix));
            Selected = severity == Severity.Malicious;
        }

        public Severity Severity { get; }
        public string Category { get; }
        public string Description { get; }
        public string Location { get; }
        public Action Fix { get; }
        public bool Selected { get; set; }

        /// <summary>Ordre d'exécution : 0 suppressions, 1 renommages, 2 réaffichage des fichiers.</summary>
        public int Order { get; }

        public override string ToString() =>
            $"[{(Severity == Severity.Malicious ? "MALVEILLANT" : "SUSPECT")}] {Category} : {Description} — {Location}";
    }
}
