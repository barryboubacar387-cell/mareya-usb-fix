using System.Drawing;
using System.IO;
using System.Reflection;

namespace MareyaUSBFix.UI
{
    internal static class Theme
    {
        public static readonly Color Primary = Color.FromArgb(0x0B, 0x5E, 0xD7);
        public static readonly Color PrimaryDark = Color.FromArgb(0x08, 0x3F, 0x91);
        public static readonly Color Danger = Color.FromArgb(0xC6, 0x28, 0x28);
        public static readonly Color DangerBack = Color.FromArgb(0xFD, 0xEC, 0xEC);
        public static readonly Color Warning = Color.FromArgb(0x9A, 0x5B, 0x00);
        public static readonly Color WarningBack = Color.FromArgb(0xFF, 0xF4, 0xE0);
        public static readonly Color Success = Color.FromArgb(0x1B, 0x7F, 0x3B);
        public static readonly Color Muted = Color.FromArgb(0x5F, 0x63, 0x68);
        public static readonly Color Surface = Color.FromArgb(0xF6, 0xF8, 0xFB);

        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Bold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Font Title = new Font("Segoe UI Semibold", 16f);
        public static readonly Font Subtitle = new Font("Segoe UI", 9.5f);
        public static readonly Font Mono = new Font("Consolas", 9f);

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
    }
}
