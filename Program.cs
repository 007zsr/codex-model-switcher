using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Microsoft.Win32;
using System.Net;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("Codex Model Switcher")]
[assembly: AssemblyDescription("Switch Codex between OpenAI, DeepSeek and Kimi providers")]
[assembly: AssemblyCompany("Local")]
[assembly: AssemblyProduct("Codex Model Switcher")]
[assembly: AssemblyVersion("2.7.1.0")]
[assembly: AssemblyFileVersion("2.7.1.0")]

namespace CodexModelSwitcher
{
    internal enum ProviderKind { OpenAi, DeepSeek, Kimi }

    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(10, 16, 27);
        public static readonly Color Surface = Color.FromArgb(19, 29, 45);
        public static readonly Color Text = Color.FromArgb(238, 244, 255);
        public static readonly Color Muted = Color.FromArgb(151, 171, 198);
        public static readonly Color Blue = Color.FromArgb(90, 177, 255);
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath(); int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right-d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); p.AddArc(r.X, r.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public static void Apply(Control root)
        {
            if (root is Form) { root.BackColor = Background; root.ForeColor = Text; }
            foreach (Control c in root.Controls)
            {
                if (c.BackColor == Color.White) c.BackColor = Surface;
                if (c is Button)
                {
                    Button b = (Button)c;
                    bool primary = b.Text == "切换并重启 ChatGPT" || b.Text == "完成";
                    b.BackColor = primary ? Color.FromArgb(35, 108, 235) : Surface;
                    b.ForeColor = Text; b.FlatAppearance.BorderSize = 0;
                }
                else if (c is TextBox || c is ComboBox) { c.BackColor = Background; c.ForeColor = Text; if (c is ComboBox) ((ComboBox)c).FlatStyle = FlatStyle.Flat; if (c is TextBox) ((TextBox)c).BorderStyle = BorderStyle.FixedSingle; }
                else if (c is GroupBox || c is TabPage || c is RoundPanel) { c.BackColor = Surface; c.ForeColor = Text; }
                else if (c is Label || c is RadioButton || c is CheckBox)
                {
                    if (c.ForeColor.GetBrightness() < .45) c.ForeColor = Text;
                    c.BackColor = Color.Transparent;
                }
                Apply(c);
            }
        }
    }

    internal sealed class RoundPanel : Panel
    {
        public bool Selected;
        public RoundPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            if (r.Width < 1 || r.Height < 1) return;
            using (GraphicsPath p = Theme.Rounded(r, 16))
            using (Pen pen = new Pen(Selected ? Theme.Blue : Color.FromArgb(43, 59, 81), Selected ? 2 : 1)) e.Graphics.DrawPath(pen, p);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 2 || Height < 2) return;
            using (GraphicsPath p = Theme.Rounded(new Rectangle(0, 0, Width, Height), 16))
            { Region old = Region; Region = new Region(p); if (old != null) old.Dispose(); }
        }
    }

    internal sealed class TechButton : Button
    {
        private bool hover;
        public TechButton() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width < 3 || Height < 3) return;
            using (GraphicsPath p = Theme.Rounded(new Rectangle(1, 1, Width-3, Height-3), 11))
            using (SolidBrush b = new SolidBrush(hover && Enabled ? Color.FromArgb(40, 75, 122) : BackColor))
            using (Pen border = new Pen(Focused ? Theme.Blue : Color.FromArgb(57, 80, 113)))
            { e.Graphics.FillPath(b, p); e.Graphics.DrawPath(border, p); }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class HeroPanel : Panel
    {
        public HeroPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            Image art = AppAssets.TryLoad("orbital-header-v2.png");
            if (art != null) e.Graphics.DrawImage(art, new Rectangle(Width - 420, -72, 420, 280));
            using (LinearGradientBrush fade = new LinearGradientBrush(new Rectangle(Width-421, 0, 130, Height), Theme.Background, Color.Transparent, 0F))
                e.Graphics.FillRectangle(fade, Width-421, 0, 130, Height);
            using (Pen pen = new Pen(Color.FromArgb(39, 66, 103))) e.Graphics.DrawLine(pen, 28, Height-1, Width-28, Height-1);
        }
    }

    internal sealed class ActivityBar : Control
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private float phase;
        public ActivityBar()
        {
            DoubleBuffered = true;
            timer.Interval = 30;
            timer.Tick += delegate { phase = (phase + .018F) % 1F; Invalidate(); };
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e); timer.Enabled = Visible; if (Visible) phase = .2F;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
            int length = Math.Max(24, Width/4);
            int left = (int)(phase * (Width + length)) - length;
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(left, 0, length, Math.Max(1, Height)), Color.FromArgb(35, 108, 235), Theme.Blue, 0F))
                e.Graphics.FillRectangle(b, left, 0, length, Height);
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class ProviderMark : Control
    {
        public ProviderMark() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(35, 110, 243), Color.FromArgb(20, 44, 78), 60F))
            using (Pen p = new Pen(Theme.Blue))
            { e.Graphics.FillEllipse(b, 1, 1, Width-3, Height-3); e.Graphics.DrawEllipse(p, 1, 1, Width-3, Height-3); }
            using (Font font = new Font("Segoe UI", 14F, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, Text, font, ClientRectangle, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    internal sealed class KeyTabs : Panel
    {
        private readonly List<Panel> pages = new List<Panel>();
        private readonly List<Button> buttons = new List<Button>();
        public void AddPage(string text, Panel page)
        {
            int index = pages.Count;
            Button button = new TechButton { Text = text, Location = new Point(index*122, 0), Size = new Size(114, 28) };
            page.Location = new Point(0, 34); page.Size = new Size(Width, Math.Max(1, Height-34));
            page.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            page.Visible = index == 0;
            pages.Add(page); buttons.Add(button); Controls.Add(page); Controls.Add(button);
            button.Click += delegate { for (int i=0; i<pages.Count; i++) { pages[i].Visible = i == index; buttons[i].ForeColor = i == index ? Theme.Blue : Theme.Muted; } };
        }
    }

    internal sealed class TechGroupBox : GroupBox
    {
        public TechGroupBox() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.Rounded(new Rectangle(1, 8, Width-3, Height-10), 14))
            using (SolidBrush b = new SolidBrush(Theme.Surface))
            using (Pen border = new Pen(Color.FromArgb(43, 59, 81))) { e.Graphics.FillPath(b,p); e.Graphics.DrawPath(border,p); }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Point(16, 0), Theme.Muted, Theme.Background);
        }
    }

    // Every visual asset ships inside this project folder and is resolved relative to the program
    // itself (or to CODEX_MODEL_SWITCHER_APP_HOME when Launcher.ps1 hosts the UI). Nothing points at
    // an absolute path, so the whole folder can be copied to another machine and still work.
    internal static class AppAssets
    {
        private static readonly Dictionary<string, Image> Cache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public static string AppDirectory
        {
            get
            {
                string home = Environment.GetEnvironmentVariable("CODEX_MODEL_SWITCHER_APP_HOME");
                if (!string.IsNullOrWhiteSpace(home))
                    return Path.GetFullPath(Environment.ExpandEnvironmentVariables(home)).TrimEnd(Path.DirectorySeparatorChar);
                return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            }
        }

        public static string Directory { get { return Path.Combine(AppDirectory, "assets"); } }

        public static string PathFor(string fileName) { return Path.Combine(Directory, fileName); }

        public static bool Exists(string fileName)
        {
            try { return File.Exists(PathFor(fileName)); }
            catch { return false; }
        }

        public static string[] RequiredFiles()
        {
            return new string[]
            {
                "app-icon.ico", "app-icon-1024.png", "app-icon-small-512.png",
                "mark-chatgpt-256.png", "mark-deepseek-256.png", "mark-kimi-256.png", "orbital-header-v2.png"
            };
        }

        public static List<string> MissingFiles()
        {
            List<string> missing = new List<string>();
            foreach (string file in RequiredFiles()) if (!Exists(file)) missing.Add(file);
            return missing;
        }

        public static Image TryLoad(string fileName)
        {
            Image cached;
            if (Cache.TryGetValue(fileName, out cached)) return cached;
            try
            {
                string path = PathFor(fileName);
                if (!File.Exists(path)) return null;
                // Copied into a new bitmap so the file is not kept open and nothing depends on the
                // current working directory.
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image original = Image.FromStream(stream))
                {
                    Bitmap copy = new Bitmap(original.Width, original.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(copy))
                    {
                        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.DrawImageUnscaled(original, 0, 0);
                    }
                    copy.SetResolution(96, 96);
                    Cache[fileName] = copy;
                    return copy;
                }
            }
            catch { return null; }
        }

        public static Icon LoadAppIcon()
        {
            try
            {
                string path = PathFor("app-icon.ico");
                if (File.Exists(path))
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Icon temporary = new Icon(stream))
                        return new Icon(temporary, new Size(32, 32));
                }
            }
            catch { }

            try
            {
                string executable = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(executable) && File.Exists(executable) &&
                    !executable.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) &&
                    !executable.EndsWith("pwsh.exe", StringComparison.OrdinalIgnoreCase))
                {
                    Icon extracted = Icon.ExtractAssociatedIcon(executable);
                    if (extracted != null) return extracted;
                }
            }
            catch { }
            return SystemIcons.Application;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                string originalDataHome = Environment.GetEnvironmentVariable("CODEX_MODEL_SWITCHER_DATA_HOME");
                string sandboxRoot = Path.Combine(Path.GetTempPath(), "codex-model-switcher-selftest-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(sandboxRoot);
                try
                {
                    Environment.SetEnvironmentVariable("CODEX_MODEL_SWITCHER_DATA_HOME",
                        Path.Combine(sandboxRoot, "data"), EnvironmentVariableTarget.Process);
                    SwitcherEngine.RunSelfTest();
                    SwitcherForm.RunProviderSelectionSelfTest();
                    SwitcherForm.RunFormSmokeSelfTest();
                    Console.WriteLine("SELF-TEST PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("SELF-TEST FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Environment.SetEnvironmentVariable("CODEX_MODEL_SWITCHER_DATA_HOME", originalDataHome,
                        EnvironmentVariableTarget.Process);
                    try { Directory.Delete(sandboxRoot, true); } catch { }
                }
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\CodexModelSwitcher.Singleton", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Codex 模型切换器已经在运行。", "Codex 模型切换器",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SwitcherForm());
            }
        }
    }

    internal sealed class SwitcherForm : Form
    {
        private readonly SwitcherEngine engine;
        private RadioButton openAiRadio;
        private RadioButton deepSeekRadio;
        private RadioButton kimiRadio;
        private ComboBox modelCombo;
        private ComboBox deepSeekModeCombo;
        private Label modelLabel;
        private TextBox keyTextBox;
        private Button saveKeyButton;
        private Button deleteKeyButton;
        private CheckBox showKeyCheckBox;
        private TextBox kimiKeyTextBox;
        private Button saveKimiKeyButton;
        private Button deleteKimiKeyButton;
        private CheckBox showKimiKeyCheckBox;
        private Label statusProvider;
        private Label statusModel;
        private Label statusKey;
        private Label statusKimiKey;
        private Label messageLabel;
        private Button applyButton;
        private Button restartButton;
        private ProviderKind? lastModelListProvider;
        private bool syncingProviderSelection;
        private ActivityBar activity;

        private static readonly Color WindowColor = Theme.Background;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Muted = Theme.Muted;
        private static readonly Color Accent = Color.FromArgb(38, 99, 235);
        private static readonly Color DeepSeekBlue = Color.FromArgb(74, 105, 255);
        private static readonly Color KimiViolet = Theme.Blue;
        private static readonly Color Good = Theme.Blue;

        public SwitcherForm()
        {
            engine = new SwitcherEngine();
            Text = "Codex 模型切换器";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(920, 790);
            MinimumSize = new Size(880, 730);
            BackColor = WindowColor;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            Icon = AppAssets.LoadAppIcon();

            BuildUi();
            Theme.Apply(this);
            Load += delegate { InitializeState(); };
        }

        // Regression guard for the "cannot switch back to ChatGPT" bug: the two provider cards
        // keep their radio button in separate panels, so no shared parent clears the other one.
        internal static void RunProviderSelectionSelfTest()
        {
            using (SwitcherForm form = new SwitcherForm())
            {
                // Clicking a card is what the click handlers do: check that radio button.
                form.deepSeekRadio.Checked = true;
                if (form.SelectedProvider() != ProviderKind.DeepSeek)
                    throw new Exception("Provider selection: choosing DeepSeek did not select DeepSeek.");

                form.openAiRadio.Checked = true;
                if (form.deepSeekRadio.Checked || form.kimiRadio.Checked)
                    throw new Exception("Provider selection: a third-party provider stayed checked after choosing OpenAI / ChatGPT.");
                if (form.SelectedProvider() != ProviderKind.OpenAi)
                    throw new Exception("Provider selection: the tool would not apply OpenAI / ChatGPT.");
                if (!form.keyTextBox.Enabled)
                    throw new Exception("Key management must remain editable while OpenAI / ChatGPT is selected.");
                if (!form.kimiKeyTextBox.Enabled)
                    throw new Exception("Kimi key management must remain editable while OpenAI / ChatGPT is selected.");

                form.kimiRadio.Checked = true;
                if (form.openAiRadio.Checked || form.deepSeekRadio.Checked ||
                    form.SelectedProvider() != ProviderKind.Kimi)
                    throw new Exception("Provider selection: switching to Kimi failed.");

                form.SelectProvider(ProviderKind.OpenAi);
                if (form.deepSeekRadio.Checked || form.kimiRadio.Checked ||
                    form.SelectedProvider() != ProviderKind.OpenAi)
                    throw new Exception("Provider selection: restoring the detected provider failed.");

                form.SelectProvider(ProviderKind.DeepSeek);
                if (!form.deepSeekRadio.Checked || form.SelectedProvider() != ProviderKind.DeepSeek)
                    throw new Exception("Provider selection: applying the DeepSeek state failed.");
                if (!form.keyTextBox.Enabled)
                    throw new Exception("Key management must remain editable while DeepSeek is selected.");

                form.SelectProvider(ProviderKind.Kimi);
                if (!form.kimiRadio.Checked || form.SelectedProvider() != ProviderKind.Kimi)
                    throw new Exception("Provider selection: applying the Kimi state failed.");
            }
        }

        // Builds every window (main, 设置, 诊断与日志) so a broken layout or a missing control is
        // caught by the self-test instead of by the user. Nothing is shown, so no config is touched.
        internal static void RunFormSmokeSelfTest()
        {
            SwitcherEngine engine = new SwitcherEngine();
            using (SwitcherForm main = new SwitcherForm())
            using (SettingsForm settings = new SettingsForm(engine))
            using (DiagnosticsForm diagnostics = new DiagnosticsForm(engine))
            {
                if (main.Text.Length == 0 || settings.Text.Length == 0 || diagnostics.Text.Length == 0)
                    throw new Exception("A window was created without a title.");
                if (main.Controls.Count == 0 || settings.Controls.Count == 0 || diagnostics.Controls.Count == 0)
                    throw new Exception("A window was created without any controls.");

                // Resize regression guard: the main window must grow and shrink freely (no MaximumSize
                // cap) and the layout must survive both a very wide and a minimum-size window.
                if (main.MaximumSize.Width != 0 || main.MaximumSize.Height != 0)
                    throw new Exception("The main window has a maximum size; free resizing is blocked.");
                main.Size = new Size(1600, 1100);
                main.PerformLayout();
                if (main.MinimumSize.Width < 700 || main.MinimumSize.Height < 600)
                    throw new Exception("The main window minimum size is too small for its content.");
                main.Size = main.MinimumSize;
                main.PerformLayout();
            }
        }

        private void BuildUi()
        {
            Panel header = new HeroPanel();
            header.Dock = DockStyle.Top;
            header.Height = 142;
            header.BackColor = Theme.Background;
            header.Padding = new Padding(28, 20, 28, 14);
            Controls.Add(header);

            Button settingsButton = new TechButton();
            settingsButton.Text = "⚙  设置";
            settingsButton.ForeColor = Color.White;
            settingsButton.BackColor = Color.FromArgb(48, 57, 73);
            settingsButton.FlatStyle = FlatStyle.Flat;
            settingsButton.FlatAppearance.BorderColor = Color.FromArgb(83, 94, 116);
            settingsButton.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            settingsButton.Size = new Size(100, 36);
            settingsButton.Location = new Point(ClientSize.Width - 128, 96);
            settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsButton.Cursor = Cursors.Hand;
            settingsButton.Click += delegate { ShowSettings(); };
            header.Controls.Add(settingsButton);

            Button diagnosticsButton = new TechButton();
            diagnosticsButton.Text = "诊断与日志";
            diagnosticsButton.ForeColor = Color.White;
            diagnosticsButton.BackColor = Color.FromArgb(48, 57, 73);
            diagnosticsButton.FlatStyle = FlatStyle.Flat;
            diagnosticsButton.FlatAppearance.BorderColor = Color.FromArgb(83, 94, 116);
            diagnosticsButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            diagnosticsButton.Size = new Size(110, 36);
            diagnosticsButton.Location = new Point(ClientSize.Width - 248, 96);
            diagnosticsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            diagnosticsButton.Cursor = Cursors.Hand;
            diagnosticsButton.Click += delegate { ShowDiagnostics(); };
            header.Controls.Add(diagnosticsButton);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.AutoScroll = true;
            body.Padding = new Padding(28, 18, 28, 20);
            Controls.Add(body);
            body.BringToFront();

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.ColumnCount = 1;
            layout.RowCount = 7;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.Controls.Add(layout);

            Panel statusCard = MakeCard(94);
            layout.Controls.Add(statusCard);
            TableLayoutPanel statusGrid = new TableLayoutPanel();
            statusGrid.Dock = DockStyle.Fill;
            statusGrid.ColumnCount = 4;
            statusGrid.RowCount = 2;
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusCard.Controls.Add(statusGrid);
            AddStatusCell(statusGrid, 0, "当前提供商", out statusProvider);
            AddStatusCell(statusGrid, 1, "当前模型", out statusModel);
            AddStatusCell(statusGrid, 2, "DeepSeek 密钥", out statusKey);
            AddStatusCell(statusGrid, 3, "Kimi 密钥", out statusKimiKey);

            Label chooseLabel = MakeSectionLabel("选择要使用的服务");
            chooseLabel.Margin = new Padding(0, 18, 0, 8);
            layout.Controls.Add(chooseLabel);

            TableLayoutPanel providers = new TableLayoutPanel();
            providers.Height = 106;
            providers.Dock = DockStyle.Top;
            providers.ColumnCount = 3;
            providers.RowCount = 1;
            providers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            providers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            providers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            providers.Margin = new Padding(0);
            layout.Controls.Add(providers);

            Panel openCard = MakeProviderCard("ChatGPT", "", Accent,
                "mark-chatgpt-256.png", out openAiRadio);
            openCard.Margin = new Padding(0, 0, 8, 0);
            providers.Controls.Add(openCard, 0, 0);
            Panel deepCard = MakeProviderCard("DeepSeek", "需要 DeepSeek API", DeepSeekBlue,
                "mark-deepseek-256.png", out deepSeekRadio);
            deepCard.Margin = new Padding(5, 0, 5, 0);
            providers.Controls.Add(deepCard, 1, 0);
            Panel kimiCard = MakeProviderCard("Kimi", "需要 Kimi API", KimiViolet,
                "mark-kimi-256.png", out kimiRadio);
            kimiCard.Margin = new Padding(8, 0, 0, 0);
            providers.Controls.Add(kimiCard, 2, 0);
            openAiRadio.CheckedChanged += ProviderChanged;
            deepSeekRadio.CheckedChanged += ProviderChanged;
            kimiRadio.CheckedChanged += ProviderChanged;

            Panel optionsCard = MakeCard(190);
            optionsCard.Margin = new Padding(0, 16, 0, 0);
            layout.Controls.Add(optionsCard);

            modelLabel = new Label();
            modelLabel.Text = "模型";
            modelLabel.ForeColor = Ink;
            modelLabel.Font = new Font(Font, FontStyle.Bold);
            modelLabel.AutoSize = true;
            modelLabel.Location = new Point(20, 18);
            optionsCard.Controls.Add(modelLabel);

            modelCombo = new ComboBox();
            modelCombo.DropDownStyle = ComboBoxStyle.DropDown;
            modelCombo.Location = new Point(20, 43);
            modelCombo.Width = 640;
            modelCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            optionsCard.Controls.Add(modelCombo);
            deepSeekModeCombo = new ComboBox();
            deepSeekModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            deepSeekModeCombo.Location = new Point(420, 43);
            deepSeekModeCombo.Size = new Size(240, 28);
            deepSeekModeCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            deepSeekModeCombo.Items.AddRange(new object[] {
                new ModelOption("非思考（直接回答）", "none"),
                new ModelOption("轻量思考（快速）", "low"),
                new ModelOption("标准思考", "high"),
                new ModelOption("深度思考", "max") });
            deepSeekModeCombo.SelectedIndex = 1;
            deepSeekModeCombo.Visible = false;
            optionsCard.Controls.Add(deepSeekModeCombo);


            KeyTabs keyTabs = new KeyTabs();
            keyTabs.Location = new Point(20, 78);
            keyTabs.Size = new Size(640, 102);
            keyTabs.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            optionsCard.Controls.Add(keyTabs);
            optionsCard.Layout += delegate { keyTabs.Width = optionsCard.ClientSize.Width - 2 * keyTabs.Left; deepSeekModeCombo.Left = optionsCard.ClientSize.Width - modelCombo.Left - deepSeekModeCombo.Width; modelCombo.Width = deepSeekModeCombo.Visible ? deepSeekModeCombo.Left - modelCombo.Left - 12 : optionsCard.ClientSize.Width - 2 * modelCombo.Left; };

            Panel deepSeekKeyPage = new Panel();
            deepSeekKeyPage.BackColor = Color.White;
            keyTabs.AddPage("DeepSeek 密钥", deepSeekKeyPage);

            keyTextBox = new TextBox();
            keyTextBox.UseSystemPasswordChar = true;
            keyTextBox.Location = new Point(8, 10);
            keyTextBox.Width = 385;
            keyTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            deepSeekKeyPage.Controls.Add(keyTextBox);
            NativeMethods.SetCueText(keyTextBox, "在此粘贴新 Key；留空不会覆盖已保存密钥");

            saveKeyButton = new TechButton();
            saveKeyButton.Text = "加密保存";
            saveKeyButton.FlatStyle = FlatStyle.Flat;
            saveKeyButton.FlatAppearance.BorderColor = Color.FromArgb(205, 211, 221);
            saveKeyButton.BackColor = Color.White;
            saveKeyButton.ForeColor = Ink;
            saveKeyButton.Location = new Point(405, 8);
            saveKeyButton.Size = new Size(98, 31);
            saveKeyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveKeyButton.Click += delegate { SaveKeyFromBox(true); };
            deepSeekKeyPage.Controls.Add(saveKeyButton);

            deleteKeyButton = new TechButton();
            deleteKeyButton.Text = "删除密钥";
            deleteKeyButton.FlatStyle = FlatStyle.Flat;
            deleteKeyButton.FlatAppearance.BorderColor = Color.FromArgb(205, 211, 221);
            deleteKeyButton.BackColor = Color.White;
            deleteKeyButton.ForeColor = Color.FromArgb(170, 55, 55);
            deleteKeyButton.Location = new Point(511, 8);
            deleteKeyButton.Size = new Size(102, 31);
            deleteKeyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            deleteKeyButton.Click += delegate { DeleteSavedKey(); };
            deepSeekKeyPage.Controls.Add(deleteKeyButton);

            showKeyCheckBox = new CheckBox();
            showKeyCheckBox.Text = "显示本次输入";
            showKeyCheckBox.ForeColor = Muted;
            showKeyCheckBox.AutoSize = true;
            showKeyCheckBox.Location = new Point(8, 46);
            showKeyCheckBox.CheckedChanged += delegate
            {
                keyTextBox.UseSystemPasswordChar = !showKeyCheckBox.Checked;
            };
            deepSeekKeyPage.Controls.Add(showKeyCheckBox);
            deepSeekKeyPage.Layout += delegate { LayoutKeys(deepSeekKeyPage, keyTextBox, saveKeyButton, deleteKeyButton); };

            Panel kimiKeyPage = new Panel();
            kimiKeyPage.BackColor = Color.White;
            keyTabs.AddPage("Kimi 密钥", kimiKeyPage);

            kimiKeyTextBox = new TextBox();
            kimiKeyTextBox.UseSystemPasswordChar = true;
            kimiKeyTextBox.Location = new Point(8, 10);
            kimiKeyTextBox.Width = 385;
            kimiKeyTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            kimiKeyPage.Controls.Add(kimiKeyTextBox);
            NativeMethods.SetCueText(kimiKeyTextBox, "粘贴所选模型对应平台的 Key；留空不会覆盖已保存密钥");

            saveKimiKeyButton = new TechButton();
            saveKimiKeyButton.Text = "加密保存";
            saveKimiKeyButton.FlatStyle = FlatStyle.Flat;
            saveKimiKeyButton.FlatAppearance.BorderColor = Color.FromArgb(205, 211, 221);
            saveKimiKeyButton.BackColor = Color.White;
            saveKimiKeyButton.ForeColor = Ink;
            saveKimiKeyButton.Location = new Point(405, 8);
            saveKimiKeyButton.Size = new Size(98, 31);
            saveKimiKeyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveKimiKeyButton.Click += delegate { SaveKimiKeyFromBox(true); };
            kimiKeyPage.Controls.Add(saveKimiKeyButton);

            deleteKimiKeyButton = new TechButton();
            deleteKimiKeyButton.Text = "删除密钥";
            deleteKimiKeyButton.FlatStyle = FlatStyle.Flat;
            deleteKimiKeyButton.FlatAppearance.BorderColor = Color.FromArgb(205, 211, 221);
            deleteKimiKeyButton.BackColor = Color.White;
            deleteKimiKeyButton.ForeColor = Color.FromArgb(170, 55, 55);
            deleteKimiKeyButton.Location = new Point(511, 8);
            deleteKimiKeyButton.Size = new Size(102, 31);
            deleteKimiKeyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            deleteKimiKeyButton.Click += delegate { DeleteSavedKimiKey(); };
            kimiKeyPage.Controls.Add(deleteKimiKeyButton);

            showKimiKeyCheckBox = new CheckBox();
            showKimiKeyCheckBox.Text = "显示本次输入";
            showKimiKeyCheckBox.ForeColor = Muted;
            showKimiKeyCheckBox.AutoSize = true;
            showKimiKeyCheckBox.Location = new Point(8, 46);
            showKimiKeyCheckBox.CheckedChanged += delegate
            {
                kimiKeyTextBox.UseSystemPasswordChar = !showKimiKeyCheckBox.Checked;
            };
            kimiKeyPage.Controls.Add(showKimiKeyCheckBox);
            kimiKeyPage.Layout += delegate { LayoutKeys(kimiKeyPage, kimiKeyTextBox, saveKimiKeyButton, deleteKimiKeyButton); };

            messageLabel = new Label();
            messageLabel.Text = string.Empty;
            messageLabel.ForeColor = Muted;
            messageLabel.BackColor = Color.Transparent;
            messageLabel.AutoSize = true;
            messageLabel.Margin = new Padding(0, 14, 0, 8);
            layout.Controls.Add(messageLabel);
            activity = new ActivityBar { Height = 4, Dock = DockStyle.Top, Visible = false, Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(activity);

            TableLayoutPanel actions = new TableLayoutPanel();
            actions.Height = 46;
            actions.Dock = DockStyle.Top;
            actions.ColumnCount = 2;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            actions.Margin = new Padding(0);
            layout.Controls.Add(actions);

            applyButton = MakeActionButton("只保存配置", Color.White, Ink, Color.FromArgb(199, 207, 219));
            applyButton.Margin = new Padding(0, 0, 8, 0);
            applyButton.Click += delegate { ApplySelection(false); };
            actions.Controls.Add(applyButton, 0, 0);

            restartButton = MakeActionButton("切换并重启 ChatGPT", Accent, Color.White, Accent);
            restartButton.Margin = new Padding(8, 0, 0, 0);
            restartButton.Click += delegate { ApplySelection(true); };
            actions.Controls.Add(restartButton, 1, 0);

        }

        private static void LayoutKeys(Panel page, TextBox input, Button save, Button delete)
        {
            delete.Left = page.ClientSize.Width - delete.Width - 10;
            save.Left = delete.Left - save.Width - 8;
            input.Width = Math.Max(100, save.Left - 18);
        }

        private static Panel MakeCard(int height)
        {
            Panel panel = new RoundPanel();
            panel.Height = height;
            panel.Dock = DockStyle.Top;
            panel.BackColor = Color.White;
            panel.BorderStyle = BorderStyle.None;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(18);
            return panel;
        }

        private static Label MakeSectionLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Ink;
            label.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            label.AutoSize = true;
            return label;
        }

        private static void AddStatusCell(TableLayoutPanel grid, int column, string caption, out Label value)
        {
            Panel cell = new Panel();
            cell.Dock = DockStyle.Fill;
            Label cap = new Label();
            cap.Text = caption;
            cap.ForeColor = Muted;
            cap.AutoSize = true;
            cap.Location = new Point(2, 2);
            cell.Controls.Add(cap);
            value = new Label();
            value.Text = "读取中…";
            value.ForeColor = Ink;
            value.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            value.AutoEllipsis = true;
            value.Location = new Point(2, 29);
            value.Size = new Size(190, 25);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cell.Controls.Add(value);
            grid.Controls.Add(cell, column, 0);
            grid.SetRowSpan(cell, 2);
        }

        private static Panel MakeProviderCard(string title, string description, Color color, string markAsset, out RadioButton radio)
        {
            Panel panel = MakeCard(106);
            panel.Padding = new Padding(16);

            // Service marks use the same circular blue treatment as the header artwork.
            Control mark = new ProviderMark { Text = title.StartsWith("ChatGPT") ? "C" : title.StartsWith("DeepSeek") ? "D" : "K", Location = new Point(16, 17), Size = new Size(44, 44) };
            panel.Controls.Add(mark);

            radio = new RadioButton();
            radio.Text = title;
            radio.ForeColor = Ink;
            radio.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            radio.AutoSize = false;
            radio.Size = new Size(175, 28);
            radio.Location = new Point(66, 15);
            panel.Controls.Add(radio);

            Label desc = new Label();
            desc.Text = description;
            desc.ForeColor = Muted;
            desc.AutoSize = false;
            desc.Location = new Point(20, 68);
            desc.Size = new Size(205, 32);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(desc);

            RadioButton clickTarget = radio;
            panel.Cursor = Cursors.Hand;
            clickTarget.CheckedChanged += delegate { ((RoundPanel)panel).Selected = clickTarget.Checked; panel.Invalidate(); };
            panel.Click += delegate { if (clickTarget.Enabled) clickTarget.Checked = true; };
            mark.Click += delegate { if (clickTarget.Enabled) clickTarget.Checked = true; };
            desc.Click += delegate { if (clickTarget.Enabled) clickTarget.Checked = true; };
            return panel;
        }

        private static Button MakeActionButton(string text, Color back, Color fore, Color border)
        {
            Button button = new TechButton();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.BackColor = back;
            button.ForeColor = fore;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = border;
            button.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static Button MakeLinkButton(string text)
        {
            Button button = new TechButton();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = WindowColor;
            button.ForeColor = Accent;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void InitializeState()
        {
            try
            {
                engine.EnsureBaseline();
                engine.MigrateLegacyKeys();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                ShowError("初始化失败", ex);
            }
        }

        private void RefreshStatus()
        {
            try
            {
                CurrentConfiguration current = engine.ReadCurrentConfiguration();
                statusProvider.Text = ProviderDisplayName(current.Provider);
                statusProvider.ForeColor = current.Provider == ProviderKind.DeepSeek ? DeepSeekBlue :
                    (current.Provider == ProviderKind.Kimi ? KimiViolet : Good);
                statusModel.Text = string.IsNullOrEmpty(current.Model) ? "应用默认" : current.Model;
                bool hasKey = engine.HasDeepSeekKey();
                statusKey.Text = hasKey ? "已加密保存 ••••••" : "尚未配置";
                statusKey.ForeColor = hasKey ? Good : Color.FromArgb(191, 99, 0);
                bool hasKimiKey = engine.HasKimiKey();
                statusKimiKey.Text = hasKimiKey ? "已加密保存 ••••••" : "尚未配置";
                statusKimiKey.ForeColor = hasKimiKey ? Good : Color.FromArgb(191, 99, 0);
                if (current.Provider == ProviderKind.DeepSeek)
                    engine.DeepSeekReasoningEffort = SwitcherEngine.NormalizeDeepSeekEffort(current.ReasoningEffort);
                for (int i = 0; i < deepSeekModeCombo.Items.Count; i++)
                    if (((ModelOption)deepSeekModeCombo.Items[i]).Slug == engine.DeepSeekReasoningEffort)
                        deepSeekModeCombo.SelectedIndex = i;
                SelectProvider(current.Provider);
                RefreshModelChoices(current.Model);
                messageLabel.Text = string.Empty;
                messageLabel.ForeColor = Muted;
            }
            catch (Exception ex)
            {
                statusProvider.Text = "读取失败";
                statusModel.Text = "—";
                statusKey.Text = "—";
                statusKimiKey.Text = "—";
                messageLabel.Text = ex.Message;
                messageLabel.ForeColor = Color.Firebrick;
            }
        }

        private void RefreshModelChoices(string preferredSlug)
        {
            ProviderKind provider = SelectedProvider();
            string preserve = preferredSlug;
            bool providerChanged = lastModelListProvider.HasValue && lastModelListProvider.Value != provider;
            if (preserve == null) preserve = providerChanged ? string.Empty : GetSelectedModelSlug();
            List<ModelOption> options = engine.GetModelOptions(provider);
            modelCombo.BeginUpdate();
            try
            {
                modelCombo.Items.Clear();
                foreach (ModelOption option in options) modelCombo.Items.Add(option);
                int selectedIndex = -1;
                for (int i = 0; i < modelCombo.Items.Count; i++)
                {
                    ModelOption option = modelCombo.Items[i] as ModelOption;
                    if (option != null && string.Equals(option.Slug, preserve, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                if (selectedIndex >= 0) modelCombo.SelectedIndex = selectedIndex;
                else if (!string.IsNullOrWhiteSpace(preserve) && provider == ProviderKind.OpenAi) modelCombo.Text = preserve;
                else if (modelCombo.Items.Count > 0) modelCombo.SelectedIndex = 0;
            }
            finally { modelCombo.EndUpdate(); }
            lastModelListProvider = provider;
        }

        private string GetSelectedModelSlug()
        {
            ModelOption selected = modelCombo.SelectedItem as ModelOption;
            if (selected != null) return selected.Slug;
            string typed = modelCombo.Text.Trim();
            int separator = typed.LastIndexOf("  [", StringComparison.Ordinal);
            if (separator >= 0 && typed.EndsWith("]", StringComparison.Ordinal))
                typed = typed.Substring(separator + 3, typed.Length - separator - 4);
            return typed;
        }

        // The two provider cards keep their radio button inside a separate panel, and WinForms
        // only auto-clears radio buttons that share one parent. Without this explicit handling,
        // picking "ChatGPT" after DeepSeek left both radios checked and the tool kept
        // re-applying DeepSeek, so it was impossible to switch back to ChatGPT.
        private void ProviderChanged(object sender, EventArgs e)
        {
            RadioButton source = sender as RadioButton;
            if (source != null && source.Checked && !syncingProviderSelection)
            {
                syncingProviderSelection = true;
                try
                {
                    foreach (RadioButton radio in new RadioButton[] { openAiRadio, deepSeekRadio, kimiRadio })
                        if (radio != null && !ReferenceEquals(radio, source)) radio.Checked = false;
                }
                finally { syncingProviderSelection = false; }
            }
            UpdateProviderControls();
        }

        private void SelectProvider(ProviderKind provider)
        {
            syncingProviderSelection = true;
            try
            {
                openAiRadio.Checked = provider == ProviderKind.OpenAi;
                deepSeekRadio.Checked = provider == ProviderKind.DeepSeek;
                kimiRadio.Checked = provider == ProviderKind.Kimi;
            }
            finally
            {
                syncingProviderSelection = false;
            }
            UpdateProviderControls();
        }

        private void UpdateProviderControls()
        {
            if (modelCombo == null || keyTextBox == null || restartButton == null) return;
            ProviderKind provider = SelectedProvider();
            modelCombo.Enabled = true;
            deepSeekModeCombo.Visible = provider == ProviderKind.DeepSeek;
            modelCombo.Parent.PerformLayout();
            // Key management is deliberately independent from the provider radio buttons.
            // A user can prepare, replace or remove a DeepSeek key while OpenAI is active.
            keyTextBox.Enabled = true;
            if (saveKeyButton != null) saveKeyButton.Enabled = true;
            if (deleteKeyButton != null) deleteKeyButton.Enabled = engine.HasDeepSeekKey();
            if (showKeyCheckBox != null) showKeyCheckBox.Enabled = true;
            if (kimiKeyTextBox != null) kimiKeyTextBox.Enabled = true;
            if (saveKimiKeyButton != null) saveKimiKeyButton.Enabled = true;
            if (deleteKimiKeyButton != null) deleteKimiKeyButton.Enabled = engine.HasKimiKey();
            if (showKimiKeyCheckBox != null) showKimiKeyCheckBox.Enabled = true;
            modelLabel.Text = provider == ProviderKind.DeepSeek ? "DeepSeek 模型（可输入模型 ID）" :
                (provider == ProviderKind.Kimi ? "Kimi 模型（开放平台与 Code 密钥不通用）" :
                "OpenAI 模型（自动读取 Codex 本地目录，也可输入模型 ID）");
            restartButton.BackColor = provider == ProviderKind.DeepSeek ? DeepSeekBlue :
                (provider == ProviderKind.Kimi ? KimiViolet : Accent);
            restartButton.FlatAppearance.BorderColor = restartButton.BackColor;
            if (!lastModelListProvider.HasValue || lastModelListProvider.Value != provider)
                RefreshModelChoices(null);
        }

        internal ProviderKind SelectedProvider()
        {
            if (kimiRadio != null && kimiRadio.Checked && !openAiRadio.Checked && !deepSeekRadio.Checked)
                return ProviderKind.Kimi;
            if (deepSeekRadio.Checked && !openAiRadio.Checked && (kimiRadio == null || !kimiRadio.Checked))
                return ProviderKind.DeepSeek;
            return ProviderKind.OpenAi;
        }

        private static string ProviderDisplayName(ProviderKind provider)
        {
            return provider == ProviderKind.DeepSeek ? "DeepSeek" :
                (provider == ProviderKind.Kimi ? "Kimi" : "ChatGPT");
        }

        private bool SaveKeyFromBox(bool showConfirmation)
        {
            string value = keyTextBox.Text.Trim();
            if (value.Length == 0)
            {
                if (showConfirmation)
                {
                    ShowError("保存密钥失败", new AppIssueException("DSK-401",
                        "尚未输入 DeepSeek API Key。", "粘贴有效的 DeepSeek API Key 后点击“加密保存”。"));
                }
                return false;
            }
            if (!value.StartsWith("sk-", StringComparison.Ordinal) || value.Length < 20)
            {
                ShowError("保存密钥失败", new AppIssueException("DSK-402",
                    "API Key 格式看起来不正确。", "请使用 DeepSeek 平台新生成、以 sk- 开头的完整 API Key。"));
                return false;
            }

            try
            {
                engine.SaveDeepSeekKey(value);
                keyTextBox.Clear();
                if (showKeyCheckBox != null) showKeyCheckBox.Checked = false;
                statusKey.Text = "已加密保存 ••••••";
                statusKey.ForeColor = Good;
                if (deleteKeyButton != null) deleteKeyButton.Enabled = true;
                messageLabel.Text = string.Empty;
                if (showConfirmation)
                {
                    MessageBox.Show("保存成功", "保存成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return true;
            }
            catch (Exception ex)
            {
                ShowError("保存密钥失败", ex);
                return false;
            }
        }

        private void DeleteSavedKey()
        {
            if (!engine.HasDeepSeekKey())
            {
                MessageBox.Show("目前没有已保存的 DeepSeek 密钥。", "删除密钥",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult answer = MessageBox.Show(
                "将删除当前 Windows 用户加密保存的 DeepSeek 密钥，并清除 DEEPSEEK_API_KEY 环境变量。\r\n\r\n删除后，如需使用 DeepSeek，必须重新输入 API Key。",
                "确认删除密钥", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            try
            {
                engine.DeleteDeepSeekKey();
                keyTextBox.Clear();
                if (showKeyCheckBox != null) showKeyCheckBox.Checked = false;
                statusKey.Text = "尚未配置";
                statusKey.ForeColor = Color.FromArgb(191, 99, 0);
                deleteKeyButton.Enabled = false;
                messageLabel.Text = string.Empty;
            }
            catch (Exception ex)
            {
                ShowError("删除密钥失败", ex);
            }
        }

        private bool SaveKimiKeyFromBox(bool showConfirmation)
        {
            string value = kimiKeyTextBox.Text.Trim();
            if (value.Length == 0)
            {
                if (showConfirmation)
                    ShowError("保存 Kimi 密钥失败", new AppIssueException("KIM-401",
                        "尚未输入 Kimi API Key。", "粘贴 所选模型对应平台生成的完整 API Key 后点击“加密保存”。"));
                return false;
            }
            if (!value.StartsWith("sk-", StringComparison.Ordinal) || value.Length < 20)
            {
                ShowError("保存 Kimi 密钥失败", new AppIssueException("KIM-402",
                    "Kimi API Key 格式看起来不正确。", "请使用 所选模型对应平台新生成、以 sk- 开头的完整 API Key。"));
                return false;
            }

            try
            {
                engine.SaveKimiKey(value);
                kimiKeyTextBox.Clear();
                if (showKimiKeyCheckBox != null) showKimiKeyCheckBox.Checked = false;
                statusKimiKey.Text = "已加密保存 ••••••";
                statusKimiKey.ForeColor = Good;
                if (deleteKimiKeyButton != null) deleteKimiKeyButton.Enabled = true;
                messageLabel.Text = string.Empty;
                if (showConfirmation)
                    MessageBox.Show("保存成功", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                ShowError("保存 Kimi 密钥失败", ex);
                return false;
            }
        }

        private void DeleteSavedKimiKey()
        {
            if (!engine.HasKimiKey())
            {
                MessageBox.Show("目前没有已保存的 Kimi 密钥。", "删除密钥",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult answer = MessageBox.Show(
                "将删除当前 Windows 用户加密保存的 Kimi Code 密钥，并清除 KIMI_API_KEY 环境变量。\r\n\r\n删除后，如需使用 Kimi，必须重新输入 API Key。",
                "确认删除密钥", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            try
            {
                engine.DeleteKimiKey();
                kimiKeyTextBox.Clear();
                if (showKimiKeyCheckBox != null) showKimiKeyCheckBox.Checked = false;
                statusKimiKey.Text = "尚未配置";
                statusKimiKey.ForeColor = Color.FromArgb(191, 99, 0);
                deleteKimiKeyButton.Enabled = false;
                messageLabel.Text = string.Empty;
            }
            catch (Exception ex)
            {
                ShowError("删除 Kimi 密钥失败", ex);
            }
        }

        private async void ApplySelection(bool restart)
        {
            try
            {
                ProviderKind provider = SelectedProvider();
                if (provider == ProviderKind.DeepSeek && keyTextBox.Text.Trim().Length > 0 && !SaveKeyFromBox(false)) return;
                if (provider == ProviderKind.Kimi && kimiKeyTextBox.Text.Trim().Length > 0 && !SaveKimiKeyFromBox(false)) return;
                if (provider == ProviderKind.DeepSeek && !engine.HasDeepSeekKey())
                {
                    ShowError("切换到 DeepSeek 失败", new AppIssueException("DSK-401",
                        "尚未保存 DeepSeek API Key。", "输入有效密钥并点击“加密保存”，也可以先在“诊断与日志”中运行 API 测试。"));
                    keyTextBox.Focus();
                    return;
                }
                if (provider == ProviderKind.Kimi && !engine.HasKimiKey())
                {
                    ShowError("切换到 Kimi 失败", new AppIssueException("KIM-401",
                        "尚未保存 Kimi API Key。", "输入有效密钥并点击“加密保存”，也可以先在“诊断与日志”中运行 Kimi API 测试。"));
                    kimiKeyTextBox.Focus();
                    return;
                }

                if (restart)
                {
                    DialogResult answer = MessageBox.Show(
                        "切换会关闭当前 ChatGPT/Codex 桌面应用，正在运行的任务会被中断，然后应用将自动重新打开。\r\n\r\n是否继续？",
                        "确认切换并重启", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes) return;
                }

                SetBusy(true);
                string model = GetSelectedModelSlug();
                if (provider == ProviderKind.DeepSeek)
                    engine.DeepSeekReasoningEffort = ((ModelOption)deepSeekModeCombo.SelectedItem).Slug;
                messageLabel.Text = "正在验证模型完整回复（最多 60 秒），通过后写入配置…";
                string backup = await System.Threading.Tasks.Task.Run(() => engine.Apply(provider, model));

                RefreshStatus();
                messageLabel.Text = "配置已保存。";
                messageLabel.ForeColor = Good;

                if (restart)
                {
                    messageLabel.Text = "正在重启 ChatGPT…";
                    RestartResult result = await System.Threading.Tasks.Task.Run(() => engine.RestartChatGpt());
                    if (!result.Launched)
                    {
                        ShowError("ChatGPT 自动启动失败", new AppIssueException("OAI-301",
                            "配置已经切换，但系统没有找到可用的 ChatGPT 启动入口。" + result.Message,
                            "请手动启动 ChatGPT，或在设置中自动检测/手工选择正确的 ChatGPT.exe。"));
                    }
                    else
                    {
                        messageLabel.Text = "切换完成，ChatGPT 正在重新启动。请在新建任务中验证模型。";
                    }
                }
                else
                {
                    MessageBox.Show("配置已保存。要让正在运行的桌面应用读取新配置，请点击“切换并重启 ChatGPT”。",
                        "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                ShowError("切换失败，原配置未被覆盖", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RestoreLatest()
        {
            string latest = engine.GetLatestBackup();
            if (string.IsNullOrEmpty(latest))
            {
                MessageBox.Show("还没有可恢复的备份。", "恢复备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult answer = MessageBox.Show("将恢复最近的配置备份：\r\n" + latest + "\r\n\r\n是否继续？",
                "恢复最近备份", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            try
            {
                engine.RestoreBackup(latest);
                RefreshStatus();
                MessageBox.Show("最近备份已恢复。请重启 ChatGPT 使其生效。", "恢复成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError("恢复失败", ex);
            }
        }

        private void ShowSettings()
        {
            using (SettingsForm settings = new SettingsForm(engine))
            {
                settings.ShowDialog(this);
            }
            lastModelListProvider = null;
            RefreshStatus();
        }

        private void ShowDiagnostics()
        {
            using (DiagnosticsForm diagnostics = new DiagnosticsForm(engine))
                diagnostics.ShowDialog(this);
            lastModelListProvider = null;
            RefreshStatus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (activity != null && activity.Visible) e.Cancel = true;
            base.OnFormClosing(e);
        }

        private void SetBusy(bool busy)
        {
            activity.Visible = busy;
            foreach (Control c in Controls) if (c is HeroPanel)
                foreach (Control child in c.Controls) if (child is Button) child.Enabled = !busy;
            openAiRadio.Enabled = deepSeekRadio.Enabled = kimiRadio.Enabled = !busy;
            deepSeekModeCombo.Enabled = !busy;
            modelCombo.Enabled = !busy;
            applyButton.Enabled = !busy;
            restartButton.Enabled = !busy;
            if (saveKeyButton != null) saveKeyButton.Enabled = !busy;
            if (deleteKeyButton != null) deleteKeyButton.Enabled = !busy && engine.HasDeepSeekKey();
            if (showKeyCheckBox != null) showKeyCheckBox.Enabled = !busy;
            if (keyTextBox != null) keyTextBox.Enabled = !busy;
            if (saveKimiKeyButton != null) saveKimiKeyButton.Enabled = !busy;
            if (deleteKimiKeyButton != null) deleteKimiKeyButton.Enabled = !busy && engine.HasKimiKey();
            if (showKimiKeyCheckBox != null) showKimiKeyCheckBox.Enabled = !busy;
            if (kimiKeyTextBox != null) kimiKeyTextBox.Enabled = !busy;
            UseWaitCursor = busy;
            Application.DoEvents();
        }

        private void ShowError(string title, Exception ex)
        {
            ErrorInfo info = ErrorReporter.Show(this, title, ex);
            messageLabel.Text = info.Code + "：" + info.Message;
            messageLabel.ForeColor = Color.Firebrick;
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly SwitcherEngine engine;
        private TextBox configPathBox;
        private TextBox launchPathBox;
        private Label configStatus;
        private Label modelStatus;

        public SettingsForm(SwitcherEngine engine)
        {
            this.engine = engine;
            Text = "设置 - Codex 模型切换器";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(740, 610);
            MinimumSize = new Size(700, 570);
            BackColor = Theme.Background;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildUi();
            Theme.Apply(this);
            RefreshSettingsState();
        }

        private void BuildUi()
        {
            Label title = new Label();
            title.Text = "设置";
            title.Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Location = new Point(26, 20);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "配置位置、模型目录、备份恢复与 ChatGPT 启动适配";
            subtitle.ForeColor = Theme.Muted;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(29, 58);
            Controls.Add(subtitle);

            GroupBox configGroup = MakeGroup("Codex 配置目录", 88, 172);
            Controls.Add(configGroup);

            configPathBox = new TextBox();
            configPathBox.Location = new Point(18, 31);
            configPathBox.Width = 650;
            configPathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            configPathBox.ReadOnly = true;
            configGroup.Controls.Add(configPathBox);

            Button autoFind = MakeButton("自动寻找", 18, 68, 110);
            autoFind.Click += delegate { AutoFindConfig(); };
            configGroup.Controls.Add(autoFind);
            Button browse = MakeButton("手动选择 config.toml", 138, 68, 174);
            browse.Click += delegate { BrowseConfig(); };
            configGroup.Controls.Add(browse);


            configStatus = new Label();
            configStatus.Location = new Point(18, 112);
            configStatus.Size = new Size(650, 40);
            configStatus.ForeColor = Theme.Muted;
            configStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            configGroup.Controls.Add(configStatus);

            GroupBox maintenance = MakeGroup("状态与维护", 270, 116);
            Controls.Add(maintenance);
            Button refresh = MakeButton("刷新状态和模型", 18, 34, 140);
            refresh.Click += delegate { RefreshModels(); };
            maintenance.Controls.Add(refresh);
            Button openConfig = MakeButton("打开配置目录", 168, 34, 130);
            openConfig.Click += delegate { SafeAction("打开配置目录失败", engine.OpenCodexDirectory); };
            maintenance.Controls.Add(openConfig);
            Button openBackups = MakeButton("打开备份目录", 308, 34, 130);
            openBackups.Click += delegate { SafeAction("打开备份目录失败", engine.OpenBackupDirectory); };
            maintenance.Controls.Add(openBackups);
            Button restore = MakeButton("恢复最近备份", 448, 34, 130);
            restore.Click += delegate { RestoreLatest(); };
            maintenance.Controls.Add(restore);

            modelStatus = new Label();
            modelStatus.Location = new Point(18, 76);
            modelStatus.Size = new Size(650, 25);
            modelStatus.ForeColor = Theme.Muted;
            maintenance.Controls.Add(modelStatus);

            GroupBox launchGroup = MakeGroup("ChatGPT 启动适配", 396, 112);
            Controls.Add(launchGroup);
            launchPathBox = new TextBox();
            launchPathBox.Location = new Point(18, 31);
            launchPathBox.Width = 500;
            launchPathBox.ReadOnly = true;
            launchPathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            launchGroup.Controls.Add(launchPathBox);
            Button detect = MakeButton("自动检测", 528, 29, 110);
            detect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            detect.Click += delegate { DetectChatGpt(); };
            launchGroup.Controls.Add(detect);
            Button manual = MakeButton("手动选择 ChatGPT.exe", 18, 68, 190);
            manual.Click += delegate { BrowseChatGpt(); };
            launchGroup.Controls.Add(manual);
            Button diagnostics = MakeButton("运行诊断", 218, 68, 110);
            diagnostics.Click += delegate
            {
                using (DiagnosticsForm form = new DiagnosticsForm(engine)) form.ShowDialog(this);
            };
            launchGroup.Controls.Add(diagnostics);
            Button openLogs = MakeButton("打开日志目录", 338, 68, 130);
            openLogs.Click += delegate { SafeAction("打开日志目录失败", engine.OpenLogsDirectory); };
            launchGroup.Controls.Add(openLogs);

            Button close = MakeButton("完成", 590, 526, 110);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.BackColor = Color.FromArgb(38, 99, 235);
            close.ForeColor = Color.White;
            close.FlatAppearance.BorderColor = close.BackColor;
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Label credit = new Label();
            credit.Text = "作者声明：本软件由「请教我周帅帅」制作，免费分享，仅供个人方便使用；" +
                "使用本软件（包括切换模型提供商、修改配置）所产生的任何后果由使用者自行承担。";
            credit.ForeColor = Theme.Muted;
            credit.Font = new Font("Microsoft YaHei UI", 8.5F);
            credit.AutoSize = false;
            credit.Location = new Point(24, 520);
            credit.Size = new Size(ClientSize.Width - 180, 40);
            credit.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(credit);
        }

        private GroupBox MakeGroup(string text, int top, int height)
        {
            GroupBox group = new TechGroupBox();
            group.Text = text;
            group.Location = new Point(26, top);
            group.Size = new Size(ClientSize.Width - 52, height);
            group.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            group.BackColor = Color.White;
            group.ForeColor = Theme.Text;
            return group;
        }

        private static Button MakeButton(string text, int left, int top, int width)
        {
            Button button = new TechButton();
            button.Text = text;
            button.Location = new Point(left, top);
            button.Size = new Size(width, 31);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(199, 207, 219);
            button.BackColor = Color.White;
            button.ForeColor = Theme.Text;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void RefreshSettingsState()
        {
            configPathBox.Text = engine.ConfigPath;
            launchPathBox.Text = engine.GetLaunchDescription();
            try
            {
                CurrentConfiguration current = engine.ReadCurrentConfiguration();
                configStatus.Text = "有效配置；当前提供商：" + (current.Provider == ProviderKind.DeepSeek ? "DeepSeek" :
                    (current.Provider == ProviderKind.Kimi ? "Kimi" : "ChatGPT")) +
                    "；模型：" + (string.IsNullOrEmpty(current.Model) ? "跟随应用默认" : current.Model);
                configStatus.ForeColor = Theme.Blue;
                List<ModelOption> openAi = engine.GetModelOptions(ProviderKind.OpenAi);
                List<ModelOption> deepSeek = engine.GetModelOptions(ProviderKind.DeepSeek);
                List<ModelOption> kimi = engine.GetModelOptions(ProviderKind.Kimi);
                modelStatus.Text = "已发现 OpenAI 模型 " + Math.Max(0, openAi.Count - 1).ToString() +
                    " 个，DeepSeek 模型 " + deepSeek.Count.ToString() + " 个，Kimi 模型 " + kimi.Count.ToString() + " 个。";
            }
            catch (Exception ex)
            {
                configStatus.Text = "当前路径不可用：" + ex.Message;
                configStatus.ForeColor = Color.Firebrick;
                modelStatus.Text = "选择有效配置后即可读取模型目录。";
            }
        }

        private void AutoFindConfig()
        {
            try
            {
                List<string> found = engine.DiscoverCodexHomes();
                if (found.Count == 0)
                {
                    MessageBox.Show("没有自动找到 config.toml。请点击“手动选择 config.toml”。",
                        "自动寻找", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string selected = found[0];
                if (found.Count > 1)
                {
                    using (PathChoiceDialog dialog = new PathChoiceDialog(found))
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        selected = dialog.SelectedPath;
                    }
                }
                engine.SetCodexDirectory(selected);
                RefreshSettingsState();
            }
            catch (Exception ex) { ShowError("自动寻找失败", ex); }
        }

        private void BrowseConfig()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 Codex 用户级 config.toml";
                dialog.Filter = "Codex 配置 (config.toml)|config.toml|TOML 文件 (*.toml)|*.toml|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (Directory.Exists(engine.CodexDirectory)) dialog.InitialDirectory = engine.CodexDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                configPathBox.Text = dialog.FileName;
                SaveConfigPath();
            }
        }

        private void SaveConfigPath()
        {
            try
            {
                engine.SetCodexDirectory(configPathBox.Text.Trim());
                engine.EnsureBaseline();
                RefreshSettingsState();
            }
            catch (Exception ex) { ShowError("配置目录无效", ex); }
        }

        private void RefreshModels()
        {
            engine.ReloadConfigurationLocation();
            RefreshSettingsState();
            MessageBox.Show("状态与本地模型目录已经重新读取。", "刷新完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DetectChatGpt()
        {
            try
            {
                string result = engine.DetectChatGptLaunch();
                launchPathBox.Text = engine.GetLaunchDescription();
                MessageBox.Show(result, "启动适配", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ShowError("自动检测失败", ex); }
        }

        private void BrowseChatGpt()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 ChatGPT.exe";
                dialog.Filter = "Windows 应用程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    engine.SetManualChatGptExecutable(dialog.FileName);
                    launchPathBox.Text = engine.GetLaunchDescription();
                }
                catch (Exception ex) { ShowError("启动文件无效", ex); }
            }
        }

        private void RestoreLatest()
        {
            string latest = engine.GetLatestBackup();
            if (string.IsNullOrEmpty(latest))
            {
                MessageBox.Show("还没有可恢复的备份。", "恢复备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult answer = MessageBox.Show("将恢复最近的配置备份：\r\n" + latest +
                "\r\n\r\n恢复后需要重新启动 ChatGPT。是否继续？", "恢复最近备份",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            try
            {
                engine.RestoreBackup(latest);
                RefreshSettingsState();
                MessageBox.Show("最近备份已恢复。", "恢复成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ShowError("恢复失败", ex); }
        }

        private void SafeAction(string title, Action action)
        {
            try { action(); }
            catch (Exception ex) { ShowError(title, ex); }
        }

        private void ShowError(string title, Exception ex)
        {
            ErrorReporter.Show(this, title, ex);
        }
    }

    internal sealed class DiagnosticsForm : Form
    {
        private readonly SwitcherEngine engine;
        private readonly TextBox reportBox;
        private readonly Label reportPathLabel;
        private List<DiagnosticItem> items = new List<DiagnosticItem>();
        private string currentReport = string.Empty;
        private ActivityBar activity = new ActivityBar { Dock = DockStyle.Top, Height = 4, Visible = false };
        private void Busy(bool value) { activity.Visible = value; UseWaitCursor = value; foreach (Control c in Controls) if (c is Button) c.Enabled = !value; }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (activity.Visible) e.Cancel = true; base.OnFormClosing(e); }

        public DiagnosticsForm(SwitcherEngine engine)
        {
            this.engine = engine;
            Controls.Add(activity);
            Text = "诊断与日志 - Codex 模型切换器";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(820, 690);
            MinimumSize = new Size(740, 600);
            BackColor = Theme.Background;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Label title = new Label();
            title.Text = "系统诊断与错误报告";
            title.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(24, 18);
            Controls.Add(title);



            reportBox = new TextBox();
            reportBox.Multiline = true;
            reportBox.ReadOnly = true;
            reportBox.ScrollBars = ScrollBars.Both;
            reportBox.WordWrap = false;
            reportBox.Font = new Font("Consolas", 9F);
            reportBox.Location = new Point(26, 70);
            reportBox.Size = new Size(ClientSize.Width - 52, ClientSize.Height - 169);
            reportBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(reportBox);

            reportPathLabel = new Label();
            reportPathLabel.Text = "尚未生成报告";
            reportPathLabel.ForeColor = Theme.Muted;
            reportPathLabel.Location = new Point(27, ClientSize.Height - 91);
            reportPathLabel.Size = new Size(ClientSize.Width - 54, 22);
            reportPathLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(reportPathLabel);

            Button runLocal = MakeButton("运行本地诊断", 26, 0, 120);
            runLocal.Top = ClientSize.Height - 59;
            runLocal.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            runLocal.Click += delegate { RunLocal(); };
            Controls.Add(runLocal);

            Button testDeepSeek = MakeButton("测试 DeepSeek", 156, 0, 135);
            testDeepSeek.Top = ClientSize.Height - 59;
            testDeepSeek.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            testDeepSeek.Click += delegate { TestDeepSeek(); };
            Controls.Add(testDeepSeek);

            Button testKimi = MakeButton("测试 Kimi", 301, 0, 125);
            testKimi.Top = ClientSize.Height - 59;
            testKimi.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            testKimi.Click += delegate { TestKimi(); };
            Controls.Add(testKimi);

            Button copy = MakeButton("复制报告", 436, 0, 90);
            copy.Top = ClientSize.Height - 59;
            copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            copy.Click += delegate
            {
                if (!string.IsNullOrWhiteSpace(reportBox.Text))
                {
                    Clipboard.SetText(reportBox.Text);
                    MessageBox.Show("复制成功", "诊断报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            Controls.Add(copy);

            Button logs = MakeButton("打开日志", 536, 0, 115);
            logs.Top = ClientSize.Height - 59;
            logs.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            logs.Click += delegate
            {
                try { engine.OpenLogsDirectory(); }
                catch (Exception ex) { ErrorReporter.Show(this, "打开日志目录失败", ex); }
            };
            Controls.Add(logs);

            Button close = MakeButton("关闭", ClientSize.Width - 126, 0, 100);
            close.Top = ClientSize.Height - 59;
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Theme.Apply(this);
            Shown += delegate { RunLocal(); };
        }

        private static Button MakeButton(string text, int left, int top, int width)
        {
            Button button = new TechButton();
            button.Text = text;
            button.Location = new Point(left, top);
            button.Size = new Size(width, 32);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(199, 207, 219);
            button.BackColor = Color.White;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private async void RunLocal()
        {
            try
            {
                Busy(true);
                Application.DoEvents();
                items = await System.Threading.Tasks.Task.Run(() => engine.RunLocalDiagnostics());
                RenderAndSave();
            }
            catch (Exception ex) { ErrorReporter.Show(this, "运行本地诊断失败", ex); }
            finally { Busy(false); }
        }

        private async void TestDeepSeek()
        {
            DialogResult answer = MessageBox.Show(
                "测试会向 DeepSeek 发送一句最小验证消息，检查完整流式回复（最多 60 秒），会产生少量 API 用量；不包含你的文件或历史聊天。\r\n\r\n是否继续？",
                "测试 DeepSeek API", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            try
            {
                Busy(true);
                Application.DoEvents();
                DiagnosticItem apiResult = await System.Threading.Tasks.Task.Run(() => engine.TestDeepSeekApi());
                items.RemoveAll(delegate(DiagnosticItem item) { return item.Code.StartsWith("DSK-4") || item.Code == "NET-501"; });
                items.Add(apiResult);
                RenderAndSave();
            }
            catch (Exception ex)
            {
                ErrorInfo info = ErrorCatalog.Classify("测试 DeepSeek API", ex);
                items.RemoveAll(delegate(DiagnosticItem item) { return item.Code.StartsWith("DSK-4") || item.Code == "NET-501"; });
                items.Add(new DiagnosticItem(DiagnosticState.Failed, info.Code, "DeepSeek API 测试", info.Message, info.Suggestion));
                RenderAndSave();
                ErrorReporter.Show(this, "测试 DeepSeek API 失败", ex);
            }
            finally { Busy(false); }
        }

        private async void TestKimi()
        {
            DialogResult answer = MessageBox.Show(
                "测试会向当前 Kimi 模型所属平台发送一句最小验证消息，因此会产生少量 API 用量；不会包含你的文件或聊天内容。\r\n\r\n是否继续？",
                "测试 Kimi API", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            try
            {
                Busy(true);
                Application.DoEvents();
                DiagnosticItem apiResult = await System.Threading.Tasks.Task.Run(() => engine.TestKimiApi());
                items.RemoveAll(delegate(DiagnosticItem item) { return item.Code.StartsWith("KIM-4") || item.Code == "NET-501"; });
                items.Add(apiResult);
                RenderAndSave();
            }
            catch (Exception ex)
            {
                ErrorInfo info = ErrorCatalog.Classify("测试 Kimi API", ex);
                items.RemoveAll(delegate(DiagnosticItem item) { return item.Code.StartsWith("KIM-4") || item.Code == "NET-501"; });
                items.Add(new DiagnosticItem(DiagnosticState.Failed, info.Code, "Kimi API 测试", info.Message, info.Suggestion));
                RenderAndSave();
                ErrorReporter.Show(this, "测试 Kimi API 失败", ex);
            }
            finally { Busy(false); }
        }

        private void RenderAndSave()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Codex 模型切换器诊断报告");
            text.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            text.AppendLine("程序版本：" + Assembly.GetExecutingAssembly().GetName().Version.ToString());
            text.AppendLine("配置位置：" + engine.ConfigPath);
            text.AppendLine();
            foreach (DiagnosticItem item in items)
            {
                string state = item.State == DiagnosticState.Passed ? "通过" :
                    item.State == DiagnosticState.Warning ? "注意" : "失败";
                text.AppendLine("[" + state + "] [" + item.Code + "] " + item.Name);
                text.AppendLine("  " + item.Detail);
                if (!string.IsNullOrWhiteSpace(item.Suggestion)) text.AppendLine("  建议：" + item.Suggestion);
            }
            text.AppendLine();
            text.AppendLine("错误编号说明");
            text.AppendLine(ErrorCatalog.ReferenceText());
            text.AppendLine();
            text.AppendLine("说明：报告和日志会自动隐藏 API Key；不会读取或记录密钥正文。");
            reportBox.Text = text.ToString();
            currentReport = AppLog.SaveDiagnosticReport(reportBox.Text);
            reportPathLabel.Text = "报告已保存：" + currentReport;
            int failed = items.FindAll(delegate(DiagnosticItem item) { return item.State == DiagnosticState.Failed; }).Count;
            int warning = items.FindAll(delegate(DiagnosticItem item) { return item.State == DiagnosticState.Warning; }).Count;
            AppLog.Info("APP-000", "诊断完成", "失败=" + failed.ToString() + "，注意=" + warning.ToString());
        }
    }

    internal sealed class PathChoiceDialog : Form
    {
        private readonly ListBox paths;
        public string SelectedPath { get { return paths.SelectedItem == null ? string.Empty : paths.SelectedItem.ToString(); } }

        public PathChoiceDialog(List<string> choices)
        {
            Text = "选择 Codex 配置目录";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(680, 330);
            Font = new Font("Microsoft YaHei UI", 9F);
            Label label = new Label();
            label.Text = "检测到多个配置目录，请选择 ChatGPT/Codex 实际使用的目录：";
            label.AutoSize = true;
            label.Location = new Point(18, 18);
            Controls.Add(label);
            paths = new ListBox();
            paths.Location = new Point(18, 49);
            paths.Size = new Size(628, 180);
            paths.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            foreach (string choice in choices) paths.Items.Add(choice);
            if (paths.Items.Count > 0) paths.SelectedIndex = 0;
            paths.DoubleClick += delegate { if (paths.SelectedItem != null) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(paths);
            Button ok = new TechButton();
            ok.Text = "使用所选目录";
            ok.Location = new Point(500, 242);
            ok.Size = new Size(146, 34);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);
            Button cancel = new TechButton();
            cancel.Text = "取消";
            cancel.Location = new Point(400, 242);
            cancel.Size = new Size(90, 34);
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    internal sealed class SwitcherEngine
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Regex Assignment = new Regex("^\\s*([A-Za-z0-9_.-]+)\\s*=", RegexOptions.Compiled);
        private static readonly HashSet<string> ManagedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "model", "model_provider", "preferred_auth_method", "forced_login_method",
            "model_reasoning_effort", "web_search", "model_catalog_json", "openai_base_url"
        };

        private readonly string appDirectory;
        private readonly string catalogPath;
        private readonly string kimiCatalogPath;
        private readonly string backupsDirectory;
        private readonly string statePath;
        private readonly string settingsPath;
        private readonly string launchHintPath;
        private readonly string logsDirectory;
        private string codexDirectory;
        private string configPath;

        public string DeepSeekReasoningEffort { get; set; }

        internal static string NormalizeDeepSeekEffort(string effort)
        {
            if (effort == "none" || effort == "low" || effort == "high" || effort == "max") return effort;
            if (effort == "medium" || effort == "xhigh") return "high";
            if (effort == "ultra") return "max";
            return "low";
        }

        public string CodexDirectory { get { return codexDirectory; } }
        public string ConfigPath { get { return configPath; } }
        public string BackupsDirectory { get { return backupsDirectory; } }
        public string LogsDirectory { get { return logsDirectory; } }

        public SwitcherEngine()
        {
            // Launcher.ps1 hosts this assembly inside PowerShell, where BaseDirectory is the
            // PowerShell folder, so it passes the real program folder through the environment.
            string appHomeOverride = Environment.GetEnvironmentVariable("CODEX_MODEL_SWITCHER_APP_HOME");
            appDirectory = string.IsNullOrWhiteSpace(appHomeOverride)
                ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(appHomeOverride)).TrimEnd(Path.DirectorySeparatorChar);
            catalogPath = Path.Combine(appDirectory, "deepseek-models.json");
            kimiCatalogPath = Path.Combine(appDirectory, "kimi-models.json");
            string dataOverride = Environment.GetEnvironmentVariable("CODEX_MODEL_SWITCHER_DATA_HOME");
            string dataDirectory;
            if (!string.IsNullOrWhiteSpace(dataOverride))
                dataDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dataOverride));
            else
            {
                dataDirectory = Path.Combine(appDirectory, "data");
            }
            Directory.CreateDirectory(dataDirectory);
            if (string.IsNullOrWhiteSpace(dataOverride)) MigratePortableData(dataDirectory);
            backupsDirectory = Path.Combine(dataDirectory, "backups");
            logsDirectory = Path.Combine(dataDirectory, "logs");
            statePath = Path.Combine(dataDirectory, "switcher-state.ini");
            settingsPath = Path.Combine(dataDirectory, "settings.ini");
            launchHintPath = Path.Combine(dataDirectory, "launch-hint.ini");
            AppLog.Initialize(dataDirectory);
            AppLog.Info("APP-000", "启动", "Codex 模型切换器启动，版本 " +
                Assembly.GetExecutingAssembly().GetName().Version.ToString());
            MigrateLegacyData();
            ReloadConfigurationLocation();
            CurrentConfiguration current = ReadCurrentConfiguration();
            DeepSeekReasoningEffort = current.Provider == ProviderKind.DeepSeek ?
                NormalizeDeepSeekEffort(current.ReasoningEffort) : "low";
        }

        public void ReloadConfigurationLocation()
        {
            string selected = LoadSavedCodexDirectory();
            if (!IsCodexHome(selected))
            {
                List<string> found = DiscoverCodexHomes();
                selected = found.Count > 0 ? found[0] : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
            codexDirectory = Path.GetFullPath(selected);
            configPath = Path.Combine(codexDirectory, "config.toml");
        }

        public List<string> DiscoverCodexHomes()
        {
            List<string> result = new List<string>();
            AddCodexHome(result, LoadSavedCodexDirectory());
            AddCodexHome(result, TryGetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.Process));
            AddCodexHome(result, TryGetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.User));
            AddCodexHome(result, TryGetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.Machine));
            AddCodexHome(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
            AddCodexHome(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Codex"));
            AddCodexHome(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex"));
            return result;
        }

        public void SetCodexDirectory(string pathOrConfig)
        {
            string directory = NormalizeCodexHomeInput(pathOrConfig);
            if (!IsCodexHome(directory))
                throw new DirectoryNotFoundException("所选位置中没有找到用户级 config.toml：" + directory);
            codexDirectory = directory;
            configPath = Path.Combine(codexDirectory, "config.toml");
            SaveConfiguredCodexDirectory(directory);
            AppLog.Info("CFG-000", "设置配置目录", "已选择：" + configPath);
        }

        public void OpenCodexDirectory()
        {
            if (!Directory.Exists(codexDirectory))
                throw new DirectoryNotFoundException("配置目录不存在：" + codexDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + codexDirectory + "\"") { UseShellExecute = true });
        }

        public void EnsureBaseline()
        {
            if (!Directory.Exists(codexDirectory))
                throw new DirectoryNotFoundException("未找到 Codex 配置目录：" + codexDirectory);
            if (!File.Exists(configPath))
                throw new FileNotFoundException("未找到 Codex 配置文件。", configPath);
            if (!File.Exists(catalogPath))
                throw new FileNotFoundException("缺少 DeepSeek 模型目录。请重新安装切换器。", catalogPath);
            if (!File.Exists(kimiCatalogPath))
                throw new FileNotFoundException("缺少 Kimi 模型目录。请重新安装切换器。", kimiCatalogPath);

            ValidateCatalog(File.ReadAllText(catalogPath, Encoding.UTF8), ProviderKind.DeepSeek);
            ValidateCatalog(File.ReadAllText(kimiCatalogPath, Encoding.UTF8), ProviderKind.Kimi);
            CurrentConfiguration current = ReadCurrentConfiguration();
            if (current.Provider == ProviderKind.OpenAi)
            {
                if (IsUsableOpenAiModel(current.Model) || string.IsNullOrWhiteSpace(current.Model))
                    SaveBaseline(current.Model, current.ReasoningEffort);
                else if (!File.Exists(statePath)) SaveBaseline(string.Empty, string.Empty);
            }
            else if (!File.Exists(statePath))
            {
                SaveBaseline(string.Empty, string.Empty);
            }
        }

        // A third-party slug left behind in ChatGPT mode must never become the saved OpenAI model,
        // otherwise switching back would later restore a model OpenAI cannot serve.
        private static bool IsUsableOpenAiModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return false;
            return model.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) < 0 &&
                !IsKimiModelId(model);
        }

        private static bool IsKimiModelId(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return false;
            return model.StartsWith("kimi-", StringComparison.OrdinalIgnoreCase) ||
                model.Equals("k3", StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith("k3-", StringComparison.OrdinalIgnoreCase);
        }

        public CurrentConfiguration ReadCurrentConfiguration()
        {
            string content = File.Exists(configPath) ? File.ReadAllText(configPath, Encoding.UTF8) : string.Empty;
            Dictionary<string, string> top = ParseTopLevel(content);
            string provider = Unquote(GetValue(top, "model_provider"));
            string model = Unquote(GetValue(top, "model"));
            string reasoning = Unquote(GetValue(top, "model_reasoning_effort"));
            ProviderKind kind = string.Equals(provider, "deepseek", StringComparison.OrdinalIgnoreCase)
                ? ProviderKind.DeepSeek : (string.Equals(provider, "kimi", StringComparison.OrdinalIgnoreCase)
                ? ProviderKind.Kimi : ProviderKind.OpenAi);
            return new CurrentConfiguration(kind, model, reasoning);
        }

        public List<ModelOption> GetModelOptions(ProviderKind provider)
        {
            List<ModelOption> result = new List<ModelOption>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (provider == ProviderKind.OpenAi)
            {
                result.Add(new ModelOption("跟随 Codex 默认（推荐，自动适配新模型）", string.Empty));
                seen.Add(string.Empty);
                string cache = Path.Combine(codexDirectory, "models_cache.json");
                AddModels(result, seen, ReadModelCatalog(cache, ProviderKind.OpenAi));

                CurrentConfiguration current;
                try { current = ReadCurrentConfiguration(); }
                catch { current = new CurrentConfiguration(ProviderKind.OpenAi, string.Empty, string.Empty); }
                if (current.Provider == ProviderKind.OpenAi && IsUsableOpenAiModel(current.Model))
                    AddModel(result, seen, current.Model, current.Model + "（当前配置）");

                string baselineModel;
                string baselineReasoning;
                LoadBaseline(out baselineModel, out baselineReasoning);
                if (IsUsableOpenAiModel(baselineModel))
                    AddModel(result, seen, baselineModel, baselineModel + "（上次 OpenAI）");
            }
            else if (provider == ProviderKind.DeepSeek)
            {
                List<ModelOption> models = ReadModelCatalog(GetDeepSeekCatalogPath(), ProviderKind.DeepSeek);
                if (ContainsModel(models, "deepseek-v4-pro"))
                    AddModel(result, seen, "deepseek-v4-pro", "DeepSeek V4 Pro（推荐）");
                AddModels(result, seen, models);
            }
            else
            {
                AddModels(result, seen, ReadModelCatalog(GetKimiCatalogPath(), ProviderKind.Kimi));
            }
            if (provider != ProviderKind.OpenAi && !IsSelfTestMode())
            {
                string availability = Path.Combine(Path.GetDirectoryName(settingsPath), "unavailable-models.txt");
                if (File.Exists(availability))
                {
                    string key = provider == ProviderKind.Kimi ? LoadKimiKey() : LoadDeepSeekKey();
                    HashSet<string> blocked = new HashSet<string>(File.ReadAllLines(availability));
                    result.RemoveAll(delegate(ModelOption m) { return blocked.Contains(AvailabilityId(provider, m.Slug, key)); });
                }
            }
            return result;
        }

        private static string AvailabilityId(ProviderKind provider, string model, string key)
        {
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                return provider.ToString() + "|" + model + "|" + Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(key ?? string.Empty)));
        }

        private void RecordAvailability(ProviderKind provider, string model, string key, bool available)
        {
            if (IsSelfTestMode()) return;
            string path = Path.Combine(Path.GetDirectoryName(settingsPath), "unavailable-models.txt");
            HashSet<string> entries = new HashSet<string>(File.Exists(path) ? File.ReadAllLines(path) : new string[0]);
            string entry = AvailabilityId(provider, model, key);
            if (available) entries.Remove(entry); else entries.Add(entry);
            File.WriteAllLines(path, new List<string>(entries).ToArray());
        }

        private string GetDeepSeekCatalogPath()
        {
            if (ReadModelCatalog(catalogPath, ProviderKind.DeepSeek).Count > 0) return catalogPath;
            string codexCatalog = Path.Combine(codexDirectory, "models.json");
            int codexCount = ReadModelCatalog(codexCatalog, ProviderKind.DeepSeek).Count;
            int bundledCount = ReadModelCatalog(catalogPath, ProviderKind.DeepSeek).Count;
            if (codexCount == 0 && bundledCount == 0)
                throw new FileNotFoundException("没有找到可用的 DeepSeek 模型目录。", catalogPath);
            if (codexCount > bundledCount) return codexCatalog;
            if (bundledCount > codexCount) return catalogPath;
            if (codexCount > 0 && File.Exists(codexCatalog) && File.Exists(catalogPath) &&
                File.GetLastWriteTimeUtc(codexCatalog) >= File.GetLastWriteTimeUtc(catalogPath)) return codexCatalog;
            if (bundledCount > 0) return catalogPath;
            if (codexCount > 0) return codexCatalog;
            throw new FileNotFoundException("没有找到可用的 DeepSeek 模型目录。", catalogPath);
        }

        private string GetKimiCatalogPath()
        {
            List<ModelOption> models = ReadModelCatalog(kimiCatalogPath, ProviderKind.Kimi);
            if (models.Count == 0)
                throw new FileNotFoundException("没有找到可用的 Kimi 模型目录。", kimiCatalogPath);
            return kimiCatalogPath;
        }

        private static List<ModelOption> ReadModelCatalog(string path, ProviderKind provider)
        {
            List<ModelOption> result = new List<ModelOption>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return result;
            string json;
            try { json = File.ReadAllText(path, Encoding.UTF8); }
            catch { return result; }

            MatchCollection slugs = Regex.Matches(json, "\\\"slug\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            for (int i = 0; i < slugs.Count; i++)
            {
                string slug = slugs[i].Groups[1].Value;
                bool isDeepSeek = slug.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isKimi = IsKimiModelId(slug);
                if (provider == ProviderKind.DeepSeek && !isDeepSeek) continue;
                if (provider == ProviderKind.Kimi && !isKimi) continue;
                if (provider == ProviderKind.OpenAi && (isDeepSeek || isKimi)) continue;
                int start = slugs[i].Index;
                int end = i + 1 < slugs.Count ? slugs[i + 1].Index : json.Length;
                string segment = json.Substring(start, end - start);
                Match visibility = Regex.Match(segment, "\\\"visibility\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                if (provider == ProviderKind.OpenAi && visibility.Success && !string.Equals(visibility.Groups[1].Value, "list", StringComparison.OrdinalIgnoreCase))
                    continue;
                Match display = Regex.Match(segment, "\\\"display_name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                string name = display.Success ? display.Groups[1].Value : slug;
                result.Add(new ModelOption(name + "  [" + slug + "]", slug));
            }
            return result;
        }

        private static void AddModels(List<ModelOption> target, HashSet<string> seen, List<ModelOption> source)
        {
            foreach (ModelOption option in source)
                if (seen.Add(option.Slug)) target.Add(option);
        }

        private static void AddModel(List<ModelOption> target, HashSet<string> seen, string slug, string label)
        {
            if (!string.IsNullOrWhiteSpace(slug) && seen.Add(slug)) target.Add(new ModelOption(label, slug));
        }

        private static bool ContainsModel(List<ModelOption> options, string slug)
        {
            foreach (ModelOption option in options)
                if (string.Equals(option.Slug, slug, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public bool HasDeepSeekKey()
        {
            return IsValidProviderKey(LoadDeepSeekKey());
        }

        public bool HasKimiKey()
        {
            return IsValidProviderKey(LoadKimiKey());
        }

        public void SaveDeepSeekKey(string key)
        {
            key = key == null ? string.Empty : key.Trim();
            if (!IsValidProviderKey(key))
                throw new ArgumentException("DeepSeek API Key 格式不正确。");

            CredentialVault.WriteDeepSeekKey(key);
            CurrentConfiguration current = ReadCurrentConfiguration();
            SynchronizeProviderEnvironment(current.Provider);
            AppLog.Info("DSK-000", "保存 DeepSeek 密钥", "已保存到 Windows 凭据管理器；未记录密钥内容。");
        }

        public void DeleteDeepSeekKey()
        {
            try { CredentialVault.DeleteDeepSeekKey(); }
            finally { ClearDeepSeekEnvironment(); }
            AppLog.Info("DSK-000", "删除 DeepSeek 密钥", "凭据及用户环境变量已删除。");
        }

        public void SaveKimiKey(string key)
        {
            key = key == null ? string.Empty : key.Trim();
            if (!IsValidProviderKey(key))
                throw new ArgumentException("Kimi API Key 格式不正确。");

            CredentialVault.WriteKimiKey(key);
            CurrentConfiguration current = ReadCurrentConfiguration();
            SynchronizeProviderEnvironment(current.Provider);
            AppLog.Info("KIM-000", "保存 Kimi 密钥", "已保存到 Windows 凭据管理器；未记录密钥内容。");
        }

        public void DeleteKimiKey()
        {
            try { CredentialVault.DeleteKimiKey(); }
            finally { ClearKimiEnvironment(); }
            AppLog.Info("KIM-000", "删除 Kimi 密钥", "凭据及用户环境变量已删除。");
        }

        // Upgrades the old persistent user environment variable to Windows Credential Manager.
        // Normal application startup calls this; command-line self-tests do not modify user data.
        public void MigrateLegacyKeys()
        {
            string legacy = LoadEnvironmentDeepSeekKey();
            if (!CredentialVault.HasDeepSeekKey() && IsValidProviderKey(legacy))
                CredentialVault.WriteDeepSeekKey(legacy);
            string legacyKimi = LoadEnvironmentKimiKey();
            if (!CredentialVault.HasKimiKey() && IsValidProviderKey(legacyKimi))
                CredentialVault.WriteKimiKey(legacyKimi);

            CurrentConfiguration current = ReadCurrentConfiguration();
            SynchronizeProviderEnvironment(current.Provider);
        }

        private static bool IsValidProviderKey(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.StartsWith("sk-", StringComparison.Ordinal) && value.Length >= 20;
        }

        private string LoadDeepSeekKey()
        {
            if (IsSelfTestMode())
                return Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.Process) ?? string.Empty;
            string saved = CredentialVault.ReadDeepSeekKey();
            if (IsValidProviderKey(saved)) return saved;
            return LoadEnvironmentDeepSeekKey();
        }

        private string LoadKimiKey()
        {
            if (IsSelfTestMode())
                return Environment.GetEnvironmentVariable("KIMI_API_KEY", EnvironmentVariableTarget.Process) ?? string.Empty;
            string saved = CredentialVault.ReadKimiKey();
            if (IsValidProviderKey(saved)) return saved;
            return LoadEnvironmentKimiKey();
        }

        private static string LoadEnvironmentDeepSeekKey()
        {
            string value = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.User);
            if (!IsValidProviderKey(value))
                value = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.Process);
            return IsValidProviderKey(value) ? value.Trim() : string.Empty;
        }

        private static string LoadEnvironmentKimiKey()
        {
            string value = Environment.GetEnvironmentVariable("KIMI_API_KEY", EnvironmentVariableTarget.User);
            if (!IsValidProviderKey(value))
                value = Environment.GetEnvironmentVariable("KIMI_API_KEY", EnvironmentVariableTarget.Process);
            return IsValidProviderKey(value) ? value.Trim() : string.Empty;
        }

        private void SynchronizeProviderEnvironment(ProviderKind provider)
        {
            if (provider == ProviderKind.OpenAi)
            {
                ClearDeepSeekEnvironment();
                ClearKimiEnvironment();
                return;
            }

            string variable = provider == ProviderKind.DeepSeek ? "DEEPSEEK_API_KEY" : "KIMI_API_KEY";
            string key = provider == ProviderKind.DeepSeek ? LoadDeepSeekKey() : LoadKimiKey();
            if (!IsValidProviderKey(key))
                throw new InvalidOperationException("尚未配置 " + variable + "。");
            if (!IsSelfTestMode())
                Environment.SetEnvironmentVariable(variable, key, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(variable, key, EnvironmentVariableTarget.Process);
            if (provider == ProviderKind.DeepSeek) ClearKimiEnvironment();
            else ClearDeepSeekEnvironment();
            NativeMethods.BroadcastEnvironmentChange();
        }

        private static void ClearDeepSeekEnvironment()
        {
            if (!IsSelfTestMode())
                Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null, EnvironmentVariableTarget.Process);
            NativeMethods.BroadcastEnvironmentChange();
        }

        private static void ClearKimiEnvironment()
        {
            if (!IsSelfTestMode())
                Environment.SetEnvironmentVariable("KIMI_API_KEY", null, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("KIMI_API_KEY", null, EnvironmentVariableTarget.Process);
            NativeMethods.BroadcastEnvironmentChange();
        }

        private static bool IsSelfTestMode()
        {
            return string.Equals(Environment.GetEnvironmentVariable("CODEX_MODEL_SWITCHER_SELF_TEST"),
                "1", StringComparison.Ordinal);
        }

        public string Apply(ProviderKind provider, string selectedModel)
        {
            AppLog.Info("CFG-000", "开始切换", "目标提供商=" + ProviderName(provider) +
                "；目标模型=" + (string.IsNullOrWhiteSpace(selectedModel) ? "跟随默认" : selectedModel));
            if (provider != ProviderKind.OpenAi && !IsSelfTestMode())
                TestProviderApi(provider, selectedModel);
            EnsureBaseline();
            string original = File.ReadAllText(configPath, Encoding.UTF8);
            CurrentConfiguration before = ReadCurrentConfiguration();
            if (before.Provider == ProviderKind.OpenAi && IsUsableOpenAiModel(before.Model))
                SaveBaseline(before.Model, before.ReasoningEffort);

            Directory.CreateDirectory(backupsDirectory);
            string backupFolder = CreateUniqueBackupDirectory();
            Directory.CreateDirectory(backupFolder);
            File.Copy(configPath, Path.Combine(backupFolder, "config.toml"), true);

            string updated;
            if (provider == ProviderKind.DeepSeek)
            {
                if (string.IsNullOrWhiteSpace(selectedModel) ||
                    selectedModel.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("请选择有效的 DeepSeek 模型。");
                if (!HasDeepSeekKey())
                    throw new InvalidOperationException("尚未配置 DEEPSEEK_API_KEY。");
                string deepSeekCatalog = GetDeepSeekCatalogPath();
                List<ModelOption> deepSeekOptions = ReadModelCatalog(deepSeekCatalog, ProviderKind.DeepSeek);
                if (!ContainsModel(deepSeekOptions, selectedModel))
                    throw new InvalidOperationException("当前 DeepSeek 模型目录中没有模型：" + selectedModel +
                        "。请在设置中刷新或更新模型目录。");
                updated = BuildDeepSeekConfig(original, selectedModel, deepSeekCatalog);
            }
            else if (provider == ProviderKind.Kimi)
            {
                if (string.IsNullOrWhiteSpace(selectedModel) || !IsKimiModelId(selectedModel))
                    throw new InvalidOperationException("请选择有效的 Kimi 模型。");
                if (!HasKimiKey())
                    throw new InvalidOperationException("尚未配置 KIMI_API_KEY。");
                string kimiCatalog = GetKimiCatalogPath();
                List<ModelOption> kimiOptions = ReadModelCatalog(kimiCatalog, ProviderKind.Kimi);
                if (!ContainsModel(kimiOptions, selectedModel))
                    throw new AppIssueException("KIM-407", "当前 Kimi 模型目录中没有模型：" + selectedModel + "。",
                        "选择目录中已有模型；需要新模型时更新 kimi-models.json 后重试。");
                updated = BuildKimiConfig(original, selectedModel, kimiCatalog);
            }
            else
            {
                string baselineModel;
                string baselineReasoning;
                LoadBaseline(out baselineModel, out baselineReasoning);
                string openAiModel = selectedModel == null ? baselineModel : selectedModel.Trim();
                if (!string.IsNullOrEmpty(openAiModel) && !IsUsableOpenAiModel(openAiModel))
                {
                    // Switching back to ChatGPT must never be blocked by a leftover third-party model
                    // ID in the model box: fall back to the saved OpenAI baseline instead.
                    AppLog.Warning("OAI-307", "切换", "忽略遗留的第三方模型 ID：" + openAiModel + "；改用保存的 OpenAI 基线模型。");
                    openAiModel = IsUsableOpenAiModel(baselineModel) ? baselineModel : string.Empty;
                }
                updated = BuildOpenAiConfig(original, openAiModel, baselineReasoning);
            }

            ValidateConfig(updated, provider, selectedModel);
            AtomicWrite(configPath, updated);

            try
            {
                CurrentConfiguration after = ReadCurrentConfiguration();
                if (after.Provider != provider)
                    throw new InvalidDataException("写入后的 Provider 验证失败。");
                // A half-written or section-losing result must never be left behind: verify the
                // whole file, not just the provider keys, and roll back if anything is missing.
                DiagnosticItem integrity = CheckConfigIntegrity(true);
                if (integrity.State == DiagnosticState.Failed)
                    throw new AppIssueException(integrity.Code,
                        "切换后的完整性检查未通过：" + integrity.Detail, integrity.Suggestion);
                SynchronizeProviderEnvironment(provider);
            }
            catch
            {
                AtomicWrite(configPath, original);
                try
                {
                    if (before.Provider == ProviderKind.OpenAi ||
                        (before.Provider == ProviderKind.DeepSeek && HasDeepSeekKey()) ||
                        (before.Provider == ProviderKind.Kimi && HasKimiKey()))
                        SynchronizeProviderEnvironment(before.Provider);
                    else
                        SynchronizeProviderEnvironment(ProviderKind.OpenAi);
                }
                catch { }
                throw;
            }

            if (provider == ProviderKind.OpenAi)
            {
                CurrentConfiguration restored = ReadCurrentConfiguration();
                SaveBaseline(restored.Model, restored.ReasoningEffort);
            }
            AppLog.Info("CFG-000", "切换完成", "配置已写入并验证；备份=" + backupFolder);
            return backupFolder;
        }

        private static string ProviderName(ProviderKind provider)
        {
            return provider == ProviderKind.DeepSeek ? "DeepSeek" :
                (provider == ProviderKind.Kimi ? "Kimi" : "ChatGPT");
        }

        public RestartResult RestartChatGpt()
        {
            AppLog.Info("OAI-000", "重启 ChatGPT", "开始关闭现有 ChatGPT 进程并检测启动入口。");
            List<Process> processes = new List<Process>();
            processes.AddRange(Process.GetProcessesByName("ChatGPT"));

            string executable = string.Empty;
            string appUserModelId = string.Empty;
            CaptureRunningChatGpt(processes, out executable, out appUserModelId);

            if (!string.IsNullOrEmpty(executable) || !string.IsNullOrEmpty(appUserModelId))
                SaveLaunchHint(executable, appUserModelId);
            else
                LoadLaunchHint(out executable, out appUserModelId);

            foreach (Process process in processes)
            {
                try { process.CloseMainWindow(); } catch { }
            }
            Thread.Sleep(900);
            foreach (Process process in Process.GetProcessesByName("ChatGPT"))
            {
                try { process.Kill(); process.WaitForExit(2500); } catch { }
            }

            // Wait until every ChatGPT/Codex process is really gone. If a dying instance is still
            // running when the new one starts, it can write its own settings back into config.toml
            // and undo the switch we just applied.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (Process.GetProcessesByName("ChatGPT").Length == 0) break;
                Thread.Sleep(500);
            }
            Thread.Sleep(900);

            Exception lastError = null;
            if (!string.IsNullOrEmpty(executable) && File.Exists(executable))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
                    AppLog.Info("OAI-000", "重启 ChatGPT", "已通过程序路径启动：" + executable);
                    return new RestartResult(true, "已通过原程序路径启动。");
                }
                catch (Exception ex) { lastError = ex; }
            }

            if (!string.IsNullOrEmpty(appUserModelId))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + appUserModelId)
                    { UseShellExecute = true });
                    AppLog.Info("OAI-000", "重启 ChatGPT", "已通过 Windows 应用标识启动。");
                    return new RestartResult(true, "已通过 Windows 应用标识启动。");
                }
                catch (Exception ex) { lastError = ex; }
            }

            string[] knownIds =
            {
                "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0!App",
                "OpenAI.ChatGPT_2p2nqsd0c76g0!App"
            };
            foreach (string id in knownIds)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + id)
                    { UseShellExecute = true });
                    AppLog.Info("OAI-000", "重启 ChatGPT", "已通过已知 Windows 应用入口启动。");
                    return new RestartResult(true, "已通过 ChatGPT 应用入口启动。");
                }
                catch (Exception ex) { lastError = ex; }
            }

            string failure = lastError == null ? "没有检测到 ChatGPT 启动入口。" : lastError.Message;
            AppLog.Warning("OAI-301", "重启 ChatGPT", failure);
            return new RestartResult(false, failure);
        }

        public List<DiagnosticItem> RunLocalDiagnostics()
        {
            AppLog.Info("APP-000", "本地诊断", "开始运行本地诊断。");
            List<DiagnosticItem> result = new List<DiagnosticItem>();

            string windowsDetail;
            bool supportedWindows = DescribeWindows(out windowsDetail);
            result.Add(new DiagnosticItem(supportedWindows ? DiagnosticState.Passed : DiagnosticState.Warning,
                "SYS-001", "Windows 系统", windowsDetail,
                supportedWindows ? string.Empty : "建议使用 Windows 10 / 11；更早的系统未纳入完整兼容性测试。"));

            string probe = Path.Combine(logsDirectory, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(logsDirectory);
                File.WriteAllText(probe, "ok", Encoding.ASCII);
                File.Delete(probe);
                result.Add(new DiagnosticItem(DiagnosticState.Passed, "SYS-002", "用户数据与日志目录",
                    "可正常写入：" + Path.GetDirectoryName(logsDirectory), string.Empty));
            }
            catch (Exception ex)
            {
                result.Add(new DiagnosticItem(DiagnosticState.Failed, "IO-601", "用户数据与日志目录",
                    ex.Message, "检查程序所在目录的写入权限、磁盘空间和安全软件拦截。"));
            }
            finally { try { if (File.Exists(probe)) File.Delete(probe); } catch { } }

            if (!Directory.Exists(codexDirectory))
                result.Add(new DiagnosticItem(DiagnosticState.Failed, "CFG-101", "Codex 配置目录",
                    "目录不存在：" + codexDirectory, "在设置中自动寻找或手工选择 config.toml。"));
            else if (!File.Exists(configPath))
                result.Add(new DiagnosticItem(DiagnosticState.Failed, "CFG-102", "Codex 配置文件",
                    "没有找到：" + configPath, "先启动一次 ChatGPT/Codex，或手工选择实际配置文件。"));
            else
            {
                try
                {
                    CurrentConfiguration current = ReadCurrentConfiguration();
                    result.Add(new DiagnosticItem(DiagnosticState.Passed, "CFG-000", "Codex 配置文件",
                        "可读取；提供商=" + ProviderName(current.Provider) +
                        "；模型=" + (string.IsNullOrWhiteSpace(current.Model) ? "跟随默认" : current.Model), string.Empty));
                }
                catch (Exception ex)
                {
                    result.Add(new DiagnosticItem(DiagnosticState.Failed, "CFG-103", "Codex 配置文件",
                        ex.Message, "恢复最近备份，或修复 config.toml 后重新诊断。"));
                }
            }

            try
            {
                int openAiCount = Math.Max(0, GetModelOptions(ProviderKind.OpenAi).Count - 1);
                int deepSeekCount = GetModelOptions(ProviderKind.DeepSeek).Count;
                int kimiCount = GetModelOptions(ProviderKind.Kimi).Count;
                DiagnosticState modelState = deepSeekCount > 0 && kimiCount > 0 ? DiagnosticState.Passed : DiagnosticState.Failed;
                result.Add(new DiagnosticItem(modelState, modelState == DiagnosticState.Passed ? "MOD-000" : "MOD-201",
                    "模型目录", "OpenAI 模型=" + openAiCount.ToString() + "；DeepSeek 模型=" + deepSeekCount.ToString() +
                    "；Kimi 模型=" + kimiCount.ToString(),
                    modelState == DiagnosticState.Passed ? string.Empty : "确认 deepseek-models.json 和 kimi-models.json 与程序位于同一目录。"));
            }
            catch (Exception ex)
            {
                result.Add(new DiagnosticItem(DiagnosticState.Failed, "MOD-201", "模型目录", ex.Message,
                    "重新复制完整的软件目录，或在设置中选择正确的 Codex 配置目录。"));
            }

            try
            {
                if (HasKimiKey())
                    result.Add(new DiagnosticItem(DiagnosticState.Passed, "KIM-000", "Kimi 密钥",
                        "Windows 凭据管理器中存在密钥。未读取或显示密钥正文。", string.Empty));
                else
                    result.Add(new DiagnosticItem(DiagnosticState.Warning, "KIM-401", "Kimi 密钥",
                        "尚未保存 Kimi API Key。", "如果不使用 Kimi 可以忽略；否则输入密钥并主动运行 Kimi API 测试。"));
            }
            catch (Exception ex)
            {
                result.Add(new DiagnosticItem(DiagnosticState.Failed, "SYS-002", "Windows 凭据管理器",
                    ex.Message, "确认 Credential Manager 服务可用，并使用普通桌面用户账户运行程序。"));
            }

            try
            {
                if (HasDeepSeekKey())
                    result.Add(new DiagnosticItem(DiagnosticState.Passed, "DSK-000", "DeepSeek 密钥",
                        "Windows 凭据管理器中存在密钥。未读取或显示密钥正文。", string.Empty));
                else
                    result.Add(new DiagnosticItem(DiagnosticState.Warning, "DSK-401", "DeepSeek 密钥",
                        "尚未保存 DeepSeek API Key。", "如果只使用 OpenAI 可以忽略；否则输入密钥并运行 DeepSeek API 测试。"));
            }
            catch (Exception ex)
            {
                result.Add(new DiagnosticItem(DiagnosticState.Failed, "SYS-002", "Windows 凭据管理器",
                    ex.Message, "确认 Credential Manager 服务可用，并使用普通桌面用户账户运行程序。"));
            }

            string authDetail;
            bool authConfirmed = TryCheckCodexLogin(out authDetail);
            result.Add(new DiagnosticItem(authConfirmed ? DiagnosticState.Passed : DiagnosticState.Warning,
                authConfirmed ? "OAI-000" : "OAI-302", "OpenAI / ChatGPT 登录",
                authDetail, authConfirmed ? string.Empty : "打开 ChatGPT 的账户菜单确认已登录；也可安装 Codex CLI 后运行 codex login status。"));

            result.Add(new DiagnosticItem(DiagnosticState.Warning, "OAI-303", "账户与模型权限",
                "本地软件不能读取 ChatGPT 订阅、工作区席位、额度或服务器端模型权限。",
                "在 ChatGPT 中新建一个最小任务验证；若服务器返回 401/403/429，请检查账户、工作区权限或额度。"));

            string launch = GetLaunchDescription();
            bool hasLaunch = launch.IndexOf("尚未保存", StringComparison.OrdinalIgnoreCase) < 0;
            result.Add(new DiagnosticItem(hasLaunch ? DiagnosticState.Passed : DiagnosticState.Warning,
                hasLaunch ? "OAI-000" : "OAI-301", "ChatGPT 启动入口", launch,
                hasLaunch ? string.Empty : "先打开 ChatGPT 后点击自动检测，或手工选择 ChatGPT.exe。"));
            // The icons ship next to the program; if someone copies only the .exe, say so instead of
            // silently drawing fallback letters.
            try
            {
                List<string> missingAssets = AppAssets.MissingFiles();
                if (missingAssets.Count == 0)
                    result.Add(new DiagnosticItem(DiagnosticState.Passed, "APP-000", "图标素材",
                        "图标素材齐全：" + AppAssets.Directory, string.Empty));
                else
                    result.Add(new DiagnosticItem(DiagnosticState.Warning, "APP-702", "图标素材",
                        "缺少 " + missingAssets.Count.ToString() + " 个文件：" + string.Join("；", missingAssets.ToArray()),
                        "请复制完整的项目文件夹（包含 assets 子目录）；缺少素材时程序仍可使用，界面会退回默认图形。"));
            }
            catch (Exception ex)
            {
                result.Add(new DiagnosticItem(DiagnosticState.Warning, "APP-702", "图标素材", ex.Message,
                    "请复制完整的项目文件夹（包含 assets 子目录）。"));
            }
            result.Add(CheckConfigIntegrity());
            result.AddRange(InspectRecentIssues());
            return result;
        }

        // Verifies that config.toml is complete and self-consistent. This is what protects the user
        // when a switch is interrupted half-way (app closed by hand, machine shut down, quota stop,
        // antivirus lock): the damage is reported with a code instead of failing silently later.
        public DiagnosticItem CheckConfigIntegrity()
        {
            return CheckConfigIntegrity(false);
        }

        // strict = true is used right after this program writes config.toml: anything that vanished
        // since the snapshot taken seconds earlier is a failed switch. In the diagnostics window a
        // difference is only a warning, because ChatGPT/Codex rewrites its own sections (plugins,
        // projects, windows) while it runs.
        public DiagnosticItem CheckConfigIntegrity(bool strict)
        {
            AppLog.Info("CFG-000", "配置完整性检查", "开始检查：" + configPath);
            if (!Directory.Exists(codexDirectory))
                return new DiagnosticItem(DiagnosticState.Failed, "CFG-101", "配置完整性",
                    "配置目录不存在：" + codexDirectory, "在设置中点击“自动寻找”，或手工选择实际的 config.toml。");
            if (!File.Exists(configPath))
                return new DiagnosticItem(DiagnosticState.Failed, "CFG-102", "配置完整性",
                    "配置文件不存在：" + configPath, "先启动一次 ChatGPT/Codex，或在设置中选择实际配置文件。");

            string content;
            try
            {
                content = File.ReadAllText(configPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return new DiagnosticItem(DiagnosticState.Failed, "CFG-103", "配置完整性",
                    ex.Message, "关闭占用配置文件的 ChatGPT/Codex 后重新检查，或在设置中恢复最近备份。");
            }

            if (string.IsNullOrWhiteSpace(content))
                return new DiagnosticItem(DiagnosticState.Failed, "CFG-103", "配置完整性",
                    "配置文件为空：" + configPath, "在设置中恢复最近备份，或重新启动 ChatGPT 生成默认配置。");

            string structural = FindStructuralProblem(content);
            if (structural != null)
                return new DiagnosticItem(DiagnosticState.Failed, "CFG-104", "配置完整性",
                    structural + "（" + configPath + "）",
                    "上一次写入可能被中断。请先“恢复最近备份”，再重新切换一次；详细过程见当天日志。");

            List<string> leftovers = new List<string>();
            try
            {
                foreach (string file in Directory.GetFiles(codexDirectory, "*.tmp"))
                    if (Path.GetFileName(file).StartsWith(".model-switcher-", StringComparison.OrdinalIgnoreCase))
                        leftovers.Add(Path.GetFileName(file));
                foreach (string file in Directory.GetFiles(codexDirectory, "*.switcher-rollback"))
                    leftovers.Add(Path.GetFileName(file));
            }
            catch { }
            if (leftovers.Count > 0)
                return new DiagnosticItem(DiagnosticState.Warning, "CFG-104", "配置完整性",
                    "配置文件本身可用，但发现上一次切换留下的临时文件：" + string.Join("；", leftovers.ToArray()),
                    "这些文件不影响使用，可以删除；若切换结果异常，请先恢复最近备份再重新切换。");

            CurrentConfiguration current = ReadCurrentConfiguration();
            if (current.Provider == ProviderKind.DeepSeek)
            {
                if (!ContainsSection(content, "model_providers.deepseek"))
                    return new DiagnosticItem(DiagnosticState.Failed, "CFG-104", "配置完整性",
                        "DeepSeek 模式下缺少 [model_providers.deepseek] 配置段。",
                        "重新点击“切换并重启 ChatGPT”，或先恢复最近备份。");
                if (string.IsNullOrWhiteSpace(current.Model))
                    return new DiagnosticItem(DiagnosticState.Failed, "CFG-104", "配置完整性",
                        "DeepSeek 模式下没有写入模型 ID。", "重新选择 DeepSeek 模型并再次切换。");
                string catalog = Unquote(GetValue(ParseTopLevel(content), "model_catalog_json"));
                if (!string.IsNullOrWhiteSpace(catalog) && !File.Exists(catalog))
                    return new DiagnosticItem(DiagnosticState.Failed, "MOD-201", "配置完整性",
                        "配置引用的模型目录不存在：" + catalog, "在设置中刷新模型目录，或重新安装程序目录中的 deepseek-models.json。");
            }
            else if (current.Provider == ProviderKind.Kimi)
            {
                if (!ContainsSection(content, "model_providers.kimi"))
                    return new DiagnosticItem(DiagnosticState.Failed, "CFG-104", "配置完整性",
                        "Kimi 模式下缺少 [model_providers.kimi] 配置段。",
                        "重新点击“切换并重启 ChatGPT”，或先恢复最近备份。");
                if (string.IsNullOrWhiteSpace(current.Model))
                    return new DiagnosticItem(DiagnosticState.Failed, "CFG-104", "配置完整性",
                        "Kimi 模式下没有写入模型 ID。", "重新选择 Kimi 模型并再次切换。");
                string catalog = Unquote(GetValue(ParseTopLevel(content), "model_catalog_json"));
                if (!string.IsNullOrWhiteSpace(catalog) && !File.Exists(catalog))
                    return new DiagnosticItem(DiagnosticState.Failed, "MOD-201", "配置完整性",
                        "配置引用的 Kimi 模型目录不存在：" + catalog,
                        "重新安装程序目录中的 kimi-models.json，或恢复最近备份。");
                if (content.IndexOf(KimiBaseUrl(current.Model), StringComparison.OrdinalIgnoreCase) < 0 ||
                    content.IndexOf("env_key = \"KIMI_API_KEY\"", StringComparison.Ordinal) < 0)
                    return new DiagnosticItem(DiagnosticState.Failed, "CFG-104", "配置完整性",
                        "Kimi API 地址与所选模型不匹配，或环境变量认证配置不完整。", "重新切换一次 Kimi，程序会重建正确配置。");
            }
            else if (!string.IsNullOrWhiteSpace(current.Model) && !IsUsableOpenAiModel(current.Model))
            {
                return new DiagnosticItem(DiagnosticState.Failed, "CFG-104", "配置完整性",
                    "OpenAI / ChatGPT 模式下残留了第三方模型 ID：" + current.Model,
                    "在 ChatGPT 的模型选择器里重选一个 OpenAI 模型，或在本程序中点击“只保存配置”修复。");
            }

            string latest = GetLatestBackup();
            if (!string.IsNullOrEmpty(latest))
            {
                try
                {
                    string backupFile = Path.Combine(latest, "config.toml");
                    if (File.Exists(backupFile))
                    {
                        List<string> missing = MissingSections(File.ReadAllText(backupFile, Encoding.UTF8), content);
                        if (missing.Count > 0)
                            return new DiagnosticItem(strict ? DiagnosticState.Failed : DiagnosticState.Warning, "CFG-104", "配置完整性",
                                "与最近备份相比，当前配置缺少这些配置段：" + string.Join("；", missing.ToArray()),
                                strict
                                    ? "这些配置段在切换前存在，切换后丢失。请恢复最近备份，然后重新切换一次。"
                                    : "如果这些配置段是在切换之后消失的，通常是 ChatGPT/Codex 自己更新了配置；如怀疑切换中断，请恢复最近备份并重新切换。");
                    }
                }
                catch { }
            }

            AppLog.Info("CFG-000", "配置完整性检查", "通过；提供商=" + ProviderName(current.Provider));
            return new DiagnosticItem(DiagnosticState.Passed, "CFG-000", "配置完整性",
                "配置完整：提供商=" + ProviderName(current.Provider) +
                "；模型=" + (string.IsNullOrWhiteSpace(current.Model) ? "跟随默认" : current.Model) +
                "；大小=" + new FileInfo(configPath).Length.ToString() + " 字节。", string.Empty);
        }

        private static bool ContainsSection(string content, string section)
        {
            string needle = "[" + section + "]";
            foreach (string line in Normalize(content).Split('\n'))
                if (line.Trim().Equals(needle, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static List<string> MissingSections(string backupContent, string currentContent)
        {
            HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in Normalize(currentContent).Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) present.Add(trimmed);
            }
            List<string> missing = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in Normalize(backupContent).Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]")) continue;
                string header = trimmed.Trim('[', ']').Trim();
                if (header.Equals("model_providers.deepseek", StringComparison.OrdinalIgnoreCase) ||
                    header.StartsWith("model_providers.deepseek.", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("model_providers.kimi", StringComparison.OrdinalIgnoreCase) ||
                    header.StartsWith("model_providers.kimi.", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(trimmed)) continue;
                if (!present.Contains(trimmed)) missing.Add(trimmed);
            }
            return missing;
        }

        // Cheap syntax sanity check: duplicate top-level keys and unbalanced quotes are exactly what
        // an interrupted write or a hand-edited file produces.
        private static string FindStructuralProblem(string content)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = Normalize(content).Split('\n');
            bool topLevel = true;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("#")) continue;
                if (trimmed.StartsWith("["))
                {
                    if (!trimmed.EndsWith("]"))
                        return "第 " + (i + 1).ToString() + " 行的配置段标题不完整";
                    topLevel = false;
                    continue;
                }
                if (!topLevel) continue;
                Match match = Assignment.Match(line);
                if (!match.Success) continue;
                int equals = line.IndexOf('=');
                string value = equals >= 0 ? line.Substring(equals + 1).Trim() : string.Empty;
                // TOML permits multi-line arrays and triple-quoted strings. Only reject an
                // ordinary one-line quoted assignment that starts a quote and never closes it;
                // the previous per-line quote/bracket counter rejected valid Codex configs.
                if ((value.StartsWith("\"") && !value.StartsWith("\"\"\"") &&
                    !EndsWithUnescapedQuote(value, '"')) ||
                    (value.StartsWith("'") && !value.StartsWith("'''") && !value.EndsWith("'")))
                    return "第 " + (i + 1).ToString() + " 行的字符串没有闭合";
                if (!seen.Add(match.Groups[1].Value))
                    return "重复的顶层字段：" + match.Groups[1].Value;
            }
            return null;
        }

        private static bool EndsWithUnescapedQuote(string value, char quote)
        {
            bool escaped = false;
            for (int i = 1; i < value.Length; i++)
            {
                char current = value[i];
                if (current == quote && !escaped) return true;
                if (quote == '"' && current == '\\') escaped = !escaped;
                else escaped = false;
            }
            return false;
        }

        // Looks for real error signatures in the newest part of the Codex/desktop logs so that
        // server-side problems (not signed in, no seat, quota exhausted, model not allowed) can be
        // reported with a code even though the switcher cannot call OpenAI itself.
        public List<DiagnosticItem> InspectRecentIssues()
        {
            List<DiagnosticItem> result = new List<DiagnosticItem>();
            Dictionary<string, int> evidence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ProviderKind activeProvider = ReadCurrentConfiguration().Provider;
            bool openAiMode = activeProvider == ProviderKind.OpenAi;
            // Keep diagnostics responsive even when the SQLite logs have grown to several GB.
            // Every known signature asks for at most 4 MB, so reading 64 MB per file was wasteful
            // and could allocate close to 200 MB before matching started.
            const int windowBytes = 4 * 1024 * 1024;
            List<byte[]> codexTails = new List<byte[]>();
            try
            {
                foreach (string file in CodexLogFiles()) codexTails.Add(ReadTailBytes(file, windowBytes));
            }
            catch { }

            List<string> problems = new List<string>();
            try
            {
                foreach (string file in OwnLogFiles())
                {
                    int errors = CountOccurrences(ReadTailBytes(file, 512 * 1024), "[ERROR]");
                    if (errors > 0)
                    {
                        problems.Add(Path.GetFileName(file) + " 中有 " + errors.ToString() + " 条错误记录");
                        AppLog.Info("APP-000", "检查日志", Path.GetFileName(file) + "：错误记录=" + errors.ToString());
                    }
                }
            }
            catch { }

            foreach (Signature signature in CodexLogSignatures())
            {
                // While DeepSeek is active the desktop app still talks to some ChatGPT endpoints, so
                // 401/403/429 lines there are expected and must not be reported as a login problem.
                if (!openAiMode && signature.Code.StartsWith("OAI-", StringComparison.OrdinalIgnoreCase)) continue;
                if (activeProvider != ProviderKind.Kimi && signature.Code.StartsWith("KIM-", StringComparison.OrdinalIgnoreCase)) continue;
                int hits = 0;
                foreach (byte[] tail in codexTails)
                    hits += CountOccurrences(tail, signature.Pattern, signature.ScanBytes);
                if (hits <= 0) continue;
                result.Add(new DiagnosticItem(DiagnosticState.Warning, signature.Code, signature.Name,
                    "每个日志文件最近约 " + (signature.ScanBytes / (1024 * 1024)).ToString() + " MB 中出现 " +
                    hits.ToString() + " 次：" + signature.Evidence + "（时间范围取决于日志写入量，可用 DeepSeek API 测试或新建任务复核）",
                    signature.Suggestion));
                int previous;
                evidence[signature.Code] = evidence.TryGetValue(signature.Code, out previous) ? previous + hits : hits;
                AppLog.Warning(signature.Code, "检查 Codex 日志", "匹配 " + hits.ToString() + " 次：" + signature.Evidence);
            }

            if (problems.Count > 0)
                result.Add(new DiagnosticItem(DiagnosticState.Warning, "APP-701", "本程序历史日志",
                    string.Join("；", problems.ToArray()),
                    "打开日志目录查看对应时间点的完整记录；本程序的日志不会包含 API Key 正文。"));
            else
                result.Add(new DiagnosticItem(DiagnosticState.Passed, "APP-000", "本程序历史日志",
                    "最近日志中没有错误记录。", string.Empty));

            if (!openAiMode)
                result.Add(new DiagnosticItem(DiagnosticState.Passed, "APP-000", "日志线索范围",
                    "当前是 " + ProviderName(activeProvider) + " 模式，未评估 ChatGPT 端点的 401 / 403 / 429 线索（第三方提供商模式下这些提示可能属正常）。",
                    string.Empty));

            if (evidence.Count > 0)
            {
                StringBuilder summary = new StringBuilder();
                foreach (KeyValuePair<string, int> pair in evidence)
                    summary.AppendLine(pair.Key + " 出现 " + pair.Value.ToString() + " 次");
                bool deepSeekSide = false;
                bool openAiSide = false;
                bool kimiSide = false;
                foreach (string code in evidence.Keys)
                {
                    if (code.StartsWith("DSK-", StringComparison.OrdinalIgnoreCase)) deepSeekSide = true;
                    else if (code.StartsWith("KIM-", StringComparison.OrdinalIgnoreCase)) kimiSide = true;
                    else openAiSide = true;
                }
                string advice = string.Empty;
                if (openAiSide)
                    advice += "ChatGPT 方向：401 表示登录失效，403 表示账户/工作区/订阅无权限，429 表示额度或频率受限。";
                if (deepSeekSide)
                    advice += "DeepSeek 方向：先点击“测试 DeepSeek API”确认密钥与余额，再决定是否充值或更换密钥。";
                if (kimiSide)
                    advice += "Kimi 方向：点击“测试 Kimi”确认 Kimi Code 密钥、会员权限与配额。";
                result.Add(new DiagnosticItem(DiagnosticState.Warning, "OAI-303", "账户与模型权限线索",
                    "根据日志线索判断可能存在的服务端问题：" + Environment.NewLine + summary.ToString().Trim(),
                    advice + "任务中途停止通常就是这类服务端拒绝造成的。"));
            }
            return result;
        }

        private sealed class Signature
        {
            public string Code;
            public string Name;
            public string Pattern;
            public string Evidence;
            public string Suggestion;
            public int ScanBytes;

            public Signature(string code, string name, string pattern, string evidence, string suggestion, int scanBytes)
            {
                Code = code;
                Name = name;
                Pattern = pattern;
                Evidence = evidence;
                Suggestion = suggestion;
                ScanBytes = scanBytes;
            }
        }

        private static List<Signature> CodexLogSignatures()
        {
            List<Signature> list = new List<Signature>();
            list.Add(new Signature("OAI-304", "登录状态线索", "401 Unauthorized",
                "401 Unauthorized", "在 ChatGPT 中重新登录；DeepSeek 模式下出现此提示通常表示仍在用 OpenAI 端点，请确认已经切换提供商。", 4 * 1024 * 1024));
            list.Add(new Signature("OAI-304", "登录状态线索", "Missing bearer or basic authentication",
                "缺少认证头", "重新登录 ChatGPT；如果本机使用系统凭据存储，请在应用内完成一次登录。", 4 * 1024 * 1024));
            list.Add(new Signature("OAI-305", "额度与频率线索", "usage_limit_reached",
                "usage_limit_reached", "额度或限额已用尽：等待额度重置、检查订阅状态，或临时切换到 DeepSeek/Kimi。", 4 * 1024 * 1024));
            list.Add(new Signature("OAI-305", "额度与频率线索", "429 Too Many Requests",
                "429 Too Many Requests", "稍后重试；任务执行到一半被中断通常就是这一类错误。", 4 * 1024 * 1024));
            list.Add(new Signature("OAI-306", "账户权限线索", "403 Forbidden",
                "403 Forbidden", "账户、工作区或订阅无权使用该功能，请在 ChatGPT 中确认账户状态。", 4 * 1024 * 1024));
            list.Add(new Signature("OAI-307", "模型可用性线索", "Unknown model",
                "Unknown model（模型不可用）", "所选模型在当前账户/目录中不可用：在模型选择器里换一个模型。", 2 * 1024 * 1024));
            list.Add(new Signature("DSK-404", "DeepSeek 计费线索", "Insufficient Balance",
                "Insufficient Balance", "DeepSeek 账户余额不足：充值后再切换，或在 ChatGPT 模式下继续工作。", 4 * 1024 * 1024));
            list.Add(new Signature("DSK-403", "DeepSeek 认证线索", "Authentication Fails",
                "Authentication Fails（密钥被拒绝）", "DeepSeek 认为密钥无效或已被撤销：在 DeepSeek 平台生成新密钥后重新保存并测试。", 4 * 1024 * 1024));
            list.Add(new Signature("KIM-403", "Kimi 密钥被拒绝", "The API Key appears to be invalid or may have expired",
                "Kimi 返回密钥无效或过期", "在 Kimi Code 控制台生成有效密钥，在切换器中重新保存并测试。", 4 * 1024 * 1024));
            list.Add(new Signature("KIM-401", "Kimi 认证线索", "Missing environment variable: KIMI_API_KEY",
                "缺少 KIMI_API_KEY", "在本程序中保存 Kimi API Key，然后重新切换并重启 ChatGPT。", 4 * 1024 * 1024));
            list.Add(new Signature("KIM-407", "Kimi 模型线索", "Model metadata not found",
                "Kimi 模型元数据缺失", "确认 kimi-models.json 完整，并重新切换到 Kimi。", 2 * 1024 * 1024));
            return list;
        }

        private List<string> OwnLogFiles()
        {
            List<string> files = new List<string>();
            try
            {
                if (Directory.Exists(logsDirectory))
                {
                    FileInfo[] found = new DirectoryInfo(logsDirectory).GetFiles("switcher-*.log");
                    Array.Sort(found, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                    for (int i = 0; i < found.Length && i < 3; i++) files.Add(found[i].FullName);
                }
            }
            catch { }
            return files;
        }

        private List<string> CodexLogFiles()
        {
            List<FileInfo> candidates = new List<FileInfo>();
            try
            {
                foreach (FileInfo info in new DirectoryInfo(codexDirectory).GetFiles("logs_*.sqlite*"))
                {
                    // -shm is only a shared-memory index next to the database, it carries no log text
                    // and it is usually the newest file, which used to push the real database out.
                    if (info.Name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)) continue;
                    candidates.Add(info);
                }
            }
            catch { }
            candidates.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            List<string> files = new List<string>();
            for (int i = 0; i < candidates.Count && i < 3; i++) files.Add(candidates[i].FullName);
            return files;
        }

        // Read-only tail read: the desktop app keeps these files open, and only the newest part of
        // the log is relevant. Matching happens on bytes, so log content is never turned into text
        // and nothing but the match count reaches the switcher log.
        private static byte[] ReadTailBytes(string path, int maxBytes)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long length = stream.Length;
                int take = (int)Math.Min((long)maxBytes, length);
                if (take <= 0) return new byte[0];
                stream.Seek(length - take, SeekOrigin.Begin);
                byte[] buffer = new byte[take];
                int read = 0;
                while (read < take)
                {
                    int got = stream.Read(buffer, read, take - read);
                    if (got <= 0) break;
                    read += got;
                }
                if (read == take) return buffer;
                byte[] trimmed = new byte[read];
                Array.Copy(buffer, trimmed, read);
                return trimmed;
            }
        }

        private static int CountOccurrences(byte[] content, string pattern)
        {
            return CountOccurrences(content, pattern, content == null ? 0 : content.Length);
        }

        private static int CountOccurrences(byte[] content, string pattern, int maxBytes)
        {
            if (content == null || content.Length == 0 || string.IsNullOrEmpty(pattern)) return 0;
            byte[] needle = Encoding.ASCII.GetBytes(pattern.ToLowerInvariant());
            int count = 0;
            int start = Math.Max(0, content.Length - Math.Max(0, maxBytes));
            int limit = content.Length - needle.Length;
            for (int i = start; i <= limit; i++)
            {
                byte head = content[i];
                if (head >= (byte)'A' && head <= (byte)'Z') head = (byte)(head + 32);
                if (head != needle[0]) continue;
                int j = 1;
                while (j < needle.Length)
                {
                    byte value = content[i + j];
                    if (value >= (byte)'A' && value <= (byte)'Z') value = (byte)(value + 32);
                    if (value != needle[j]) break;
                    j++;
                }
                if (j == needle.Length)
                {
                    count++;
                    i += needle.Length - 1;
                }
            }
            return count;
        }

        // Environment.OSVersion reports 6.2 on Windows 10/11 unless the executable manifest opts in,
        // so relying on it would tell users their system is too old. Read the real values from the
        // registry and only warn for versions older than Windows 10.
        private static bool DescribeWindows(out string detail)
        {
            string product = string.Empty;
            string display = string.Empty;
            string build = string.Empty;
            int major = 0;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ProductName");
                        if (value != null) product = value.ToString().Trim();
                        value = key.GetValue("DisplayVersion");
                        if (value == null) value = key.GetValue("ReleaseId");
                        if (value != null) display = value.ToString().Trim();
                        value = key.GetValue("CurrentBuildNumber");
                        if (value != null) build = value.ToString().Trim();
                        value = key.GetValue("CurrentMajorVersionNumber");
                        if (value is int) major = (int)value;
                    }
                }
            }
            catch { }
            if (major <= 0) major = Environment.OSVersion.Version.Major;

            int buildNumber = 0;
            int.TryParse(build, out buildNumber);
            string name = string.IsNullOrEmpty(product) ? "Windows" : product;
            if (major >= 10 && buildNumber >= 22000 && name.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) < 0)
                name = "Windows 11";

            StringBuilder text = new StringBuilder();
            text.Append(name);
            if (!string.IsNullOrEmpty(display)) text.Append(" " + display);
            if (buildNumber > 0) text.Append("（内部版本 " + buildNumber.ToString() + "）");
            text.Append("；" + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
            detail = text.ToString();
            return major >= 10;
        }

        private bool TryCheckCodexLogin(out string detail)
        {
            string authFile = Path.Combine(codexDirectory, "auth.json");
            try
            {
                if (File.Exists(authFile) && new FileInfo(authFile).Length > 8)
                {
                    detail = "检测到 Codex 登录缓存。未读取缓存内容。";
                    return true;
                }
            }
            catch { }

            try
            {
                ProcessStartInfo start = new ProcessStartInfo("codex", "login status");
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                using (Process process = Process.Start(start))
                {
                    if (process.WaitForExit(5000) && process.ExitCode == 0)
                    {
                        detail = "Codex CLI 确认存在有效登录。";
                        return true;
                    }
                    try { if (!process.HasExited) process.Kill(); } catch { }
                }
            }
            catch { }
            detail = "未能从 auth.json 或 codex login status 确认登录；使用系统凭据存储时也可能出现此结果。";
            return false;
        }

        public DiagnosticItem TestDeepSeekApi()
        {
            CurrentConfiguration current = ReadCurrentConfiguration();
            return TestProviderApi(ProviderKind.DeepSeek,
                current.Provider == ProviderKind.DeepSeek ? current.Model : "deepseek-v4-pro");
        }

        public DiagnosticItem TestKimiApi()
        {
            CurrentConfiguration current = ReadCurrentConfiguration();
            return TestProviderApi(ProviderKind.Kimi,
                current.Provider == ProviderKind.Kimi ? current.Model : "kimi-k3");
        }

        private DiagnosticItem TestProviderApi(ProviderKind provider, string model)
        {
            string prefix = provider == ProviderKind.Kimi ? "KIM" : "DSK";
            string name = ProviderName(provider);
            string key = provider == ProviderKind.Kimi ? LoadKimiKey() : LoadDeepSeekKey();
            if (!IsValidProviderKey(key))
                throw new AppIssueException(prefix + "-401", "尚未保存 " + name + " API Key。",
                    "在切换器中输入有效密钥并加密保存后重试。");
            string url = provider == ProviderKind.Kimi ? KimiBaseUrl(model) + "/responses" :
                "https://api.deepseek.com/responses";
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.Accept = "text/event-stream";
            request.ContentType = "application/json";
            request.UserAgent = "CodexModelSwitcher/2.4.2";
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            request.AllowAutoRedirect = false;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            int timedOut = 0;
            // ReadWriteTimeout alone does not bound a stream of keep-alive comments.
            using (System.Threading.Timer deadline = new System.Threading.Timer(delegate(object state)
            {
                Interlocked.Exchange(ref timedOut, 1);
                request.Abort();
            }, null, 60000, Timeout.Infinite))
            {
                try
                {
                    JavaScriptSerializer json = new JavaScriptSerializer();
                    byte[] body = Encoding.UTF8.GetBytes(json.Serialize(new {
                        model = model, input = "Reply with OK only.", stream = true,
                        reasoning = new { effort = provider == ProviderKind.DeepSeek ? NormalizeDeepSeekEffort(DeepSeekReasoningEffort) : "low" }, max_output_tokens = 1024
                    }));
                    request.ContentLength = body.Length;
                    using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        if (response.ContentType.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) < 0)
                            throw new InvalidDataException("响应不是 Responses 流式事件。");
                        VerifyResponseStream(reader);
                    }
                    RecordAvailability(provider, model, key, true);
                    AppLog.Info(prefix + "-000", name + " API 测试", "已收到正文及 response.completed；模型=" + model);
                    return new DiagnosticItem(DiagnosticState.Passed, prefix + "-000", name + " API 测试",
                        "模型 " + model + " 已完成真实流式回复（包含正文和完成事件）。", string.Empty);
                }
                catch (Exception ex)
                {
                    if (Interlocked.CompareExchange(ref timedOut, 0, 0) != 0)
                        throw new AppIssueException(prefix + "-408", name + " 在 60 秒内未完成最小回复。",
                            "连接成功或心跳不代表模型正常工作。稍后重试，或换用其他模型；当前配置未因测试改变。", ex);
                    WebException web = ex as WebException;
                    HttpWebResponse response = web == null ? null : web.Response as HttpWebResponse;
                    if (response != null)
                    {
                        int status;
                        using (response) { status = (int)response.StatusCode; }
                        if (status == 401 || status == 403) RecordAvailability(provider, model, key, false);
                        string code = status == 401 || status == 403 ? "403" : status == 402 ? "404" :
                            status == 429 ? "405" : status == 400 || status == 404 ? "407" : "406";
                        string advice = status == 401 || status == 403 ?
                            "服务端拒绝认证。请在对应平台生成有效密钥，在切换器中重新保存；Kimi K3（开放平台）使用 platform.kimi.com 的密钥；Code 会员模型使用 kimi.com/code/console 的密钥，两者不能互用。" :
                            "检查所选模型、服务状态、账户余额或配额后重试。";
                        throw new AppIssueException(prefix + "-" + code, name + " 推理请求失败；HTTP " + status + "。", advice, ex);
                    }
                    if (ex is InvalidDataException || ex is ArgumentException)
                        throw new AppIssueException(prefix + "-407", name + " 未返回有效的完整回复。",
                            "模型列表可用或 HTTP 200 不代表推理成功。请稍后重试或选择其他模型。", ex);
                    throw new AppIssueException("NET-501", "无法完成 " + name + " 流式请求。",
                        "检查网络、代理及服务状态后重试。", ex);
                }
                finally { request.Abort(); }
            }
        }

        internal static void VerifyResponseStream(TextReader reader)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            StringBuilder data = new StringBuilder();
            bool textReceived = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length != 0)
                {
                    if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        if (data.Length > 0) data.Append('\n');
                        data.Append(line.Substring(5).TrimStart());
                        if (data.Length > 1024 * 1024) throw new InvalidDataException("事件过大。");
                    }
                    continue;
                }
                if (data.Length == 0) continue;
                string payload = data.ToString(); data.Length = 0;
                if (payload == "[DONE]") break;
                Dictionary<string, object> item = json.DeserializeObject(payload) as Dictionary<string, object>;
                if (item == null) throw new InvalidDataException("无效事件。");
                object value;
                string type = item.TryGetValue("type", out value) ? value as string : null;
                if (type == "error" || type == "response.failed" || type == "response.incomplete")
                    throw new InvalidDataException("服务端返回失败或不完整事件。");
                if (type == "response.output_text.delta" && item.TryGetValue("delta", out value) &&
                    !string.IsNullOrWhiteSpace(value as string)) textReceived = true;
                if (type == "response.completed")
                {
                    Dictionary<string, object> response = item.TryGetValue("response", out value) ?
                        value as Dictionary<string, object> : null;
                    if (response == null || !response.TryGetValue("status", out value) || !object.Equals(value, "completed"))
                        throw new InvalidDataException("回复未完成。");
                    if (!textReceived) throw new InvalidDataException("回复没有正文。");
                    return;
                }
            }
            throw new InvalidDataException("连接已结束，但没有完整回复。");
        }

        public string DetectChatGptLaunch()
        {
            AppLog.Info("OAI-000", "检测 ChatGPT", "开始检测正在运行的 ChatGPT 启动方式。");
            List<Process> processes = new List<Process>();
            processes.AddRange(Process.GetProcessesByName("ChatGPT"));
            if (processes.Count == 0)
                return "当前没有检测到正在运行的 ChatGPT。可以先打开 ChatGPT 后再次检测，或手动选择 ChatGPT.exe。";
            string executable;
            string appUserModelId;
            CaptureRunningChatGpt(processes, out executable, out appUserModelId);
            if (string.IsNullOrEmpty(executable) && string.IsNullOrEmpty(appUserModelId))
                return "检测到了 ChatGPT 进程，但 Windows 没有允许读取它的启动信息。请使用手动选择。";
            SaveLaunchHint(executable, appUserModelId);
            AppLog.Info("OAI-000", "检测 ChatGPT", "启动方式检测并保存成功。");
            return "已保存当前电脑的 ChatGPT 启动方式。";
        }

        public void SetManualChatGptExecutable(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("请选择有效的 Windows 可执行文件。", fullPath);
            SaveLaunchHint(fullPath, string.Empty);
            AppLog.Info("OAI-000", "手工设置 ChatGPT", "已保存启动文件：" + fullPath);
        }

        public string GetLaunchDescription()
        {
            string executable;
            string appUserModelId;
            LoadLaunchHint(out executable, out appUserModelId);
            if (!string.IsNullOrEmpty(executable)) return executable;
            if (!string.IsNullOrEmpty(appUserModelId)) return "Windows 应用：" + appUserModelId;
            return "尚未保存；重启时会自动检测正在运行的 ChatGPT";
        }

        private static void CaptureRunningChatGpt(List<Process> processes, out string executable, out string appUserModelId)
        {
            executable = string.Empty;
            appUserModelId = string.Empty;
            foreach (Process process in processes)
            {
                if (string.IsNullOrEmpty(executable)) executable = NativeMethods.TryGetProcessPath(process);
                if (string.IsNullOrEmpty(appUserModelId)) appUserModelId = NativeMethods.TryGetApplicationUserModelId(process);
            }
        }

        public void OpenBackupDirectory()
        {
            Directory.CreateDirectory(backupsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + backupsDirectory + "\"") { UseShellExecute = true });
        }

        public void OpenLogsDirectory()
        {
            Directory.CreateDirectory(logsDirectory);
            AppLog.Info("APP-000", "打开日志目录", logsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + logsDirectory + "\"") { UseShellExecute = true });
        }

        public string GetLatestBackup()
        {
            if (!Directory.Exists(backupsDirectory)) return string.Empty;
            DirectoryInfo[] directories = new DirectoryInfo(backupsDirectory).GetDirectories();
            Array.Sort(directories, delegate(DirectoryInfo a, DirectoryInfo b)
            {
                return b.Name.CompareTo(a.Name);
            });
            // "-before-restore" folders are safety copies of the state *before* a restore, so they
            // must not become the next "latest backup" (that would undo the user's own restore).
            string fallback = string.Empty;
            foreach (DirectoryInfo directory in directories)
            {
                if (!File.Exists(Path.Combine(directory.FullName, "config.toml"))) continue;
                if (directory.Name.EndsWith("-before-restore", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(fallback)) fallback = directory.FullName;
                    continue;
                }
                return directory.FullName;
            }
            return fallback;
        }

        public void RestoreBackup(string directory)
        {
            string fullDirectory = Path.GetFullPath(directory);
            string fullBackupRoot = Path.GetFullPath(backupsDirectory) + Path.DirectorySeparatorChar;
            if (!fullDirectory.StartsWith(fullBackupRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("拒绝恢复备份目录以外的文件。");
            string source = Path.Combine(fullDirectory, "config.toml");
            if (!File.Exists(source)) throw new FileNotFoundException("备份中的 config.toml 不存在。", source);
            string content = File.ReadAllText(source, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("备份文件为空。");
            string previous = File.Exists(configPath) ? File.ReadAllText(configPath, Encoding.UTF8) : string.Empty;

            Directory.CreateDirectory(backupsDirectory);
            string safety = CreateUniqueBackupDirectory() + "-before-restore";
            Directory.CreateDirectory(safety);
            if (File.Exists(configPath)) File.Copy(configPath, Path.Combine(safety, "config.toml"), true);
            AtomicWrite(configPath, content);
            try
            {
                CurrentConfiguration restored = ReadCurrentConfiguration();
                DiagnosticItem integrity = CheckConfigIntegrity(false);
                if (integrity.State == DiagnosticState.Failed)
                    throw new AppIssueException("CFG-104", "恢复后的配置未通过完整性检查：" + integrity.Detail,
                        "请选择其他备份，或修复备份中的 config.toml。" );
                if (restored.Provider == ProviderKind.OpenAi ||
                    (restored.Provider == ProviderKind.DeepSeek && HasDeepSeekKey()) ||
                    (restored.Provider == ProviderKind.Kimi && HasKimiKey()))
                    SynchronizeProviderEnvironment(restored.Provider);
                else if (restored.Provider == ProviderKind.DeepSeek)
                {
                    SynchronizeProviderEnvironment(ProviderKind.OpenAi);
                    AppLog.Warning("DSK-401", "恢复备份", "恢复到了 DeepSeek 配置，但尚未保存 API Key。");
                }
                else
                {
                    SynchronizeProviderEnvironment(ProviderKind.OpenAi);
                    AppLog.Warning("KIM-401", "恢复备份", "恢复到了 Kimi 配置，但尚未保存 API Key。");
                }
            }
            catch
            {
                if (!string.IsNullOrEmpty(previous)) AtomicWrite(configPath, previous);
                try
                {
                    CurrentConfiguration rolledBack = ReadCurrentConfiguration();
                    SynchronizeProviderEnvironment(rolledBack.Provider);
                }
                catch { }
                throw;
            }
            AppLog.Info("CFG-000", "恢复备份", "已从备份恢复配置：" + fullDirectory + "；恢复前安全备份=" + safety);
        }

        private string BuildDeepSeekConfig(string original, string model, string deepSeekCatalogPath)
        {
            List<string> lines = StripManagedConfiguration(original);
            List<string> result = new List<string>();
            result.Add("model = \"" + model + "\"");
            result.Add("model_provider = \"deepseek\"");
            result.Add("preferred_auth_method = \"apikey\"");
            result.Add("forced_login_method = \"api\"");
            result.Add("model_reasoning_effort = \"" + NormalizeDeepSeekEffort(DeepSeekReasoningEffort) + "\"");
            result.Add("web_search = \"disabled\"");
            result.Add("model_catalog_json = \"" + TomlEscape(deepSeekCatalogPath.Replace('\\', '/')) + "\"");
            result.Add(string.Empty);
            AppendTrimmed(result, lines);
            if (result.Count > 0 && result[result.Count - 1].Length != 0) result.Add(string.Empty);
            result.Add("[model_providers.deepseek]");
            result.Add("name = \"deepseek\"");
            result.Add("base_url = \"https://api.deepseek.com/\"");
            result.Add("wire_api = \"responses\"");
            result.Add("env_key = \"DEEPSEEK_API_KEY\"");
            result.Add("requires_openai_auth = false");
            result.Add("request_max_retries = 0");
            result.Add("stream_max_retries = 0");
            result.Add("stream_idle_timeout_ms = 60000");
            return string.Join("\n", result.ToArray()).TrimEnd() + "\n";
        }

        private static string KimiBaseUrl(string model)
        {
            // Public API keys and Code membership keys belong to distinct services.
            // Route only by the explicitly selected model; never try a key on another platform.
            return string.Equals(model, "kimi-k3", StringComparison.OrdinalIgnoreCase) ?
                "https://api.moonshot.cn/v1" : "https://api.kimi.com/coding/v1";
        }

        private string BuildKimiConfig(string original, string model, string catalog)
        {
            List<string> lines = StripManagedConfiguration(original);
            List<string> result = new List<string>();
            result.Add("model = \"" + TomlEscape(model) + "\"");
            result.Add("model_provider = \"kimi\"");
            result.Add("preferred_auth_method = \"apikey\"");
            result.Add("forced_login_method = \"api\"");
            result.Add("model_reasoning_effort = \"low\"");
            result.Add("web_search = \"disabled\"");
            result.Add("model_catalog_json = \"" + TomlEscape(catalog.Replace('\\', '/')) + "\"");
            result.Add(string.Empty);
            AppendTrimmed(result, lines);
            if (result.Count > 0 && result[result.Count - 1].Length != 0) result.Add(string.Empty);
            result.Add("[model_providers.kimi]");
            result.Add("name = \"Kimi\"");
            result.Add("base_url = \"" + KimiBaseUrl(model) + "\"");
            result.Add("wire_api = \"responses\"");
            result.Add("env_key = \"KIMI_API_KEY\"");
            result.Add("requires_openai_auth = false");
            result.Add("request_max_retries = 0");
            result.Add("stream_max_retries = 0");
            result.Add("stream_idle_timeout_ms = 60000");
            return string.Join("\n", result.ToArray()).TrimEnd() + "\n";
        }

        private string BuildOpenAiConfig(string original, string model, string reasoning)
        {
            List<string> lines = StripManagedConfiguration(original);
            List<string> result = new List<string>();
            if (!string.IsNullOrWhiteSpace(model)) result.Add("model = \"" + TomlEscape(model) + "\"");
            if (!string.IsNullOrWhiteSpace(reasoning)) result.Add("model_reasoning_effort = \"" + TomlEscape(reasoning) + "\"");
            if (result.Count > 0) result.Add(string.Empty);
            AppendTrimmed(result, lines);
            return string.Join("\n", result.ToArray()).TrimEnd() + "\n";
        }

        private static List<string> StripManagedConfiguration(string content)
        {
            string normalized = Normalize(content);
            string[] source = normalized.Split('\n');
            List<string> output = new List<string>();
            bool topLevel = true;
            bool skipProviderSection = false;

            for (int i = 0; i < source.Length; i++)
            {
                string line = source[i];
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    topLevel = false;
                    string header = trimmed.Trim('[', ']').Trim();
                    skipProviderSection = header.Equals("model_providers.deepseek", StringComparison.OrdinalIgnoreCase)
                        || header.StartsWith("model_providers.deepseek.", StringComparison.OrdinalIgnoreCase)
                        || header.Equals("model_providers.kimi", StringComparison.OrdinalIgnoreCase)
                        || header.StartsWith("model_providers.kimi.", StringComparison.OrdinalIgnoreCase);
                    if (skipProviderSection) continue;
                }
                if (skipProviderSection) continue;

                if (topLevel)
                {
                    Match match = Assignment.Match(line);
                    if (match.Success && ManagedKeys.Contains(match.Groups[1].Value)) continue;
                }
                output.Add(line);
            }
            return output;
        }

        private static void AppendTrimmed(List<string> destination, List<string> source)
        {
            int start = 0;
            int end = source.Count - 1;
            while (start <= end && string.IsNullOrWhiteSpace(source[start])) start++;
            while (end >= start && string.IsNullOrWhiteSpace(source[end])) end--;
            for (int i = start; i <= end; i++) destination.Add(source[i]);
        }

        private static Dictionary<string, string> ParseTopLevel(string content)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = Normalize(content).Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[")) break;
                Match match = Assignment.Match(line);
                if (!match.Success) continue;
                int equals = line.IndexOf('=');
                if (equals >= 0) result[match.Groups[1].Value] = line.Substring(equals + 1).Trim();
            }
            return result;
        }

        private static string GetValue(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static string Unquote(string value)
        {
            if (value == null) return string.Empty;
            value = value.Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') ||
                (value[0] == '\'' && value[value.Length - 1] == '\'')))
                return value.Substring(1, value.Length - 2);
            return value;
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string TomlEscape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void ValidateCatalog(string json, ProviderKind provider)
        {
            string expected = provider == ProviderKind.Kimi ?
                "\\\"slug\\\"\\s*:\\s*\\\"(?:kimi-|k3(?:-|\\\"))" :
                "\\\"slug\\\"\\s*:\\s*\\\"deepseek[^\\\"]*\\\"";
            if (string.IsNullOrWhiteSpace(json) || json.IndexOf("\"models\"", StringComparison.Ordinal) < 0 ||
                !Regex.IsMatch(json, expected, RegexOptions.IgnoreCase))
                throw new InvalidDataException(ProviderName(provider) + " 模型目录不完整或已损坏。");
        }

        private static void ValidateConfig(string content, ProviderKind expectedProvider, string model)
        {
            Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = Normalize(content).Split('\n');
            bool topLevel = true;
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[")) topLevel = false;
                if (!topLevel) continue;
                Match match = Assignment.Match(line);
                if (!match.Success) continue;
                string key = match.Groups[1].Value;
                if (seen.ContainsKey(key)) throw new InvalidDataException("生成的配置包含重复字段：" + key);
                seen[key] = line;
            }
            Dictionary<string, string> top = ParseTopLevel(content);
            string provider = Unquote(GetValue(top, "model_provider"));
            if (expectedProvider == ProviderKind.DeepSeek)
            {
                if (!string.Equals(provider, "deepseek", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("DeepSeek Provider 未正确写入。");
                if (content.IndexOf("[model_providers.deepseek]", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidDataException("DeepSeek Provider 配置段缺失。");
                if (!string.Equals(Unquote(GetValue(top, "model")), model, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("DeepSeek 模型未正确写入。");
                if (content.IndexOf("env_key = \"DEEPSEEK_API_KEY\"", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("DeepSeek 环境变量认证配置缺失。");
                if (Regex.IsMatch(content, "sk-[A-Za-z0-9_-]+"))
                    throw new InvalidDataException("安全检查失败：配置中不应出现 API Key。");
            }
            else if (expectedProvider == ProviderKind.Kimi)
            {
                if (!string.Equals(provider, "kimi", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Kimi Provider 未正确写入。");
                if (content.IndexOf("[model_providers.kimi]", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidDataException("Kimi Provider 配置段缺失。");
                if (!string.Equals(Unquote(GetValue(top, "model")), model, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Kimi 模型未正确写入。");
                if (content.IndexOf("env_key = \"KIMI_API_KEY\"", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("Kimi 环境变量认证配置缺失。");
                if (content.IndexOf("base_url = \"" + KimiBaseUrl(model) + "\"", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("Kimi API 地址与所选模型不匹配。");
                if (Regex.IsMatch(content, "sk-[A-Za-z0-9_-]+"))
                    throw new InvalidDataException("安全检查失败：配置中不应出现 API Key。");
            }
            else if (string.Equals(provider, "deepseek", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, "kimi", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("OpenAI 配置中仍残留第三方 Provider 选择。");
        }

        private static void AtomicWrite(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("配置路径无效。");
            Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, ".model-switcher-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, content, Utf8NoBom);
                using (FileStream stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    stream.Flush();
                }
                if (File.Exists(path))
                {
                    string replaceBackup = path + ".switcher-rollback";
                    try
                    {
                        File.Replace(temp, path, replaceBackup, true);
                        try { File.Delete(replaceBackup); } catch { }
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temp, path, true);
                        File.Delete(temp);
                    }
                }
                else File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private string CreateUniqueBackupDirectory()
        {
            string baseName = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string candidate = Path.Combine(backupsDirectory, baseName);
            int suffix = 1;
            while (Directory.Exists(candidate))
            {
                candidate = Path.Combine(backupsDirectory, baseName + "-" + suffix.ToString());
                suffix++;
            }
            return candidate;
        }

        private void SaveBaseline(string model, string reasoning)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("codex_home=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(codexDirectory ?? string.Empty)));
            text.AppendLine("model=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(model ?? string.Empty)));
            text.AppendLine("reasoning=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(reasoning ?? string.Empty)));
            AtomicWrite(statePath, text.ToString());
        }

        private void LoadBaseline(out string model, out string reasoning)
        {
            model = string.Empty;
            reasoning = string.Empty;
            if (!File.Exists(statePath)) return;
            string savedHome = string.Empty;
            string savedModel = string.Empty;
            string savedReasoning = string.Empty;
            foreach (string line in File.ReadAllLines(statePath, Encoding.UTF8))
            {
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals);
                string value = line.Substring(equals + 1);
                try
                {
                    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                    if (key == "codex_home") savedHome = decoded;
                    if (key == "model") savedModel = decoded;
                    if (key == "reasoning") savedReasoning = decoded;
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(savedHome) &&
                !string.Equals(Path.GetFullPath(savedHome), Path.GetFullPath(codexDirectory), StringComparison.OrdinalIgnoreCase))
                return;
            if (string.IsNullOrEmpty(savedModel) || IsUsableOpenAiModel(savedModel)) model = savedModel;
            reasoning = savedReasoning;
        }

        private void SaveLaunchHint(string executable, string appUserModelId)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("exe=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(executable ?? string.Empty)));
            text.AppendLine("aumid=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(appUserModelId ?? string.Empty)));
            AtomicWrite(launchHintPath, text.ToString());
        }

        private void LoadLaunchHint(out string executable, out string appUserModelId)
        {
            executable = string.Empty;
            appUserModelId = string.Empty;
            if (!File.Exists(launchHintPath)) return;
            foreach (string line in File.ReadAllLines(launchHintPath, Encoding.UTF8))
            {
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                try
                {
                    string value = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(equals + 1)));
                    if (line.StartsWith("exe=", StringComparison.OrdinalIgnoreCase)) executable = value;
                    if (line.StartsWith("aumid=", StringComparison.OrdinalIgnoreCase)) appUserModelId = value;
                }
                catch { }
            }
        }

        private static void MigratePortableData(string destination)
        {
            string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexModelSwitcher");
            string marker = Path.Combine(destination, ".migration-complete");
            if (File.Exists(marker) || !Directory.Exists(source)) return;
            foreach (string file in new string[] { "settings.ini", "switcher-state.ini", "launch-hint.ini" })
                CopyPortableFile(Path.Combine(source, file), Path.Combine(destination, file));
            foreach (string folder in new string[] { "logs", "reports", "backups" })
                CopyPortableTree(Path.Combine(source, folder), Path.Combine(destination, folder));
            File.WriteAllText(marker, "Copied existing data; originals preserved.");
        }

        private static void CopyPortableFile(string source, string destination)
        {
            if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination, false);
        }

        private static void CopyPortableTree(string source, string destination)
        {
            if (!Directory.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) return;
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) CopyPortableFile(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string folder in Directory.GetDirectories(source)) CopyPortableTree(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }

        private void MigrateLegacyData()
        {
            TryCopyLegacyFile(Path.Combine(appDirectory, "switcher-state.ini"), statePath);
            TryCopyLegacyFile(Path.Combine(appDirectory, "launch-hint.ini"), launchHintPath);
            string legacyBackups = Path.Combine(appDirectory, "backups");
            if (!Directory.Exists(legacyBackups)) return;
            Directory.CreateDirectory(backupsDirectory);
            foreach (string legacyDirectory in Directory.GetDirectories(legacyBackups))
            {
                string source = Path.Combine(legacyDirectory, "config.toml");
                if (!File.Exists(source)) continue;
                string destinationDirectory = Path.Combine(backupsDirectory, Path.GetFileName(legacyDirectory));
                string destination = Path.Combine(destinationDirectory, "config.toml");
                if (File.Exists(destination)) continue;
                Directory.CreateDirectory(destinationDirectory);
                File.Copy(source, destination, false);
            }
        }

        private static void TryCopyLegacyFile(string source, string destination)
        {
            try
            {
                if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination, false);
            }
            catch { }
        }

        private string LoadSavedCodexDirectory()
        {
            if (!File.Exists(settingsPath)) return string.Empty;
            foreach (string line in File.ReadAllLines(settingsPath, Encoding.UTF8))
            {
                if (!line.StartsWith("codex_home=", StringComparison.OrdinalIgnoreCase)) continue;
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring("codex_home=".Length))); }
                catch { return string.Empty; }
            }
            return string.Empty;
        }

        private void SaveConfiguredCodexDirectory(string directory)
        {
            string value = "codex_home=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(directory)) + Environment.NewLine;
            AtomicWrite(settingsPath, value);
        }

        private static string NormalizeCodexHomeInput(string pathOrConfig)
        {
            if (string.IsNullOrWhiteSpace(pathOrConfig))
                throw new ArgumentException("配置路径不能为空。");
            string expanded = Environment.ExpandEnvironmentVariables(pathOrConfig.Trim().Trim('"'));
            string full = Path.GetFullPath(expanded);
            if (File.Exists(full))
            {
                if (!string.Equals(Path.GetFileName(full), "config.toml", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("请选择名为 config.toml 的用户级配置文件。");
                full = Path.GetDirectoryName(full);
            }
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsCodexHome(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;
            try { return File.Exists(Path.Combine(Path.GetFullPath(directory), "config.toml")); }
            catch { return false; }
        }

        private static void AddCodexHome(List<string> target, string candidate)
        {
            if (!IsCodexHome(candidate)) return;
            string full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string existing in target)
                if (string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)) return;
            target.Add(full);
        }

        private static string TryGetEnvironmentVariable(string name, EnvironmentVariableTarget target)
        {
            try { return Environment.GetEnvironmentVariable(name, target); }
            catch { return string.Empty; }
        }

        // Runs entirely inside a temporary data home so it never touches the user's config.toml,
        // logs, backups or credentials.
        public static void RunSelfTest()
        {
            string originalDataHome = Environment.GetEnvironmentVariable("CODEX_MODEL_SWITCHER_DATA_HOME");
            string originalSelfTest = Environment.GetEnvironmentVariable("CODEX_MODEL_SWITCHER_SELF_TEST");
            string originalProcessKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.Process);
            string originalProcessKimiKey = Environment.GetEnvironmentVariable("KIMI_API_KEY", EnvironmentVariableTarget.Process);
            string tempRoot = Path.Combine(Path.GetTempPath(), "codex-model-switcher-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                Environment.SetEnvironmentVariable("CODEX_MODEL_SWITCHER_SELF_TEST", "1", EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sk-" + new string('d', 32), EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("KIMI_API_KEY", "sk-" + new string('k', 32), EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("CODEX_MODEL_SWITCHER_DATA_HOME",
                    Path.Combine(tempRoot, "data"), EnvironmentVariableTarget.Process);
                RunEngineSelfTest(tempRoot);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CODEX_MODEL_SWITCHER_DATA_HOME", originalDataHome,
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("CODEX_MODEL_SWITCHER_SELF_TEST", originalSelfTest,
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", originalProcessKey,
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("KIMI_API_KEY", originalProcessKimiKey,
                    EnvironmentVariableTarget.Process);
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }

        private static void RunEngineSelfTest(string tempRoot)
        {
            string sample = "sandbox_mode = \"workspace-write\"\nmodel = \"gpt-test\"\nmodel_reasoning_effort = \"medium\"\n\n[desktop]\nlocaleOverride = \"zh-CN\"\n\n[model_providers.deepseek]\nname = \"old\"\nexperimental_bearer_token = \"not-a-real-key\"\n";
            SwitcherEngine test = new SwitcherEngine();
            string testCatalog = test.GetDeepSeekCatalogPath();
            string deep = test.BuildDeepSeekConfig(sample, "deepseek-flash", testCatalog);
            ValidateConfig(deep, ProviderKind.DeepSeek, "deepseek-flash");
            if (deep.IndexOf("sandbox_mode", StringComparison.Ordinal) < 0 || deep.IndexOf("[desktop]", StringComparison.Ordinal) < 0)
                throw new Exception("Unrelated settings were not preserved.");
            if (deep.IndexOf("experimental_bearer_token", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception("Legacy inline token setting was not removed.");
            string open = test.BuildOpenAiConfig(deep, "gpt-test", "medium");
            ValidateConfig(open, ProviderKind.OpenAi, "");
            if (open.IndexOf("model = \"gpt-test\"", StringComparison.Ordinal) < 0 ||
                open.IndexOf("model_providers.deepseek", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception("OpenAI restoration failed.");
            if (IsUsableOpenAiModel("deepseek-flash") || IsUsableOpenAiModel(""))
                throw new Exception("A DeepSeek slug would be accepted as the OpenAI baseline model.");
            if (!IsUsableOpenAiModel("gpt-5.6-sol"))
                throw new Exception("A real OpenAI model was rejected as the OpenAI baseline model.");
            string kimi = test.BuildKimiConfig(deep, "kimi-for-coding", test.GetKimiCatalogPath());
            ValidateConfig(kimi, ProviderKind.Kimi, "kimi-for-coding");
            if (kimi.IndexOf("model_providers.deepseek", StringComparison.OrdinalIgnoreCase) >= 0 ||
                kimi.IndexOf("model_providers.kimi", StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("Switching from DeepSeek to Kimi did not replace the provider section.");
            string deepAgain = test.BuildDeepSeekConfig(kimi, "deepseek-flash", testCatalog);
            ValidateConfig(deepAgain, ProviderKind.DeepSeek, "deepseek-flash");
            if (deepAgain.IndexOf("model_providers.kimi", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception("Switching from Kimi to DeepSeek kept the Kimi provider section.");
            if (IsUsableOpenAiModel("kimi-for-coding") || IsUsableOpenAiModel("k3"))
                throw new Exception("A Kimi slug would be accepted as the OpenAI baseline model.");
            ValidateCatalog(File.ReadAllText(test.catalogPath, Encoding.UTF8), ProviderKind.DeepSeek);
            ValidateCatalog(File.ReadAllText(test.kimiCatalogPath, Encoding.UTF8), ProviderKind.Kimi);
            List<ModelOption> openAiModels = test.GetModelOptions(ProviderKind.OpenAi);
            if (openAiModels.Count == 0 || openAiModels[0].Slug != string.Empty)
                throw new Exception("The future-proof Codex default model option is missing.");
            List<ModelOption> deepSeekModels = test.GetModelOptions(ProviderKind.DeepSeek);
            if (!ContainsModel(deepSeekModels, "deepseek-flash") || !ContainsModel(deepSeekModels, "deepseek-v4-pro"))
                throw new Exception("DeepSeek models were not discovered from the catalog.");
            List<ModelOption> kimiModels = test.GetModelOptions(ProviderKind.Kimi);
            if (!ContainsModel(kimiModels, "kimi-for-coding") || !ContainsModel(kimiModels, "k3-256k"))
                throw new Exception("Kimi models were not discovered from the catalog.");

            if (deepSeekModels[0].Slug != "deepseek-v4-pro")
                throw new Exception("The verified DeepSeek default was not selected.");
            if (!deep.Contains("stream_idle_timeout_ms = 60000") || !kimi.Contains("stream_max_retries = 0"))
                throw new Exception("Provider timeouts/retries were not configured.");
            string publicKimi = test.BuildKimiConfig(sample, "kimi-k3", test.GetKimiCatalogPath());
            ValidateConfig(publicKimi, ProviderKind.Kimi, "kimi-k3");
            if (!publicKimi.Contains("https://api.moonshot.cn/v1") || publicKimi.Contains("api.kimi.com"))
                throw new Exception("Public Kimi model was routed to the membership service.");
            if (!kimi.Contains("https://api.kimi.com/coding/v1"))
                throw new Exception("Kimi Code membership routing changed.");
            CredentialVault.RunSelfTest();
            RunLogAndCatalogSelfTest();
            RunAssetSelfTest();
            RunConfigFileSelfTest(test, tempRoot, deep, kimi, open);
        }

        // The icons must live inside this project folder and load from there, because the whole
        // folder gets copied to other machines. This also catches a half-copied assets folder.
        private static void RunAssetSelfTest()
        {
            if (!Directory.Exists(AppAssets.Directory))
                throw new Exception("The assets folder is missing next to the program: " + AppAssets.Directory);
            List<string> missing = AppAssets.MissingFiles();
            if (missing.Count > 0)
                throw new Exception("Missing icon assets: " + string.Join(", ", missing.ToArray()));

            string expectedRoot = Path.GetFullPath(AppAssets.AppDirectory) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(AppAssets.Directory).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Icon assets must be resolved relative to the program folder.");

            foreach (string file in new string[] { "app-icon-1024.png", "mark-chatgpt-256.png", "mark-deepseek-256.png", "mark-kimi-256.png" })
            {
                Image image = AppAssets.TryLoad(file);
                if (image == null) throw new Exception("Asset failed to load: " + file);
                if (image.Width != image.Height) throw new Exception("Asset is not square: " + file);
                if (image.Width < 128) throw new Exception("Asset is too small to scale: " + file);
            }

            Icon icon = AppAssets.LoadAppIcon();
            if (icon == null || icon.Width < 16) throw new Exception("The application icon could not be loaded.");
        }

        private static void RunLogAndCatalogSelfTest()
        {
            if (ErrorCatalog.Classify("保存密钥失败", new AppIssueException("DSK-403", "bad key", "fix")).Code != "DSK-403")
                throw new Exception("Error catalog did not keep the explicit error code.");
            if (ErrorCatalog.Classify("写入配置", new UnauthorizedAccessException("config.toml")).Code != "CFG-105")
                throw new Exception("Error catalog did not classify an unwritable config file.");
            if (ErrorCatalog.Classify("测试 DeepSeek API", new WebException("no network")).Code != "NET-501")
                throw new Exception("Error catalog did not classify a network failure.");
            string reference = ErrorCatalog.ReferenceText();
            foreach (string required in new string[] { "SYS-003", "CFG-104", "MOD-203", "OAI-304", "OAI-306", "DSK-407", "KIM-407" })
                if (reference.IndexOf(required, StringComparison.Ordinal) < 0)
                    throw new Exception("Error code catalog is missing " + required + ".");

            // Built at run time so this source file never contains anything that looks like a key.
            string secret = "self-test-secret-" + new string('x', 24);
            string sanitized = AppLog.Sanitize("保存 key=" + secret + " Authorization: Bearer " + secret);
            if (sanitized.IndexOf(secret, StringComparison.Ordinal) >= 0)
                throw new Exception("Log sanitizer leaked an API key.");
            if (sanitized.IndexOf("[REDACTED]", StringComparison.Ordinal) < 0)
                throw new Exception("Log sanitizer did not mark the removed key.");
        }

        private static void RunConfigFileSelfTest(SwitcherEngine test, string tempRoot, string deepConfig,
            string kimiConfig, string openConfig)
        {
            string codexHome = Path.Combine(tempRoot, "codex");
            Directory.CreateDirectory(codexHome);
            string configFile = Path.Combine(codexHome, "config.toml");
            File.WriteAllText(configFile, deepConfig, new UTF8Encoding(false));
            test.SetCodexDirectory(codexHome);

            // 1. A freshly written DeepSeek config must pass the integrity check.
            DiagnosticItem deepCheck = test.CheckConfigIntegrity();
            if (deepCheck.State == DiagnosticState.Failed)
                throw new Exception("Integrity check rejected a valid DeepSeek config: " + deepCheck.Detail);

            // 2. Switching back to OpenAI must also pass, and must keep the unrelated sections.
            AtomicWrite(configFile, openConfig);
            DiagnosticItem openCheck = test.CheckConfigIntegrity();
            if (openCheck.State == DiagnosticState.Failed)
                throw new Exception("Integrity check rejected a valid OpenAI config: " + openCheck.Detail);
            if (File.ReadAllText(configFile, Encoding.UTF8).IndexOf("[desktop]", StringComparison.Ordinal) < 0)
                throw new Exception("The OpenAI config lost an unrelated section.");

            AtomicWrite(configFile, kimiConfig);
            DiagnosticItem kimiCheck = test.CheckConfigIntegrity();
            if (kimiCheck.State == DiagnosticState.Failed)
                throw new Exception("Integrity check rejected a valid Kimi config: " + kimiCheck.Detail);

            string publicKimiConfig = test.BuildKimiConfig(openConfig, "kimi-k3", test.GetKimiCatalogPath());
            AtomicWrite(configFile, publicKimiConfig);
            DiagnosticItem publicCheck = test.CheckConfigIntegrity(true);
            if (publicCheck.State == DiagnosticState.Failed)
                throw new Exception("Public Kimi integrity regression: " + publicCheck.Detail);
            AtomicWrite(configFile, publicKimiConfig.Replace("https://api.moonshot.cn/v1", "https://api.kimi.com/coding/v1"));
            if (test.CheckConfigIntegrity().State != DiagnosticState.Failed)
                throw new Exception("Public Kimi accepted the Code membership endpoint.");
            AtomicWrite(configFile, kimiConfig.Replace("https://api.kimi.com/coding/v1", "https://api.moonshot.cn/v1"));
            if (test.CheckConfigIntegrity().State != DiagnosticState.Failed)
                throw new Exception("Kimi Code accepted the public endpoint.");

            // 3. Duplicated top-level keys and unterminated quotes must be reported, not ignored.
            AtomicWrite(configFile,
                deepConfig.Replace("model = \"deepseek-flash\"", "model = \"deepseek-flash\"\nmodel = \"duplicate\""));
            if (test.CheckConfigIntegrity().State != DiagnosticState.Failed)
                throw new Exception("Integrity check did not detect a duplicated top-level key.");
            AtomicWrite(configFile, "model = \"unterminated\n");
            if (test.CheckConfigIntegrity().State != DiagnosticState.Failed)
                throw new Exception("Integrity check did not detect a broken value.");
            AtomicWrite(configFile,
                "model = \"gpt-test\"\nfeatures = [\n  \"one\",\n  \"two\",\n]\nnotes = \"\"\"multi\nline\ntext\"\"\"\n");
            if (test.CheckConfigIntegrity().State == DiagnosticState.Failed)
                throw new Exception("Integrity check rejected valid TOML multi-line values.");

            // 4. Backup then restore must round-trip, which is the recovery path offered to users.
            AtomicWrite(configFile, deepConfig);
            string backupFolder = test.CreateUniqueBackupDirectory();
            Directory.CreateDirectory(backupFolder);
            File.Copy(configFile, Path.Combine(backupFolder, "config.toml"), true);
            AtomicWrite(configFile, "model = \"gpt-broken\"\n");
            test.RestoreBackup(backupFolder);
            if (!string.Equals(File.ReadAllText(configFile, Encoding.UTF8), deepConfig, StringComparison.Ordinal))
                throw new Exception("Restoring the latest backup did not restore the original config.");

            string kimiBackup = test.CreateUniqueBackupDirectory();
            Directory.CreateDirectory(kimiBackup);
            File.WriteAllText(Path.Combine(kimiBackup, "config.toml"), kimiConfig, new UTF8Encoding(false));
            AtomicWrite(configFile, openConfig);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("KIMI_API_KEY", null, EnvironmentVariableTarget.Process);
            test.RestoreBackup(kimiBackup);
            if (test.ReadCurrentConfiguration().Provider != ProviderKind.Kimi ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.Process)) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KIMI_API_KEY", EnvironmentVariableTarget.Process)))
                throw new Exception("Restoring a Kimi backup without a key left a stale provider environment variable.");

            // 5. A config that lost sections present in the newest backup must be flagged as CFG-104.
            AtomicWrite(configFile, "model = \"gpt-test\"\nmodel_reasoning_effort = \"medium\"\n");
            DiagnosticItem lostSections = test.CheckConfigIntegrity(true);
            if (lostSections.State != DiagnosticState.Failed || lostSections.Code != "CFG-104")
                throw new Exception("Integrity check did not detect sections lost since the last backup.");
            if (test.CheckConfigIntegrity().State == DiagnosticState.Failed)
                throw new Exception("Non-strict integrity check must not fail on sections the app may rewrite itself.");

            // 6. Log and Codex-log inspection must work without touching the network.
            test.InspectRecentIssues();
            AppLog.Info("APP-000", "自检", "自检写入日志成功。");
            if (string.IsNullOrEmpty(AppLog.LogsDirectory) || !Directory.Exists(AppLog.LogsDirectory))
                throw new Exception("The log directory was not created.");

            // 7. Exercise every directed edge among OpenAI, DeepSeek and Kimi. For OpenAI, pass the
            //    previous third-party model deliberately: the switcher must fall back safely.
            AtomicWrite(configFile, deepConfig);
            ProviderKind[] targets = new ProviderKind[]
            {
                ProviderKind.OpenAi, ProviderKind.DeepSeek, ProviderKind.Kimi,
                ProviderKind.OpenAi, ProviderKind.Kimi, ProviderKind.DeepSeek, ProviderKind.OpenAi,
                ProviderKind.Kimi, ProviderKind.Kimi, ProviderKind.Kimi, ProviderKind.Kimi,
                ProviderKind.DeepSeek, ProviderKind.Kimi, ProviderKind.OpenAi
            };
            string[] requested = new string[]
            {
                "deepseek-flash", "deepseek-flash", "kimi-for-coding",
                "kimi-for-coding", "k3-256k", "deepseek-flash", "deepseek-flash",
                "kimi-k3", "kimi-k3", "kimi-for-coding", "kimi-k3",
                "deepseek-v4-pro", "kimi-k3", "kimi-k3"
            };
            for (int i = 0; i < targets.Length; i++)
            {
                Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sk-" + new string('d', 32), EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("KIMI_API_KEY", "sk-" + new string('k', 32), EnvironmentVariableTarget.Process);
                string switchBackup = test.Apply(targets[i], requested[i]);
                if (string.IsNullOrEmpty(switchBackup) || !Directory.Exists(switchBackup))
                    throw new Exception("Provider transition did not create a backup: " + targets[i].ToString());
                CurrentConfiguration afterSwitch = test.ReadCurrentConfiguration();
                if (afterSwitch.Provider != targets[i])
                    throw new Exception("Provider transition selected the wrong provider: " + targets[i].ToString());
                if (targets[i] == ProviderKind.Kimi && afterSwitch.Model != requested[i])
                    throw new Exception("Kimi switch silently rolled back or selected another model.");
                if (targets[i] == ProviderKind.OpenAi && !string.IsNullOrEmpty(afterSwitch.Model) &&
                    !IsUsableOpenAiModel(afterSwitch.Model))
                    throw new Exception("Switching back to OpenAI kept a third-party model ID: " + afterSwitch.Model);
                DiagnosticItem transitionCheck = test.CheckConfigIntegrity(true);
                if (transitionCheck.State == DiagnosticState.Failed)
                    throw new Exception("Config integrity failed after transition to " + targets[i].ToString() +
                        ": " + transitionCheck.Detail);
            }
            foreach (string effort in new string[] { "none", "low", "high", "max", "none" })
            {
                Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sk-" + new string('d', 32), EnvironmentVariableTarget.Process);
                test.DeepSeekReasoningEffort = effort;
                test.Apply(ProviderKind.DeepSeek, "deepseek-v4-pro");
                if (test.ReadCurrentConfiguration().ReasoningEffort != effort || test.CheckConfigIntegrity(true).State == DiagnosticState.Failed)
                    throw new Exception("DeepSeek mode did not survive the full switch: " + effort);
            }
            test.Apply(ProviderKind.OpenAi, "gpt-test");
            if (test.ReadCurrentConfiguration().ReasoningEffort == "none")
                throw new Exception("DeepSeek mode leaked into OpenAI configuration.");
            AtomicWrite(configFile, publicKimiConfig);
            Environment.SetEnvironmentVariable("KIMI_API_KEY", "sk-" + new string('k', 32), EnvironmentVariableTarget.Process);
            string publicBackup = test.Apply(ProviderKind.OpenAi, string.Empty);
            test.RestoreBackup(publicBackup);
            if (test.ReadCurrentConfiguration().Model != "kimi-k3" || test.CheckConfigIntegrity(true).State == DiagnosticState.Failed)
                throw new Exception("Restoring public Kimi backup failed.");
        }
    }

    internal sealed class CurrentConfiguration
    {
        public ProviderKind Provider { get; private set; }
        public bool IsDeepSeek { get { return Provider == ProviderKind.DeepSeek; } }
        public bool IsKimi { get { return Provider == ProviderKind.Kimi; } }
        public string Model { get; private set; }
        public string ReasoningEffort { get; private set; }

        public CurrentConfiguration(ProviderKind provider, string model, string reasoningEffort)
        {
            Provider = provider;
            Model = model ?? string.Empty;
            ReasoningEffort = reasoningEffort ?? string.Empty;
        }
    }

    internal sealed class ModelOption
    {
        public string Label { get; private set; }
        public string Slug { get; private set; }

        public ModelOption(string label, string slug)
        {
            Label = label;
            Slug = slug;
        }

        public override string ToString() { return Label; }
    }

    internal sealed class RestartResult
    {
        public bool Launched { get; private set; }
        public string Message { get; private set; }

        public RestartResult(bool launched, string message)
        {
            Launched = launched;
            Message = message ?? string.Empty;
        }
    }

    internal sealed class AppIssueException : Exception
    {
        public string Code { get; private set; }
        public string Suggestion { get; private set; }

        public AppIssueException(string code, string message, string suggestion)
            : base(message)
        {
            Code = code;
            Suggestion = suggestion;
        }

        public AppIssueException(string code, string message, string suggestion, Exception inner)
            : base(message, inner)
        {
            Code = code;
            Suggestion = suggestion;
        }
    }

    internal sealed class ErrorInfo
    {
        public string Code { get; private set; }
        public string Message { get; private set; }
        public string Suggestion { get; private set; }

        public ErrorInfo(string code, string message, string suggestion)
        {
            Code = code;
            Message = message;
            Suggestion = suggestion;
        }
    }

    internal static class ErrorCatalog
    {
        public static ErrorInfo Classify(string context, Exception exception)
        {
            AppIssueException issue = exception as AppIssueException;
            if (issue != null) return new ErrorInfo(issue.Code, issue.Message, issue.Suggestion);

            string message = exception == null ? "未知错误。" : exception.Message;
            string lower = (context + " " + message).ToLowerInvariant();
            if (lower.IndexOf("应用程序控制策略") >= 0 || lower.IndexOf("smart app control") >= 0 ||
                lower.IndexOf("code integrity") >= 0 || lower.IndexOf("enterprise signing level") >= 0)
                return new ErrorInfo("SYS-003", "Windows 应用控制策略阻止了本程序或其启动的组件。",
                    "这是 Windows 安全策略（Smart App Control / WDAC）对未签名程序的限制，不是程序内部错误。请使用桌面的统一快捷方式；它会在正式 EXE 失败时自动转入 PowerShell 兼容模式。正式分发应使用受信任的代码签名证书。");
            if (exception is UnauthorizedAccessException)
            {
                if (lower.IndexOf("config.toml") >= 0)
                    return new ErrorInfo("CFG-105", message, "关闭占用配置文件的 ChatGPT/Codex 后重试，并确认当前用户对配置目录有写入权限。");
                return new ErrorInfo("IO-601", message, "检查当前 Windows 用户是否有权读取和写入所选目录，或关闭占用配置文件的程序后重试。");
            }
            if (exception is IOException &&
                (lower.IndexOf("正在使用") >= 0 || lower.IndexOf("being used") >= 0 || lower.IndexOf("拒绝访问") >= 0))
                return new ErrorInfo("CFG-105", message, "关闭占用配置文件的 ChatGPT/Codex 后重试；也可以先“只保存配置”，再手动重启应用。");
            if (exception is DirectoryNotFoundException)
                return new ErrorInfo("CFG-101", message, "在设置中点击“自动寻找”，或手工选择实际使用的 config.toml。");
            if (exception is FileNotFoundException &&
                (lower.IndexOf("catalog") >= 0 || lower.IndexOf("模型目录") >= 0))
                return new ErrorInfo("MOD-201", message, "刷新模型目录；如文件缺失，重新复制完整的软件目录、deepseek-models.json 和 kimi-models.json。");
            if (exception is FileNotFoundException)
                return new ErrorInfo("CFG-102", message, "在设置中重新选择有效的 config.toml，并确认文件没有被移动或删除。");
            if (exception is InvalidDataException || exception is FormatException)
                return new ErrorInfo("CFG-103", message, "检查 config.toml 或模型目录格式；可先在设置中恢复最近备份。");
            if (exception is WebException)
                return new ErrorInfo("NET-501", message, "检查网络、代理、防火墙、DNS 和系统时间，然后重新测试。");
            if (exception is System.ComponentModel.Win32Exception)
                return new ErrorInfo("SYS-002", message, "确认 Windows 凭据管理器、目标程序和当前用户权限可用。");
            if (lower.IndexOf("chatgpt") >= 0 && (lower.IndexOf("启动") >= 0 || lower.IndexOf("入口") >= 0))
                return new ErrorInfo("OAI-301", message, "在设置中点击“自动检测”，或手工选择正确的 ChatGPT.exe。");
            if (lower.IndexOf("deepseek") >= 0 && lower.IndexOf("key") >= 0)
                return new ErrorInfo("DSK-401", message, "输入有效的 DeepSeek API Key 并点击“加密保存”，再运行 API 测试。");
            if (lower.IndexOf("kimi") >= 0 && lower.IndexOf("key") >= 0)
                return new ErrorInfo("KIM-401", message, "输入有效的 Kimi API Key 并点击“加密保存”，再运行 API 测试。");
            return new ErrorInfo("APP-701", message, "打开“诊断与日志”运行检查，并根据报告编号查看对应日志。");
        }

        public static string ReferenceText()
        {
            return
                "【系统与环境】\r\n" +
                "SYS-001  Windows 版本过旧或被判为不受支持\r\n" +
                "SYS-002  Windows 组件、凭据管理器或用户权限不可用\r\n" +
                "SYS-003  程序被 Windows 应用控制策略（Smart App Control / WDAC）阻止\r\n" +
                "【配置与文件】\r\n" +
                "CFG-101  未找到 Codex 配置目录\r\n" +
                "CFG-102  未找到 config.toml\r\n" +
                "CFG-103  配置格式、读取或写入失败\r\n" +
                "CFG-104  配置完整性检查失败（上一次切换可能被中断）\r\n" +
                "CFG-105  config.toml 被占用、只读或权限不足\r\n" +
                "IO-601   目录权限、磁盘空间或安全软件拦截\r\n" +
                "【模型目录】\r\n" +
                "MOD-201  模型目录缺失或损坏\r\n" +
                "MOD-202  所选模型不在本机模型目录中\r\n" +
                "MOD-203  模型列表为空（需要刷新或重新安装程序目录）\r\n" +
                "【OpenAI / ChatGPT】\r\n" +
                "OAI-301  未找到或无法启动 ChatGPT 桌面应用\r\n" +
                "OAI-302  无法确认 OpenAI / ChatGPT 登录状态（未登录或凭据不可见）\r\n" +
                "OAI-303  账户、工作区、订阅或额度不确定，需要人工确认\r\n" +
                "OAI-304  ChatGPT 登录已失效（服务器返回 401）\r\n" +
                "OAI-305  额度用尽或请求频率受限（429 / usage limit）\r\n" +
                "OAI-306  账户、工作区或订阅无权限（403）\r\n" +
                "OAI-307  当前账户无法使用所选模型\r\n" +
                "【DeepSeek】\r\n" +
                "DSK-401  尚未保存 DeepSeek API Key\r\n" +
                "DSK-402  DeepSeek API Key 格式错误\r\n" +
                "DSK-403  DeepSeek API Key 无效、被撤销或不属于该账户\r\n" +
                "DSK-404  DeepSeek 余额或计费状态异常\r\n" +
                "DSK-405  DeepSeek 请求频率受限\r\n" +
                "DSK-406  DeepSeek 服务端异常或返回意外状态\r\n" +
                "DSK-407  DeepSeek 模型或完整回复不可用\r\n" +
                "DSK-408  DeepSeek 推理测试超时\r\n" +
                "【Kimi Code】\r\n" +
                "KIM-401  尚未保存 Kimi API Key\r\n" +
                "KIM-402  Kimi API Key 格式错误\r\n" +
                "KIM-403  Kimi API Key 无效、被撤销或会员无权限\r\n" +
                "KIM-404  Kimi Code 额度或计费状态异常\r\n" +
                "KIM-405  Kimi Code 请求频率或配额受限\r\n" +
                "KIM-406  Kimi Code 服务端异常或返回意外状态\r\n" +
                "KIM-407  Kimi 模型、端点或 Responses 请求不可用\r\n" +
                "KIM-408  Kimi 推理测试超时\r\n" +
                "【网络】\r\n" +
                "NET-501  网络、代理、防火墙、DNS 或 TLS 连接失败\r\n" +
                "【其他】\r\n" +
                "APP-702  图标素材缺失（界面会退回默认图形）\r\n" +
                "APP-701  未分类的程序错误（请查看报告编号对应的日志）";
        }
    }

    internal static class AppLog
    {
        private static readonly object Sync = new object();
        private static string logsDirectory = string.Empty;
        private static string reportsDirectory = string.Empty;

        public static string LogsDirectory { get { return logsDirectory; } }

        public static void Initialize(string dataDirectory)
        {
            logsDirectory = Path.Combine(dataDirectory, "logs");
            reportsDirectory = Path.Combine(dataDirectory, "reports");
            Directory.CreateDirectory(logsDirectory);
            Directory.CreateDirectory(reportsDirectory);
        }

        public static void Info(string code, string action, string detail)
        {
            Write("INFO", code, action, detail, null);
        }

        public static void Warning(string code, string action, string detail)
        {
            Write("WARN", code, action, detail, null);
        }

        public static string RecordError(string context, ErrorInfo info, Exception exception, out string reportId)
        {
            reportId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
            Write("ERROR", info.Code, context, info.Message + " | 报告编号=" + reportId, exception);
            string path = Path.Combine(reportsDirectory, "error-" + reportId + ".txt");
            StringBuilder report = new StringBuilder();
            report.AppendLine("Codex 模型切换器错误报告");
            report.AppendLine("报告编号：" + reportId);
            report.AppendLine("错误编号：" + info.Code);
            report.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            report.AppendLine("操作：" + Sanitize(context));
            report.AppendLine("原因：" + Sanitize(info.Message));
            report.AppendLine("建议：" + Sanitize(info.Suggestion));
            report.AppendLine("系统：" + Environment.OSVersion.VersionString + "; " +
                (Environment.Is64BitOperatingSystem ? "64 位 Windows" : "32 位 Windows"));
            report.AppendLine("程序版本：" + Assembly.GetExecutingAssembly().GetName().Version.ToString());
            report.AppendLine();
            report.AppendLine("技术信息（已脱敏）：");
            report.AppendLine(Sanitize(exception == null ? string.Empty : exception.ToString()));
            try { AtomicTextWrite(path, report.ToString()); }
            catch { }
            return path;
        }

        public static string SaveDiagnosticReport(string content)
        {
            string path = Path.Combine(reportsDirectory, "diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            AtomicTextWrite(path, Sanitize(content));
            Info("APP-000", "诊断", "已生成诊断报告：" + path);
            return path;
        }

        private static void Write(string level, string code, string action, string detail, Exception exception)
        {
            try
            {
                lock (Sync)
                {
                    if (string.IsNullOrEmpty(logsDirectory)) return;
                    Directory.CreateDirectory(logsDirectory);
                    string path = Path.Combine(logsDirectory, "switcher-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + " [" + level + "] [" +
                        code + "] " + Sanitize(action) + " | " + Sanitize(detail);
                    if (exception != null) line += " | " + Sanitize(exception.GetType().Name + ": " + exception.Message);
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        private static void AtomicTextWrite(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        internal static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string safe = Regex.Replace(value, "sk-[A-Za-z0-9_-]{8,}", "sk-[REDACTED]", RegexOptions.IgnoreCase);
            safe = Regex.Replace(safe, "(?i)(\\bkey\\s*[:=]\\s*)[^\\s,;]+", "$1[REDACTED]");
            safe = Regex.Replace(safe, "(?i)(authorization\\s*[:=]\\s*bearer\\s+)[^\\s,;]+", "$1[REDACTED]");
            safe = Regex.Replace(safe, "(?i)(DEEPSEEK_API_KEY\\s*[:=]\\s*)[^\\s,;]+", "$1[REDACTED]");
            safe = Regex.Replace(safe, "(?i)(KIMI_API_KEY\\s*[:=]\\s*)[^\\s,;]+", "$1[REDACTED]");
            return safe;
        }
    }

    internal static class ErrorReporter
    {
        public static ErrorInfo Show(IWin32Window owner, string context, Exception exception)
        {
            ErrorInfo info = ErrorCatalog.Classify(context, exception);
            string reportId;
            string reportPath = AppLog.RecordError(context, info, exception, out reportId);
            MessageBox.Show(owner,
                "错误编号：" + info.Code + "\r\n" +
                "报告编号：" + reportId + "\r\n\r\n" +
                "原因：" + info.Message + "\r\n\r\n" +
                "处理建议：" + info.Suggestion + "\r\n\r\n" +
                "报告位置：" + reportPath,
                context, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return info;
        }
    }

    internal enum DiagnosticState { Passed, Warning, Failed }

    internal sealed class DiagnosticItem
    {
        public DiagnosticState State { get; private set; }
        public string Code { get; private set; }
        public string Name { get; private set; }
        public string Detail { get; private set; }
        public string Suggestion { get; private set; }

        public DiagnosticItem(DiagnosticState state, string code, string name, string detail, string suggestion)
        {
            State = state;
            Code = code;
            Name = name;
            Detail = detail;
            Suggestion = suggestion;
        }
    }

    // Uses the Windows Credential Manager instead of placing the persistent secret in an
    // application file. Generic credentials are scoped to the current Windows user.
    internal static class CredentialVault
    {
        private const uint CredTypeGeneric = 1;
        private const uint CredPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;
        private const string DeepSeekTarget = "CodexModelSwitcher/DeepSeekApiKey";
        private const string KimiTarget = "CodexModelSwitcher/KimiApiKey";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credential);

        public static bool HasDeepSeekKey()
        {
            return !string.IsNullOrEmpty(Read(DeepSeekTarget));
        }

        public static string ReadDeepSeekKey()
        {
            return Read(DeepSeekTarget);
        }

        public static void WriteDeepSeekKey(string key)
        {
            Write(DeepSeekTarget, key);
        }

        public static void DeleteDeepSeekKey()
        {
            Delete(DeepSeekTarget);
        }

        public static bool HasKimiKey()
        {
            return !string.IsNullOrEmpty(Read(KimiTarget));
        }

        public static string ReadKimiKey()
        {
            return Read(KimiTarget);
        }

        public static void WriteKimiKey(string key)
        {
            Write(KimiTarget, key);
        }

        public static void DeleteKimiKey()
        {
            Delete(KimiTarget);
        }

        private static string Read(string target)
        {
            IntPtr pointer;
            if (!CredRead(target, CredTypeGeneric, 0, out pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return string.Empty;
                throw new System.ComponentModel.Win32Exception(error, "无法从 Windows 凭据管理器读取密钥。");
            }

            try
            {
                NativeCredential credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                    return string.Empty;
                return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }
            finally { CredFree(pointer); }
        }

        private static void Write(string target, string secret)
        {
            if (string.IsNullOrEmpty(secret)) throw new ArgumentException("密钥不能为空。");
            byte[] bytes = Encoding.Unicode.GetBytes(secret);
            IntPtr blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                NativeCredential credential = new NativeCredential();
                credential.Type = CredTypeGeneric;
                credential.TargetName = target;
                credential.Comment = "Codex 模型切换器保存的 DeepSeek API Key";
                credential.CredentialBlobSize = (uint)bytes.Length;
                credential.CredentialBlob = blob;
                credential.Persist = CredPersistLocalMachine;
                credential.UserName = Environment.UserName;
                if (!CredWrite(ref credential, 0))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                        "无法保存到 Windows 凭据管理器。");
            }
            finally
            {
                for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
                Array.Clear(bytes, 0, bytes.Length);
                Marshal.FreeCoTaskMem(blob);
            }
        }

        private static void Delete(string target)
        {
            if (CredDelete(target, CredTypeGeneric, 0)) return;
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new System.ComponentModel.Win32Exception(error, "无法从 Windows 凭据管理器删除密钥。");
        }

        public static void RunSelfTest()
        {
            string target = "CodexModelSwitcher/SelfTest/" + Guid.NewGuid().ToString("N");
            string expected = "self-test-credential-placeholder-1234567890";
            try
            {
                Write(target, expected);
                if (!string.Equals(Read(target), expected, StringComparison.Ordinal))
                    throw new Exception("Windows 凭据管理器读写自检失败。");
            }
            finally { Delete(target); }
        }
    }

    internal static class NativeMethods
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int ErrorInsufficientBuffer = 122;
        private const uint WmSettingChange = 0x001A;
        private const int EmSetCueBanner = 0x1501;
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint applicationUserModelIdLength, StringBuilder applicationUserModelId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
            string lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        public static void SetCueText(TextBox textBox, string cueText)
        {
            if (textBox == null || textBox.IsDisposed) return;
            SendMessage(textBox.Handle, EmSetCueBanner, new IntPtr(1), cueText ?? string.Empty);
        }

        public static string TryGetProcessPath(Process process)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
                if (handle == IntPtr.Zero) return string.Empty;
                int size = 32768;
                StringBuilder value = new StringBuilder(size);
                return QueryFullProcessImageName(handle, 0, value, ref size) ? value.ToString() : string.Empty;
            }
            catch { return string.Empty; }
            finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
        }

        public static string TryGetApplicationUserModelId(Process process)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
                if (handle == IntPtr.Zero) return string.Empty;
                uint length = 0;
                int first = GetApplicationUserModelId(handle, ref length, null);
                if (first != ErrorInsufficientBuffer || length == 0) return string.Empty;
                StringBuilder value = new StringBuilder((int)length);
                int second = GetApplicationUserModelId(handle, ref length, value);
                return second == 0 ? value.ToString() : string.Empty;
            }
            catch { return string.Empty; }
            finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
        }

        public static void BroadcastEnvironmentChange()
        {
            IntPtr result;
            SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", 0x0002, 3000, out result);
        }
    }
}

