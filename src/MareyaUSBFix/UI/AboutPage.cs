using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MareyaUSBFix.UI
{
    /// <summary>Page « À propos » : présentation, version, liens utiles.</summary>
    public sealed class AboutPage : UserControl
    {
        public const string ReleasesUrl = "https://github.com/mareya-group/mareya-usb-fix/releases/latest";
        public const string GuideUrl = "https://github.com/mareya-group/mareya-usb-fix/blob/main/docs/GUIDE.md";

        public AboutPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;

            var card = new Card { Dock = DockStyle.Top, Height = 470, Padding = new Padding(28, 26, 28, 24) };
            var logo = new PictureBox { Size = new Size(72, 72), Location = new Point(28, 26), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
            try { logo.Image = new Icon(Theme.AppIcon, 128, 128).ToBitmap(); } catch { }
            var name = new Label { Text = "Mareya USB Fix", Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Location = new Point(116, 30), BackColor = Color.White };
            var version = new Label { Text = $"Version {Theme.Version}  ·  Mareya Group", Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(119, 70), BackColor = Color.White };

            var text = new Label
            {
                Location = new Point(28, 122),
                Size = new Size(760, 250),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = Theme.Body,
                ForeColor = Theme.Text,
                BackColor = Color.White,
                Text =
                    "Logiciel gratuit contre le virus « raccourci » des clés USB et disques durs.\n\n" +
                    "•  Réparation : supprime les raccourcis piégés et les scripts du virus, réaffiche vos fichiers " +
                    "et nettoie l'ordinateur qui réinfecte les clés.\n" +
                    "•  Sécurité : rien n'est supprimé définitivement ; ce qui est retiré va en quarantaine et se restaure en un clic.\n" +
                    "•  Diagnostic : santé des disques (température, secteurs défectueux, usure) et test d'authenticité " +
                    "qui démasque les fausses clés.\n" +
                    "•  Protection automatique : chaque clé branchée est nettoyée dès son arrivée.\n\n" +
                    "Mareya USB Fix ne remplace pas un antivirus : faites aussi des analyses avec Windows Defender."
            };

            var links = new FlowLayoutPanel { Location = new Point(26, 392), Size = new Size(760, 44), BackColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var guide = new ModernButton("Guide d'utilisation", ButtonKind.Primary, Theme.Icons.Info);
            var download = new ModernButton("Page de téléchargement", ButtonKind.Secondary, Theme.Icons.Link);
            guide.Click += (s, e) => Open(GuideUrl);
            download.Click += (s, e) => Open(ReleasesUrl);
            links.Controls.AddRange(new Control[] { guide, download });

            card.Controls.AddRange(new Control[] { logo, name, version, text, links });
            card.Resize += (s, e) => { text.Width = card.Width - 56; links.Width = card.Width - 56; };
            Controls.Add(card);
        }

        private static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
