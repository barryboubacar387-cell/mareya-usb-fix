using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MareyaUSBFix.UI
{
    public enum ButtonKind { Primary, Secondary, Danger, Ghost }

    /// <summary>Bouton arrondi avec icône, survol et état désactivé grisé.</summary>
    public class ModernButton : Control
    {
        private bool _hover, _down;
        private ButtonKind _kind;

        public ModernButton(string text, ButtonKind kind = ButtonKind.Primary, string icon = null)
        {
            Text = text;
            _kind = kind;
            Icon = icon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Bold;
            Cursor = Cursors.Hand;
            Height = 38;
            Margin = new Padding(0, 0, 10, 0);
            AutoSizeToText();
        }

        public string Icon { get; set; }

        public ButtonKind Kind
        {
            get => _kind;
            set { _kind = value; Invalidate(); }
        }

        public void AutoSizeToText()
        {
            int textW = TextRenderer.MeasureText(Text, Font).Width;
            Width = textW + 36 + (string.IsNullOrEmpty(Icon) ? 0 : 26);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AutoSizeToText(); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color back, fore, border;
            if (!Enabled)
            {
                back = Theme.DisabledBack; fore = Theme.DisabledFore; border = Theme.DisabledBack;
            }
            else
            {
                switch (_kind)
                {
                    case ButtonKind.Primary:
                        back = _down ? Theme.Navy : _hover ? Theme.PrimaryDark : Theme.Primary; fore = Color.White; border = back; break;
                    case ButtonKind.Danger:
                        back = _down ? Color.FromArgb(0x8E, 0x1B, 0x1B) : _hover ? Color.FromArgb(0xA8, 0x20, 0x20) : Theme.Danger; fore = Color.White; border = back; break;
                    case ButtonKind.Ghost:
                        back = _hover ? Theme.PrimaryLight : Color.Transparent; fore = Theme.PrimaryDark; border = Color.Transparent; break;
                    default:
                        back = _down ? Theme.PrimaryLight : _hover ? Theme.Surface : Color.White; fore = Theme.PrimaryDark; border = Theme.Border; break;
                }
            }

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(r, 8))
            {
                if (back.A > 0) using (var b = new SolidBrush(back)) g.FillPath(b, path);
                if (border.A > 0) using (var p = new Pen(border)) g.DrawPath(p, path);
            }

            int x = 18;
            if (!string.IsNullOrEmpty(Icon))
            {
                using (var f = Theme.IconFont(11f))
                    TextRenderer.DrawText(g, Icon, f, new Rectangle(14, 0, 22, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                x = 40;
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - 14, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Carte blanche à coins arrondis, avec titre optionnel.</summary>
    public class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Padding = new Padding(18, 16, 18, 16);
            Margin = new Padding(0, 0, 0, 14);
        }

        public string Title { get; set; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Background)) g.FillRectangle(bg, ClientRectangle);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(r, 12))
            using (var b = new SolidBrush(BackColor))
            using (var p = new Pen(Theme.Border))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            if (!string.IsNullOrEmpty(Title))
                TextRenderer.DrawText(g, Title, Theme.Heading, new Point(Padding.Left - 2, 12), Theme.Text);
        }
    }

    public enum BannerKind { Info, Success, Warning, Danger, Neutral }

    /// <summary>Bandeau de résultat coloré : icône dans un rond, titre et texte.</summary>
    public class Banner : Control
    {
        private BannerKind _kind = BannerKind.Neutral;
        private string _detail = "";

        public Banner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 64;
            Margin = new Padding(0, 0, 0, 14);
        }

        public void Set(BannerKind kind, string title, string detail = "")
        {
            _kind = kind;
            Text = title;
            _detail = detail ?? "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Background)) g.FillRectangle(bg, ClientRectangle);

            Color back, accent; string icon;
            switch (_kind)
            {
                case BannerKind.Success: back = Theme.SuccessBack; accent = Theme.Success; icon = Theme.Icons.Check; break;
                case BannerKind.Warning: back = Theme.WarningBack; accent = Theme.Warning; icon = Theme.Icons.Warning; break;
                case BannerKind.Danger: back = Theme.DangerBack; accent = Theme.Danger; icon = Theme.Icons.Error; break;
                case BannerKind.Info: back = Theme.InfoBack; accent = Theme.Primary; icon = Theme.Icons.Info; break;
                default: back = Color.White; accent = Theme.Muted; icon = Theme.Icons.Info; break;
            }

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(r, 12))
            using (var b = new SolidBrush(back))
            using (var p = new Pen(Color.FromArgb(60, accent)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }

            int circle = 36, cy = (Height - circle) / 2;
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, 16, cy, circle, circle);
            using (var f = Theme.IconFont(13f))
                TextRenderer.DrawText(g, icon, f, new Rectangle(16, cy, circle, circle), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int x = 16 + circle + 14, w = Width - x - 16;
            bool hasDetail = _detail.Length > 0;
            TextRenderer.DrawText(g, Text, Theme.Bold, new Rectangle(x, hasDetail ? 11 : 0, w, hasDetail ? 22 : Height), accent,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | (hasDetail ? 0 : TextFormatFlags.VerticalCenter));
            if (hasDetail)
                TextRenderer.DrawText(g, _detail, Theme.Small, new Rectangle(x, 33, w, Height - 36), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Interrupteur marche/arrêt (style Windows 11).</summary>
    public class ToggleSwitch : Control
    {
        private bool _checked;
        public event EventHandler CheckedChanged;

        public ToggleSwitch(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = 28;
            Width = 60 + TextRenderer.MeasureText(text, Font).Width;
        }

        public Color TextColor { get; set; } = Theme.Text;

        public bool Checked
        {
            get => _checked;
            set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        /// <summary>Change l'état sans déclencher l'événement (chargement initial).</summary>
        public void SetSilently(bool value) { _checked = value; Invalidate(); }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(1, (Height - 20) / 2f, 40, 20);
            Color on = Enabled ? Theme.Primary : Theme.DisabledFore;
            using (var path = Theme.Rounded(track, 10))
            {
                if (_checked) using (var b = new SolidBrush(on)) g.FillPath(b, path);
                else using (var p = new Pen(TextColor == Color.White ? Color.FromArgb(160, 255, 255, 255) : Theme.Muted, 1.2f)) g.DrawPath(p, path);
            }
            float knob = 12, kx = _checked ? track.Right - knob - 4 : track.X + 4, ky = track.Y + (track.Height - knob) / 2;
            using (var b = new SolidBrush(_checked ? Color.White : (TextColor == Color.White ? Color.FromArgb(200, 255, 255, 255) : Theme.Muted)))
                g.FillEllipse(b, kx, ky, knob, knob);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(52, 0, Width - 52, Height), TextColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Entrée du menu latéral : icône, texte, barre d'accent quand elle est sélectionnée.</summary>
    public class NavItem : Control
    {
        private bool _selected, _hover;
        public string Icon { get; }

        public NavItem(string text, string icon)
        {
            Text = text;
            Icon = icon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 46;
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
        }

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Theme.Navy)) g.FillRectangle(bg, ClientRectangle);
            if (_selected || _hover)
            {
                var r = new RectangleF(10, 3, Width - 20, Height - 6);
                using (var path = Theme.Rounded(r, 8))
                using (var b = new SolidBrush(_selected ? Theme.NavyLight : Color.FromArgb(0x16, 0x24, 0x44)))
                    g.FillPath(b, path);
                if (_selected)
                    using (var path = Theme.Rounded(new RectangleF(10, 13, 4, Height - 26), 2))
                    using (var b = new SolidBrush(Color.FromArgb(0x4C, 0x9A, 0xFF)))
                        g.FillPath(b, path);
            }
            Color fore = _selected ? Color.White : Color.FromArgb(0xC4, 0xCE, 0xE0);
            using (var f = Theme.IconFont(13f))
                TextRenderer.DrawText(g, Icon, f, new Rectangle(24, 0, 28, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            using (var f = new Font(Theme.Body, _selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Text, f, new Rectangle(62, 0, Width - 66, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    /// <summary>Informations d'un lecteur affiché en tuile.</summary>
    public sealed class DriveEntry
    {
        public string Root;
        public string Name;
        public string Kind;
        public long Total;
        public long Free;
        public bool IsRemovable;
        public bool IsSystem;
        public string Letter => Root?.TrimEnd('\\');
    }

    /// <summary>Tuile de lecteur : icône, nom, type, barre d'espace utilisé.</summary>
    public class DriveTile : Control
    {
        private bool _selected, _hover;
        public DriveEntry Drive { get; }

        public DriveTile(DriveEntry drive)
        {
            Drive = drive;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(236, 86);
            Margin = new Padding(0, 0, 12, 0);
            Cursor = Cursors.Hand;
        }

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Background)) g.FillRectangle(bg, ClientRectangle);

            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(r, 12))
            {
                using (var b = new SolidBrush(_selected ? Theme.PrimaryLight : Color.White)) g.FillPath(b, path);
                using (var p = new Pen(_selected ? Theme.Primary : _hover ? Color.FromArgb(0xB8, 0xC4, 0xD8) : Theme.Border, _selected ? 2f : 1f))
                    g.DrawPath(p, path);
            }

            // icône dans un carré arrondi
            var iconBox = new RectangleF(14, 16, 40, 40);
            using (var path = Theme.Rounded(iconBox, 10))
            using (var b = new SolidBrush(Drive.IsRemovable ? Color.FromArgb(0xFF, 0xF1, 0xE0) : Color.FromArgb(0xE6, 0xEE, 0xFB)))
                g.FillPath(b, path);
            using (var f = Theme.IconFont(17f))
                TextRenderer.DrawText(g, Drive.IsSystem ? Theme.Icons.Windows : Drive.IsRemovable ? Theme.Icons.Usb : Theme.Icons.Drive, f,
                    Rectangle.Round(iconBox), Drive.IsRemovable ? Color.FromArgb(0xC2, 0x6A, 0x00) : Theme.Primary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int x = 66, w = Width - x - 12;
            TextRenderer.DrawText(g, $"{Drive.Letter}  {Drive.Name}", Theme.Bold, new Rectangle(x, 12, w, 20), Theme.Text, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Drive.Kind, Theme.Small, new Rectangle(x, 32, w, 18), Theme.Muted, TextFormatFlags.EndEllipsis);

            // barre d'espace utilisé
            float used = Drive.Total > 0 ? (float)(Drive.Total - Drive.Free) / Drive.Total : 0;
            var bar = new RectangleF(x, 54, w, 6);
            using (var path = Theme.Rounded(bar, 3)) using (var b = new SolidBrush(Color.FromArgb(0xE3, 0xE7, 0xEE))) g.FillPath(b, path);
            if (used > 0.01f)
            {
                var fill = new RectangleF(bar.X, bar.Y, Math.Max(6, bar.Width * used), bar.Height);
                using (var path = Theme.Rounded(fill, 3))
                using (var b = new SolidBrush(used > 0.9f ? Theme.Danger : Theme.Primary)) g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, $"{Drive.Free / 1e9:0.#} Go libres sur {Drive.Total / 1e9:0.#} Go", Theme.Small,
                new Rectangle(x, 62, w, 18), Theme.Muted, TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Petite étiquette colorée arrondie (« Virus », « À vérifier »…).</summary>
    public static class Pill
    {
        public static void Draw(Graphics g, string text, Rectangle area, Color fore, Color back)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var size = TextRenderer.MeasureText(text, Theme.Small);
            var r = new RectangleF(area.X, area.Y + (area.Height - 22) / 2f, size.Width + 14, 22);
            using (var path = Theme.Rounded(r, 11)) using (var b = new SolidBrush(back)) g.FillPath(b, path);
            TextRenderer.DrawText(g, text, Theme.Small, Rectangle.Round(r), fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Barre de progression fine et arrondie (déterminée ou animée).</summary>
    public class SlimProgress : Control
    {
        private readonly Timer _timer = new Timer { Interval = 30 };
        private float _phase;
        private int _value;

        public SlimProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 6;
            _timer.Tick += (s, e) => { _phase = (_phase + 0.015f) % 1.3f; Invalidate(); };
        }

        /// <summary>0 à 1000 ; -1 = animation indéterminée.</summary>
        [DefaultValue(0)]
        public int Value
        {
            get => _value;
            set { _value = value; _timer.Enabled = value < 0 && Visible; Invalidate(); }
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); _timer.Enabled = Visible && _value < 0; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Background)) g.FillRectangle(bg, ClientRectangle);
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(track, Height / 2f)) using (var b = new SolidBrush(Color.FromArgb(0xDD, 0xE3, 0xEC))) g.FillPath(b, path);
            RectangleF fill;
            if (_value < 0)
            {
                float w = track.Width * 0.3f, x = (track.Width + w) * _phase - w;
                fill = RectangleF.Intersect(new RectangleF(x, 0, w, track.Height), track);
            }
            else fill = new RectangleF(0, 0, track.Width * Math.Min(1000, _value) / 1000f, track.Height);
            if (fill.Width > 1)
                using (var path = Theme.Rounded(fill, Height / 2f)) using (var b = new SolidBrush(Theme.Primary)) g.FillPath(b, path);
        }

        protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
    }
}

namespace MareyaUSBFix.UI
{
    /// <summary>Jauge circulaire de santé : anneau coloré proportionnel au pourcentage, valeur au centre.</summary>
    public class HealthGauge : Control
    {
        private int? _percent;
        private string _caption = "";

        public HealthGauge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(156, 156);
        }

        public static Color ColorFor(int? percent) =>
            !percent.HasValue ? Theme.Muted : percent >= 70 ? Theme.Success : percent >= 50 ? Theme.Warning : Theme.Danger;

        public void Set(int? percent, string caption)
        {
            _percent = percent;
            _caption = caption ?? "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent?.BackColor ?? Color.White)) g.FillRectangle(bg, ClientRectangle);

            const float thickness = 12;
            float size = Math.Min(Width, Height) - thickness - 2;
            var ring = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            using (var track = new Pen(Color.FromArgb(0xE6, 0xEA, 0xF0), thickness))
                g.DrawEllipse(track, ring);
            if (_percent > 0)
                using (var arc = new Pen(ColorFor(_percent), thickness) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawArc(arc, ring, -90, 360f * _percent.Value / 100f);

            string value = _percent.HasValue ? $"{_percent} %" : "?";
            using (var f = new Font(Theme.Title.FontFamily, 22f, FontStyle.Bold))
                TextRenderer.DrawText(g, value, f, new Rectangle(0, (int)(Height / 2f - 26), Width, 36), ColorFor(_percent),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _caption, Theme.Small, new Rectangle(0, (int)(Height / 2f + 10), Width, 20), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
