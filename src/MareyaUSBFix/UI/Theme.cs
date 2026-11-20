using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MareyaUSBFix.UI
{
    /// <summary>Palette, polices et icônes de l'application (style Windows 11).</summary>
    internal static class Theme
    {
        // Marque
        public static readonly Color Primary = Color.FromArgb(0x0B, 0x5E, 0xD7);
        public static readonly Color PrimaryDark = Color.FromArgb(0x08, 0x47, 0xA6);
        public static readonly Color PrimaryLight = Color.FromArgb(0xE8, 0xF1, 0xFD);
        public static readonly Color Navy = Color.FromArgb(0x0E, 0x1B, 0x36);
        public static readonly Color NavyLight = Color.FromArgb(0x1B, 0x2C, 0x52);

        // Surfaces
        public static readonly Color Background = Color.FromArgb(0xF3, 0xF5, 0xF9);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(0xE2, 0xE6, 0xEE);
        public static readonly Color Surface = Color.FromArgb(0xF6, 0xF8, 0xFB);
        public static readonly Color Text = Color.FromArgb(0x1A, 0x20, 0x2C);
        public static readonly Color Muted = Color.FromArgb(0x5F, 0x67, 0x76);

        // États
        public static readonly Color Success = Color.FromArgb(0x16, 0x83, 0x4A);
        public static readonly Color SuccessBack = Color.FromArgb(0xE7, 0xF6, 0xEE);
        public static readonly Color Warning = Color.FromArgb(0xB4, 0x63, 0x00);
        public static readonly Color WarningBack = Color.FromArgb(0xFF, 0xF4, 0xE0);
        public static readonly Color Danger = Color.FromArgb(0xC6, 0x28, 0x28);
        public static readonly Color DangerBack = Color.FromArgb(0xFD, 0xEC, 0xEC);
        public static readonly Color InfoBack = Color.FromArgb(0xEA, 0xF2, 0xFE);

        public static readonly Color DisabledBack = Color.FromArgb(0xE6, 0xE8, 0xEB);
        public static readonly Color DisabledFore = Color.FromArgb(0x9A, 0x9E, 0xA4);

        // Polices
        private static readonly string UiFamily = FirstInstalled("Segoe UI Variable Text", "Segoe UI");
        private static readonly string DisplayFamily = FirstInstalled("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI");
        public static readonly Font Body = new Font(UiFamily, 9.75f);
        public static readonly Font Small = new Font(UiFamily, 8.75f);
        public static readonly Font Bold = new Font(UiFamily, 9.75f, FontStyle.Bold);
        public static readonly Font Title = new Font(DisplayFamily, 19f, FontStyle.Bold);
        public static readonly Font Heading = new Font(DisplayFamily, 12.5f, FontStyle.Bold);
        public static readonly Font Big = new Font(DisplayFamily, 15f, FontStyle.Bold);
        public static readonly Font Subtitle = new Font(UiFamily, 9.75f);
        public static readonly Font Mono = new Font("Consolas", 9f);

        /// <summary>Police d'icônes de Windows 10/11 (Segoe Fluent Icons ou Segoe MDL2 Assets).</summary>
        public static readonly string IconFamily = FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol");
        public static Font IconFont(float size) => new Font(IconFamily, size);

        public static class Icons
        {
            public const string Shield = "";
            public const string Health = "";
            public const string Box = "";
            public const string Info = "";
            public const string Usb = "";
            public const string Drive = "";
            public const string Refresh = "";
            public const string Search = "";
            public const string Repair = "";
            public const string Speed = "";
            public const string Check = "";
            public const string Warning = "";
            public const string Error = "";
            public const string Stop = "";
            public const string Lock = "";
            public const string Undo = "";
            public const string Folder = "";
            public const string Link = "";
            public const string Windows = "";
        }

        /// <summary>Pastille de couleur (s'affiche partout, contrairement aux émojis).</summary>
        public const string Dot = "●";

        private static string FirstInstalled(params string[] families)
        {
            using (var fonts = new System.Drawing.Text.InstalledFontCollection())
            {
                var names = fonts.Families.Select(f => f.Name).ToList();
                return families.FirstOrDefault(f => names.Contains(f)) ?? families.Last();
            }
        }

        /// <summary>Rectangle à coins arrondis.</summary>
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Ancien style de bouton plat, conservé pour les fenêtres secondaires.</summary>
        public static void GreyWhenDisabled(Button b, bool primary)
        {
            void Apply()
            {
                b.BackColor = b.Enabled ? (primary ? Primary : Color.White) : DisabledBack;
                b.ForeColor = b.Enabled ? (primary ? Color.White : PrimaryDark) : DisabledFore;
                b.FlatAppearance.BorderColor = b.Enabled ? Primary : DisabledBack;
                b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
            }
            b.EnabledChanged += (s, e) => Apply();
            Apply();
        }

        private static Icon _icon;

        public static Icon AppIcon
        {
            get
            {
                if (_icon != null) return _icon;
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MareyaUSBFix.app.ico"))
                    _icon = s != null ? new Icon(s) : SystemIcons.Shield;
                return _icon;
            }
        }

        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }
    }
}
