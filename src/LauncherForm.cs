using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace OneCraft
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(11, 20, 36);
        public static readonly Color Bar = Color.FromArgb(9, 16, 29);
        public static readonly Color Panel = Color.FromArgb(18, 31, 54);
        public static readonly Color Card = Color.FromArgb(23, 39, 66);
        public static readonly Color Line = Color.FromArgb(46, 66, 99);
        public static readonly Color Gold = Color.FromArgb(227, 176, 75);
        public static readonly Color GoldLight = Color.FromArgb(246, 210, 128);
        public static readonly Color GoldDark = Color.FromArgb(160, 113, 34);
        public static readonly Color Paper = Color.FromArgb(232, 225, 207);
        public static readonly Color Muted = Color.FromArgb(140, 154, 179);
        public static readonly Color Sea = Color.FromArgb(63, 176, 192);
        public static readonly Color Bad = Color.FromArgb(214, 96, 84);

        static string family;

        public static Font Font(float size, FontStyle style)
        {
            if (family == null)
            {
                family = "Segoe UI";
                using (var fonts = new InstalledFontCollection())
                    foreach (var f in fonts.Families)
                        if (f.Name == "Bahnschrift") { family = f.Name; break; }
            }
            return new Font(family, size, style, GraphicsUnit.Pixel);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }

    /// <summary>Flat, painted button: Primary is the big gold PLAY, the rest are outlined.</summary>
    class FlatButton : Control
    {
        public bool Primary;
        bool hover, down;

        public FlatButton(string text, bool primary)
        {
            Text = text;
            Primary = primary;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = primary ? Theme.Font(30, FontStyle.Bold) : Theme.Font(14, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var r = new RectangleF(1.5f, 1.5f, Width - 3, Height - 3);
            using (var path = Theme.Round(r, Primary ? 8 : 6))
            {
                if (Primary)
                {
                    if (Enabled)
                    {
                        float glow = hover ? 1 : 0;
                        for (int i = 6; i >= 1; i--)
                            using (var halo = Theme.Round(new RectangleF(r.X - i + 1, r.Y - i + 1, r.Width + 2 * i - 2, r.Height + 2 * i - 2), 8 + i))
                            using (var pen = new Pen(Color.FromArgb((int)((10 + 14 * glow) * (7 - i) / 3), Theme.Gold), 2))
                                g.DrawPath(pen, halo);
                    }
                    Color top = !Enabled ? Color.FromArgb(92, 84, 66) : down ? Theme.GoldDark : hover ? Theme.GoldLight : Theme.Gold;
                    Color bottom = !Enabled ? Color.FromArgb(70, 64, 52) : down ? Color.FromArgb(130, 90, 26) : Theme.GoldDark;
                    using (var brush = new LinearGradientBrush(r, top, bottom, 90f)) g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(Enabled ? 255 : 90, Theme.GoldLight), 1.5f)) g.DrawPath(pen, path);
                    // A light inner bevel along the top edge reads as a chunky, blocky button.
                    using (var pen = new Pen(Color.FromArgb(Enabled ? 90 : 25, Color.White), 2))
                        g.DrawLine(pen, r.X + 8, r.Y + 3, r.Right - 8, r.Y + 3);
                    TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), Enabled ? Theme.Bg : Color.FromArgb(40, 40, 40),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                else
                {
                    using (var brush = new SolidBrush(down ? Color.FromArgb(40, Theme.Gold) : hover ? Color.FromArgb(24, Theme.Gold) : Color.FromArgb(150, Theme.Panel)))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Enabled ? (hover ? Theme.Gold : Theme.GoldDark) : Theme.Line, 1.2f)) g.DrawPath(pen, path);
                    TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), Enabled ? (hover ? Theme.GoldLight : Theme.Paper) : Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
        }
    }

    /// <summary>The big picture: in-game shots that slowly pan and cross-fade, with the One Craft title on top.</summary>
    class Banner : Control
    {
        readonly List<Image> slides = new List<Image>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 33 };
        int index;
        double t;
        const double Hold = 7.0, Fade = 1.2;
        public string Tagline = "Set sail across the Blue Planet";

        public Banner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            for (int i = 1; i <= 8; i++)
            {
                var img = LauncherForm.Resource("banner" + i + ".jpg");
                if (img != null) slides.Add(img);
            }
            timer.Tick += delegate
            {
                t += timer.Interval / 1000.0;
                if (t >= Hold + Fade) { t -= Hold + Fade; index = (index + 1) % Math.Max(1, slides.Count); }
                Invalidate();
            };
            timer.Start();
        }

        void Draw(Graphics g, Image img, float pan, float alpha, Rectangle area)
        {
            float scale = Math.Max((float)area.Width / img.Width, (float)area.Height / img.Height) * (1.06f + .04f * pan);
            float w = img.Width * scale, h = img.Height * scale;
            float x = area.X + (area.Width - w) / 2 - 12 * (pan - .5f), y = area.Y + (area.Height - h) / 2;
            if (alpha >= 1) { g.DrawImage(img, x, y, w, h); return; }
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
            using (var ia = new System.Drawing.Imaging.ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(img, new[] { new PointF(x, y), new PointF(x + w, y), new PointF(x, y + h) }, new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, ia);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Theme.Bg);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var clip = Theme.Round(r, 10))
            {
                g.SetClip(clip);
                g.InterpolationMode = InterpolationMode.Bilinear;
                if (slides.Count > 0)
                {
                    float total = (float)(Hold + Fade);
                    Draw(g, slides[index], (float)(t / total), 1, ClientRectangle);
                    if (t > Hold && slides.Count > 1)
                        Draw(g, slides[(index + 1) % slides.Count], (float)((t - Hold) / total), (float)((t - Hold) / Fade), ClientRectangle);
                }
                // Navy wash so the gold title always reads, darker toward the bottom.
                using (var wash = new LinearGradientBrush(r, Color.FromArgb(70, Theme.Bg), Color.FromArgb(200, Theme.Bg), 90f)) g.FillRectangle(wash, r);
                g.ResetClip();

                var titleFont = Theme.Font(Math.Max(40, Height / 6.5f), FontStyle.Bold);
                var tagFont = Theme.Font(Math.Max(15, Height / 26f), FontStyle.Regular);
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                float cy = Height / 2f - Height / 16f;
                var titleRect = new RectangleF(0, cy - titleFont.Size, Width, titleFont.Size * 2);
                for (int i = 4; i >= 1; i--)
                    using (var sh = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                        g.DrawString("ONE CRAFT", titleFont, sh, new RectangleF(titleRect.X + i, titleRect.Y + i, titleRect.Width, titleRect.Height), fmt);
                using (var gold = new LinearGradientBrush(titleRect, Theme.GoldLight, Theme.GoldDark, 90f))
                    g.DrawString("ONE CRAFT", titleFont, gold, titleRect, fmt);
                float lineY = cy + titleFont.Size * .78f;
                using (var pen = new Pen(Color.FromArgb(160, Theme.Gold), 1.5f))
                {
                    g.DrawLine(pen, Width / 2f - 150, lineY, Width / 2f - 14, lineY);
                    g.DrawLine(pen, Width / 2f + 14, lineY, Width / 2f + 150, lineY);
                }
                using (var b = new SolidBrush(Theme.Gold)) g.FillRectangle(b, Width / 2f - 4, lineY - 4, 8, 8);
                using (var b = new SolidBrush(Theme.Paper))
                    g.DrawString(Tagline, tagFont, b, new RectangleF(0, lineY + 10, Width, tagFont.Size * 2), fmt);
                titleFont.Dispose(); tagFont.Dispose();

                // Slide dots.
                for (int i = 0; i < slides.Count; i++)
                    using (var b = new SolidBrush(i == index ? Theme.Gold : Color.FromArgb(110, Theme.Paper)))
                        g.FillRectangle(b, Width / 2f - slides.Count * 9 + i * 18, Height - 22, 10, 4);

                using (var pen = new Pen(Color.FromArgb(150, Theme.GoldDark), 1.5f)) g.DrawPath(pen, clip);
            }
        }
    }

    /// <summary>"What's new": patch-note cards from the manifest, with its own wheel scrolling and a thin gold scrollbar.</summary>
    class NewsList : Control
    {
        public List<Dictionary<string, object>> Items = new List<Dictionary<string, object>>();
        int scroll, contentHeight;
        readonly Font title = Theme.Font(15, FontStyle.Bold), date = Theme.Font(11, FontStyle.Regular), body = Theme.Font(13, FontStyle.Regular);

        public NewsList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(Math.Max(0, contentHeight - Height), scroll - e.Delta / 2));
            Invalidate();
        }

        static List<string> Lines(Dictionary<string, object> item)
        {
            var o = new List<string>();
            object v;
            if (item.TryGetValue("lines", out v) && v is System.Collections.IEnumerable && !(v is string))
                foreach (var l in (System.Collections.IEnumerable)v) o.Add(l.ToString());
            else if (item.TryGetValue("text", out v) && v != null) o.Add(v.ToString());
            return o;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Theme.Panel);
            int w = Width - 14, y = 4 - scroll;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            if (Items.Count == 0)
                TextRenderer.DrawText(g, "Patch notes show up here once the launcher is online.", body, new Rectangle(8, 8, w - 16, 60), Theme.Muted, flags);
            foreach (var item in Items)
            {
                var lines = Lines(item);
                int inner = w - 28, h = 52;
                var sizes = new List<int>();
                foreach (var l in lines)
                {
                    int lh = TextRenderer.MeasureText(g, "• " + l, body, new Size(inner, 2000), flags).Height;
                    sizes.Add(lh);
                    h += lh + 8;
                }
                var card = new RectangleF(0.5f, y + .5f, w - 1, h);
                using (var path = Theme.Round(card, 7))
                {
                    using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                    using (var p = new Pen(Theme.Line)) g.DrawPath(p, path);
                }
                using (var b = new SolidBrush(Theme.Gold)) g.FillRectangle(b, 1, y + 12, 3, 22);
                TextRenderer.DrawText(g, Installer.Str(item, "title", "Update"), title, new Point(14, y + 12), Theme.Gold);
                TextRenderer.DrawText(g, Installer.Str(item, "date", ""), date, new Point(14, y + 32), Theme.Muted);
                int ly = y + 52;
                for (int i = 0; i < lines.Count; i++)
                {
                    TextRenderer.DrawText(g, "• " + lines[i], body, new Rectangle(14, ly, inner, sizes[i]), Theme.Paper, flags);
                    ly += sizes[i] + 8;
                }
                y += h + 12;
            }
            contentHeight = y + scroll;
            if (contentHeight > Height)
            {
                float track = Height - 8, thumb = Math.Max(30, track * Height / contentHeight);
                float pos = 4 + (track - thumb) * scroll / (contentHeight - Height);
                using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, Width - 5, 4, 3, track);
                using (var b = new SolidBrush(Theme.GoldDark)) g.FillRectangle(b, Width - 6, pos, 5, thumb);
            }
        }
    }

    public class LauncherForm : Form
    {
        public const string Version = "1.0.0";

        readonly string manifestSource;
        Dictionary<string, object> manifest;
        Settings settings = Settings.Load();
        bool busy;
        double progress;
        string status = "Checking for updates...";
        string updateUrl;

        readonly Banner banner = new Banner();
        readonly FlatButton play = new FlatButton("PLAY", true);
        readonly FlatButton updateOnly = new FlatButton("Update mods only", false);
        readonly FlatButton repair = new FlatButton("Repair install", false);
        readonly FlatButton folder = new FlatButton("Open game folder", false);
        readonly FlatButton restart = new FlatButton("Restart to update", false);
        readonly FlatButton gear = new FlatButton("⚙", false);
        readonly NewsList news = new NewsList();

        const int HeadH = 92, FootH = 76, Side = 360;

        public LauncherForm(string manifestSource)
        {
            this.manifestSource = manifestSource;
            Text = "One Craft Launcher";
            BackColor = Theme.Bg;
            ForeColor = Theme.Paper;
            Font = Theme.Font(14, FontStyle.Regular);
            MinimumSize = new Size(1040, 660);
            Size = new Size(1360, 820);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            try { using (var s = typeof(LauncherForm).Assembly.GetManifestResourceStream("icon.ico")) Icon = new Icon(s); } catch { }

            gear.Font = Theme.Font(20, FontStyle.Regular);
            restart.Visible = false;
            Controls.AddRange(new Control[] { banner, play, updateOnly, repair, folder, restart, gear, news });

            play.Click += delegate { Start(true, false); };
            updateOnly.Click += delegate { Start(false, false); };
            repair.Click += delegate
            {
                if (MessageBox.Show(this, "Re-download NeoForge and every One Craft mod? Your worlds, settings and your own extra mods are kept.", "Repair install", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                    Start(false, true);
            };
            folder.Click += delegate { Directory.CreateDirectory(Installer.GameDir); Process.Start("explorer.exe", "\"" + Installer.GameDir + "\""); };
            gear.Click += delegate { using (var s = new SettingsForm(settings)) s.ShowDialog(this); Invalidate(); };
            restart.Click += delegate
            {
                restart.Enabled = false;
                var job = new Installer();
                job.Status = s => Ui(() => { status = s; Invalidate(); });
                job.Progress = p => Ui(() => { progress = p; Invalidate(); });
                new Thread(() =>
                {
                    try { job.SelfUpdate(updateUrl); Ui(() => Application.Exit()); }
                    catch (Exception ex) { Installer.Log("Self update failed: " + ex); Ui(() => { status = "Launcher update failed: " + ex.Message; restart.Enabled = true; Invalidate(); }); }
                }) { IsBackground = true }.Start();
            };

            Load += delegate { LoadManifest(); };
            Layout += delegate { Arrange(); };
        }

        void Ui(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(a); else a();
        }

        void Arrange()
        {
            int mainW = ClientSize.Width - Side - 48;
            int bottomOfButtons = ClientSize.Height - FootH - 22;
            int secondaryY = bottomOfButtons - 40;
            int playY = secondaryY - 22 - 66;
            banner.Bounds = new Rectangle(24, HeadH + 16, mainW, playY - 22 - (HeadH + 16));
            int cx = 24 + mainW / 2;
            play.Bounds = new Rectangle(cx - 130, playY, 260, 66);
            int bw = 190, gap = 14;
            updateOnly.Bounds = new Rectangle(cx - bw * 3 / 2 - gap, secondaryY, bw, 38);
            repair.Bounds = new Rectangle(cx - bw / 2, secondaryY, bw, 38);
            folder.Bounds = new Rectangle(cx + bw / 2 + gap, secondaryY, bw, 38);
            gear.Bounds = new Rectangle(ClientSize.Width - 24 - 40, 26, 40, 40);
            restart.Bounds = new Rectangle(gear.Left - 14 - 170, 30, 170, 32);
            news.Bounds = new Rectangle(ClientSize.Width - Side + 18, HeadH + 70, Side - 36, ClientSize.Height - FootH - HeadH - 70 - 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            int w = ClientSize.Width, h = ClientSize.Height;

            // Header.
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, HeadH), Theme.Bar, Theme.Bg, 90f)) g.FillRectangle(b, 0, 0, w, HeadH);
            using (var f = Theme.Font(38, FontStyle.Bold))
            using (var gold = new LinearGradientBrush(new Rectangle(20, 14, 300, 46), Theme.GoldLight, Theme.Gold, 90f))
                g.DrawString("ONE CRAFT", f, gold, 20, 14);
            string sub = "One Craft Launcher  v" + Version;
            if (manifest != null) sub += "     •     One Craft " + Installer.Str(manifest, "version", "") + "  •  Minecraft " + Installer.Str(manifest, "minecraft", "1.21.1");
            using (var f = Theme.Font(13, FontStyle.Regular)) TextRenderer.DrawText(g, sub, f, new Point(24, 62), Theme.Muted);
            using (var pen = new Pen(Color.FromArgb(120, Theme.GoldDark))) g.DrawLine(pen, 0, HeadH - 1, w, HeadH - 1);

            // What's new column.
            var side = new Rectangle(w - Side, HeadH, Side, h - HeadH - FootH);
            using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, side);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, side.X, side.Y, side.X, side.Bottom);
            using (var f = Theme.Font(26, FontStyle.Bold)) TextRenderer.DrawText(g, "What's new", f, new Point(side.X + 18, HeadH + 24), Theme.Gold);

            // Footer.
            using (var b = new SolidBrush(Theme.Bar)) g.FillRectangle(b, 0, h - FootH, w, FootH);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, h - FootH, w, h - FootH);
            using (var f = Theme.Font(13, FontStyle.Regular))
            using (var small = Theme.Font(12, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "Signs in through the Minecraft Launcher", f, new Point(24, h - FootH + 16), Theme.Paper);
                TextRenderer.DrawText(g, "Game folder: " + Installer.GameDir + "    •    Memory: " + settings.MemoryGb + " GB", small, new Point(24, h - FootH + 40), Theme.Muted);

                var sr = new Rectangle(w - Side, h - FootH + 14, Side - 24, 20);
                TextRenderer.DrawText(g, status, f, sr, busy ? Theme.GoldLight : Theme.Paper, TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                var bar = new RectangleF(w - Side, h - FootH + 44, Side - 24, 8);
                using (var path = Theme.Round(bar, 4))
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                if (busy || progress > 0)
                {
                    RectangleF fill;
                    if (progress < 0)
                    {
                        float x = (float)((Environment.TickCount % 1600) / 1600.0) * (bar.Width + 80) - 80;
                        fill = RectangleF.Intersect(bar, new RectangleF(bar.X + x, bar.Y, 80, bar.Height));
                    }
                    else fill = new RectangleF(bar.X, bar.Y, Math.Max(8, bar.Width * (float)Math.Min(1, progress)), bar.Height);
                    if (fill.Width > 0)
                        using (var path = Theme.Round(fill, 4))
                        using (var b = new LinearGradientBrush(bar, Theme.GoldDark, Theme.GoldLight, 0f)) g.FillPath(b, path);
                }
            }
        }

        readonly System.Windows.Forms.Timer spinner = new System.Windows.Forms.Timer { Interval = 30 };

        void LoadManifest()
        {
            spinner.Tick += delegate { if (busy && progress < 0) Invalidate(new Rectangle(ClientSize.Width - Side, ClientSize.Height - FootH, Side, FootH)); };
            spinner.Start();
            SetBusy(true, "Checking for updates...");
            progress = -1;
            new Thread(() =>
            {
                try
                {
                    var m = Installer.LoadManifest(manifestSource);
                    Ui(() =>
                    {
                        manifest = m;
                        news.Items = Installer.List(m, "news");
                        news.Invalidate();
                        string tag = Installer.Str(m, "tagline", null);
                        if (tag != null) banner.Tagline = tag;
                        object l;
                        if (m.TryGetValue("launcher", out l) && l is Dictionary<string, object>)
                        {
                            var info = (Dictionary<string, object>)l;
                            string latest = Installer.Str(info, "version", Version);
                            updateUrl = Installer.Str(info, "url", null);
                            restart.Visible = updateUrl != null && Installer.CompareVersions(latest, Version) > 0;
                        }
                        progress = 0;
                        SetBusy(false, Installer.NeoForgeInstalled(Installer.Str(m, "neoforge", "21.1.265")) ? "Ready to play" : "Ready to install");
                    });
                }
                catch (Exception ex)
                {
                    Ui(() => { progress = 0; SetBusy(false, ex.Message); play.Enabled = false; updateOnly.Enabled = false; repair.Enabled = false; });
                }
            }) { IsBackground = true }.Start();
        }

        void SetBusy(bool on, string text)
        {
            busy = on;
            status = text;
            play.Enabled = updateOnly.Enabled = repair.Enabled = !on && manifest != null;
            play.Text = on ? "WORKING..." : "PLAY";
            Invalidate();
        }

        void Start(bool launch, bool clean)
        {
            if (busy || manifest == null) return;
            SetBusy(true, "Getting ready...");
            var job = new Installer();
            job.Status = s => Ui(() => { status = s; Invalidate(); });
            job.Progress = p => Ui(() => { progress = p; Invalidate(); });
            int memory = settings.MemoryGb;
            var m = manifest;
            new Thread(() =>
            {
                try
                {
                    if (clean) Installer.CleanForRepair(m);
                    job.Run(m, memory);
                    bool opened = launch && Installer.OpenMinecraftLauncher();
                    Ui(() =>
                    {
                        progress = 1;
                        if (!launch) SetBusy(false, "Up to date");
                        else if (opened) SetBusy(false, "Minecraft Launcher opened: pick One Craft and press Play");
                        else
                        {
                            SetBusy(false, "Minecraft Launcher not found");
                            if (MessageBox.Show(this, "One Craft is installed, but the Minecraft Launcher isn't on this PC.\n\nInstall it from minecraft.net, open it once and sign in, then press PLAY here again.\n\nOpen the download page now?",
                                    "One Craft Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                                Process.Start("https://www.minecraft.net/download");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Installer.Log("Install failed: " + ex);
                    Ui(() =>
                    {
                        progress = 0;
                        SetBusy(false, "Something went wrong");
                        MessageBox.Show(this, ex.Message, "One Craft Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
            }) { IsBackground = true }.Start();
        }

        // ---------------------------------------------------------------- resources

        public static Image Resource(string name)
        {
            var s = typeof(LauncherForm).Assembly.GetManifestResourceStream(name);
            return s == null ? null : Image.FromStream(s);
        }

        public static string IconDataUri()
        {
            using (var s = typeof(LauncherForm).Assembly.GetManifestResourceStream("icon128.png"))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            string source = Installer.DefaultManifest;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--manifest") source = args[i + 1];
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm(source));
        }
    }

    public class Settings
    {
        public int MemoryGb = 6;

        static string FilePath { get { return Path.Combine(Installer.Data, "settings.json"); } }

        public static Settings Load()
        {
            try { if (File.Exists(FilePath)) return Installer.Json.Deserialize<Settings>(File.ReadAllText(FilePath)); }
            catch { }
            var s = new Settings();
            try
            {
                ulong total = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024UL * 1024 * 1024);
                s.MemoryGb = (int)Math.Max(4, Math.Min(8, total / 3));
            }
            catch { }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Installer.Data);
            File.WriteAllText(FilePath, Installer.Json.Serialize(this));
        }
    }

    class SettingsForm : Form
    {
        public SettingsForm(Settings settings)
        {
            Text = "Settings";
            BackColor = Theme.Panel;
            ForeColor = Theme.Paper;
            Font = Theme.Font(14, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 270);

            var heading = new Label { Text = "Settings", Font = Theme.Font(24, FontStyle.Bold), ForeColor = Theme.Gold, AutoSize = true, Location = new Point(22, 16) };
            var memLabel = new Label { Text = "Memory for Minecraft (GB)", AutoSize = true, Location = new Point(24, 72) };
            var mem = new NumericUpDown { Minimum = 3, Maximum = 32, Value = Math.Max(3, Math.Min(32, settings.MemoryGb)), Location = new Point(300, 68), Width = 110, BackColor = Theme.Card, ForeColor = Theme.Paper, BorderStyle = BorderStyle.FixedSingle };
            var hint = new Label { Text = "6 GB suits most PCs. Takes effect the next time you press PLAY.", ForeColor = Theme.Muted, Font = Theme.Font(12, FontStyle.Regular), AutoSize = false, Size = new Size(390, 36), Location = new Point(24, 100) };
            var logs = new FlatButton("Open launcher log", false) { Bounds = new Rectangle(24, 150, 190, 36) };
            var mods = new FlatButton("Open mods folder", false) { Bounds = new Rectangle(226, 150, 190, 36) };
            var ok = new FlatButton("Save", false) { Bounds = new Rectangle(296, 212, 120, 38) };
            logs.Click += delegate
            {
                string f = Path.Combine(Installer.Data, "launcher.log");
                if (File.Exists(f)) Process.Start("notepad.exe", "\"" + f + "\"");
                else MessageBox.Show(this, "Nothing logged yet.", "Settings");
            };
            mods.Click += delegate
            {
                string d = Path.Combine(Installer.GameDir, "mods");
                Directory.CreateDirectory(d);
                Process.Start("explorer.exe", "\"" + d + "\"");
            };
            ok.Click += delegate { settings.MemoryGb = (int)mem.Value; settings.Save(); Close(); };
            Controls.AddRange(new Control[] { heading, memLabel, mem, hint, logs, mods, ok });
        }
    }
}
