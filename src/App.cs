using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Edge 扩展提醒延期")]
[assembly: AssemblyDescription("设置 Microsoft Edge 开发者模式扩展的下次提醒时间")]
[assembly: AssemblyProduct("Edge 扩展提醒延期")]
[assembly: AssemblyCompany("Independent utility")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace EdgeReminder {
    internal static class Program {
        [STAThread] static void Main(string[] args) {
            bool created; using (Mutex mutex = new Mutex(true, "Local\\EdgeReminder.App", out created)) {
                if (!created) { MessageBox.Show("Edge 提醒延期已在运行。", "Edge 提醒延期"); return; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error); };
                Application.Run(new MainForm(args));
            }
        }
    }
    internal sealed class Header : Control {
        public Header() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = g.DpiX / 96f;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(196, 86, 34))) g.FillEllipse(b, 0, 10 * scale, 48 * scale, 48 * scale);
            using (Pen pen = new Pen(Color.White, 2.8f * scale)) { g.DrawEllipse(pen, 12 * scale, 22 * scale, 24 * scale, 24 * scale); g.DrawLines(pen, new[] { new PointF(24 * scale, 27 * scale), new PointF(24 * scale, 35 * scale), new PointF(31 * scale, 38 * scale) }); }
            using (Font title = new Font("Microsoft YaHei UI", 21, FontStyle.Bold)) TextRenderer.DrawText(g, "Edge 扩展提醒延期", title, new Point((int)(62 * scale), (int)(1 * scale)), Color.FromArgb(51, 42, 35));
            using (Font sub = new Font("Microsoft YaHei UI", 9.5f)) TextRenderer.DrawText(g, "自定义开发者模式扩展提醒，最多延后 1000 周", sub, new Point((int)(65 * scale), (int)(45 * scale)), Color.FromArgb(125, 107, 93));
        }
    }
    internal sealed class RoundedButton : Button {
        bool hover, down;
        public RoundedButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
            float radius = 10f * g.DpiX / 96f, diameter = radius * 2;
            RectangleF rect = new RectangleF(1, 1, Width - 3, Height - 3); diameter = Math.Min(diameter, Math.Min(rect.Width, rect.Height));
            using (GraphicsPath path = new GraphicsPath()) {
                path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90); path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
                path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
                Color fill = !Enabled ? Color.FromArgb(236, 231, 226) : down ? ControlPaint.Dark(BackColor, .07f) : hover ? (BackColor == Color.White ? Color.FromArgb(255, 242, 231) : ControlPaint.Light(BackColor, .09f)) : BackColor;
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                using (Pen p = new Pen(Focused && Enabled ? Color.FromArgb(196, 86, 34) : FlatAppearance.BorderColor, Focused ? 2f : 1f)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(153, 143, 134), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
    internal sealed class MainForm : Form {
        readonly Color Ink = Color.FromArgb(51, 42, 35), Muted = Color.FromArgb(125, 107, 93), Blue = Color.FromArgb(196, 86, 34);
        readonly List<string> manual = new List<string>();
        readonly ListView profiles = new ListView(); readonly NumericUpDown weeks = new NumericUpDown();
        readonly Label pathLabel = new Label(), preview = new Label(), status = new Label(), processLabel = new Label();
        readonly Button apply = new RoundedButton(), restore = new RoundedButton(), refresh = new RoundedButton(), closeEdge = new RoundedButton();
        readonly ToolTip tips = new ToolTip(); readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool busy; List<Profile> discovered = new List<Profile>(); bool testOnly;
        public MainForm(string[] args) {
            Text = "Edge 扩展提醒延期"; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9.5f); ForeColor = Ink; BackColor = Color.FromArgb(250, 247, 243);
            ClientSize = new Size(920, 652);
            Icon = BuildIcon();
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--profile" && i + 1 < args.Length) manual.Add(args[++i]);
                if (args[i] == "--no-discover") testOnly = true;
            }
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 18, 26, 16), ColumnCount = 1, RowCount = 7 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            Controls.Add(root); root.Controls.Add(new Header { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
            TableLayoutPanel toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); for (int i = 0; i < 3; i++) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            toolbar.Controls.Add(Label("01   选择 Edge 配置", true), 0, 0);
            Style(refresh, "刷新", false); refresh.Click += delegate { RefreshProfiles(); }; toolbar.Controls.Add(refresh, 1, 0);
            Button folder = Button("选择目录", false); folder.Click += delegate { AddFolder(); }; toolbar.Controls.Add(folder, 2, 0);
            Button file = Button("选择文件", false); file.Click += delegate { AddFile(); }; toolbar.Controls.Add(file, 3, 0); root.Controls.Add(toolbar, 0, 1);
            profiles.Dock = DockStyle.Fill; profiles.Margin = Padding.Empty; profiles.View = View.Details; profiles.CheckBoxes = true;
            profiles.FullRowSelect = true; profiles.HideSelection = false; profiles.MultiSelect = false; profiles.BorderStyle = BorderStyle.FixedSingle;
            profiles.BackColor = Color.White; profiles.HeaderStyle = ColumnHeaderStyle.Nonclickable; profiles.AccessibleName = "要修改的 Edge 配置，可勾选多个";
            profiles.Columns.Add("配置名称", 226); profiles.Columns.Add("Edge 通道", 98); profiles.Columns.Add("当前提醒时间", 192); profiles.Columns.Add("配置状态", 240);
            profiles.Resize += delegate { int width = Math.Max(300, profiles.ClientSize.Width - 4); profiles.Columns[0].Width = width * 28 / 100; profiles.Columns[1].Width = width * 17 / 100; profiles.Columns[2].Width = width * 25 / 100; profiles.Columns[3].Width = width - profiles.Columns[0].Width - profiles.Columns[1].Width - profiles.Columns[2].Width; };
            profiles.SelectedIndexChanged += delegate { UpdatePath(); }; profiles.ItemChecked += delegate { UpdateActions(); }; root.Controls.Add(profiles, 0, 2);
            pathLabel.Dock = DockStyle.Fill; pathLabel.ForeColor = Muted; pathLabel.TextAlign = ContentAlignment.MiddleLeft; pathLabel.AutoEllipsis = true; pathLabel.Margin = new Padding(0, 0, 0, 4); root.Controls.Add(pathLabel, 0, 3);
            Panel setting = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 2, 0, 0), Padding = new Padding(16) };
            setting.Paint += delegate(object sender, PaintEventArgs e) { using (Pen p = new Pen(Color.FromArgb(232, 219, 207))) e.Graphics.DrawRectangle(p, 0, 0, setting.Width - 1, setting.Height - 1); };
            TableLayoutPanel settingGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            settingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66)); settingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); settingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 49)); settingGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            setting.Controls.Add(settingGrid); settingGrid.Controls.Add(Label("02   设置提醒间隔", true), 0, 0);
            Label dateTitle = Label("预计下次提醒", false); dateTitle.ForeColor = Muted; settingGrid.Controls.Add(dateTitle, 1, 0);
            FlowLayoutPanel input = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            weeks.Minimum = 1; weeks.Maximum = 1000; weeks.Value = 999; weeks.Width = 119; weeks.Font = new Font("Microsoft YaHei UI", 20, FontStyle.Bold);
            weeks.BorderStyle = BorderStyle.FixedSingle; weeks.AccessibleName = "延期周数，1 到 1000"; weeks.ValueChanged += delegate { UpdatePreview(); };
            input.Controls.Add(weeks); input.Controls.Add(new Label { Text = "周后", AutoSize = true, Padding = new Padding(2, 11, 10, 0) });
            foreach (int n in new[] { 2, 52, 999, 1000 }) {
                int chosen = n; Button preset = Button(n.ToString(), false); preset.Width = 62; preset.Height = 32; preset.Dock = DockStyle.None; preset.Margin = new Padding(3, 5, 0, 0);
                preset.AccessibleName = "设为 " + n + " 周"; preset.Click += delegate { weeks.Value = chosen; }; input.Controls.Add(preset);
            }
            settingGrid.Controls.Add(input, 0, 1);
            preview.Dock = DockStyle.Fill; preview.Font = new Font("Microsoft YaHei UI", 19, FontStyle.Bold); preview.ForeColor = Blue; preview.TextAlign = ContentAlignment.MiddleLeft;
            settingGrid.Controls.Add(preview, 1, 1);
            Label range = Label("可输入 1～1000 周，从点击应用的时间起算。", false); range.ForeColor = Muted; range.Font = new Font(Font.FontFamily, 8.5f); settingGrid.Controls.Add(range, 0, 2);
            Label future = Label("实际弹出还取决于 Edge 的运行时间", false); future.ForeColor = Muted; future.Font = new Font(Font.FontFamily, 8.5f); settingGrid.Controls.Add(future, 1, 2);
            root.Controls.Add(setting, 0, 4);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 14, 0, 7) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 119)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 139)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            processLabel.Dock = DockStyle.Fill; processLabel.TextAlign = ContentAlignment.MiddleLeft; processLabel.ForeColor = Muted; actions.Controls.Add(processLabel, 0, 0);
            Style(closeEdge, "关闭 Edge…", false); closeEdge.Click += delegate { CloseEdge(); }; actions.Controls.Add(closeEdge, 1, 0);
            Style(restore, "撤销上次修改", false); restore.Click += delegate { RestoreSelected(); }; actions.Controls.Add(restore, 2, 0);
            Style(apply, "应用提醒时间", true); apply.Click += delegate { ApplySelected(); }; actions.Controls.Add(apply, 3, 0); root.Controls.Add(actions, 0, 5);
            TableLayoutPanel bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            status.Dock = DockStyle.Fill; status.Text = "正在查找 Edge 配置…"; status.ForeColor = Muted; status.Font = new Font(Font.FontFamily, 8.5f); status.Padding = new Padding(0, 4, 0, 0);
            bottom.Controls.Add(status, 0, 0); Button help = Button("说明与备份", false); help.Margin = new Padding(8, 0, 0, 18);
            help.Click += delegate { ShowHelp(); }; bottom.Controls.Add(help, 1, 0); root.Controls.Add(bottom, 0, 6);
            Shown += delegate { RefreshProfiles(); }; timer.Interval = 2500; timer.Tick += delegate { if (!busy) UpdateProcess(); }; timer.Start();
            FormClosed += delegate { timer.Dispose(); tips.Dispose(); }; UpdatePreview();
            // One explicit DPI scale avoids mixing the form's runtime font metrics with design-time autoscaling.
            float scale; using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            if (Math.Abs(scale - 1f) > .01f) Scale(new SizeF(scale, scale));
            Rectangle area = Screen.FromControl(this).WorkingArea;
            ClientSize = new Size(Math.Min((int)(920 * scale), area.Width - 40), Math.Min((int)(652 * scale), area.Height - (int)(55 * scale)));
            MinimumSize = new Size(Math.Min((int)(860 * scale), Width), Math.Min((int)(630 * scale), Height));
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        internal static Icon BuildIcon() {
            using (Bitmap bmp = new Bitmap(64, 64)) { using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(196, 86, 34))) g.FillEllipse(b, 2, 2, 60, 60);
                using (Pen p = new Pen(Color.White, 4)) { g.DrawEllipse(p, 15, 15, 34, 34); g.DrawLines(p, new[] { new Point(32, 21), new Point(32, 33), new Point(42, 39) }); }
            } IntPtr handle = bmp.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); } }
        }
        Label Label(string text, bool bold) { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = new Font(Font, bold ? FontStyle.Bold : FontStyle.Regular), Margin = Padding.Empty }; }
        Button Button(string text, bool primary) { Button b = new RoundedButton(); Style(b, text, primary); return b; }
        void Style(Button b, string text, bool primary) {
            b.Text = text; b.Dock = DockStyle.Fill; b.Margin = new Padding(6, 2, 0, 5); b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(226, 207, 190); b.FlatAppearance.BorderSize = 1;
            b.BackColor = primary ? Blue : Color.White; b.ForeColor = primary ? Color.White : Ink; b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false; b.AccessibleName = text;
        }
        void UpdatePreview() { preview.Text = DateTime.Now.AddDays((int)weeks.Value * 7).ToString("yyyy-MM-dd"); }
        void UpdateActions() { apply.Enabled = !busy && profiles.CheckedItems.Count > 0; restore.Enabled = !busy && profiles.CheckedItems.Count > 0; refresh.Enabled = !busy; closeEdge.Enabled = !busy; }
        void SetBusy(bool value) { busy = value; profiles.Enabled = !value; weeks.Enabled = !value; UpdateActions(); UseWaitCursor = value; }
        void UpdateProcess() {
            List<Process> list = Engine.Processes(); int count = list.Count; foreach (Process p in list) p.Dispose();
            processLabel.Text = count == 0 ? "●  Edge 已退出，可以修改" : "●  Edge 正在运行，请先关闭";
            processLabel.ForeColor = count == 0 ? Color.FromArgb(35, 125, 93) : Color.FromArgb(168, 109, 36);
        }
        async void RefreshProfiles() {
            if (busy) return; HashSet<string> checkedPaths = new HashSet<string>(profiles.CheckedItems.Cast<ListViewItem>().Select(delegate(ListViewItem i) { return ((Profile)i.Tag).Path; }), StringComparer.OrdinalIgnoreCase);
            bool initial = profiles.Items.Count == 0; SetBusy(true); string[] additions = manual.ToArray();
            try {
                discovered = await Task.Run(delegate { return testOnly ? additions.SelectMany(delegate(string path) { return Discovery.InRoot(path, "测试配置"); }).ToList() : Discovery.Scan(additions); });
                profiles.BeginUpdate(); profiles.Items.Clear(); foreach (Profile p in discovered) {
                    ListViewItem item = new ListViewItem(new[] { p.Name, p.Channel, p.Reminder, p.Note }); item.Tag = p;
                    item.Checked = checkedPaths.Contains(p.Path) || (initial && p.Note == "开发者模式已开启"); profiles.Items.Add(item);
                }
                if (initial && profiles.CheckedItems.Count == 0 && profiles.Items.Count == 1) profiles.Items[0].Checked = true;
                if (profiles.Items.Count > 0) profiles.Items[0].Selected = true; profiles.EndUpdate();
                status.Text = discovered.Count == 0 ? "没有找到配置。可用“选择目录”添加 Edge 用户数据目录，或直接选择 Preferences 文件。" : "已找到 " + discovered.Count + " 个配置。修改前自动备份；保留开发者模式和扩展设置。";
                UpdatePath(); UpdateProcess();
            } catch (Exception ex) { status.Text = "查找未完成：" + ex.Message; } finally { SetBusy(false); }
        }
        void UpdatePath() {
            Profile p = profiles.SelectedItems.Count > 0 ? profiles.SelectedItems[0].Tag as Profile : null;
            pathLabel.Text = p == null ? "选中一行可查看完整配置路径" : "路径：" + p.Path; tips.SetToolTip(pathLabel, p == null ? "" : p.Path);
        }
        void AddFolder() {
            if (busy) return; using (FolderBrowserDialog dlg = new FolderBrowserDialog { Description = "选择 Edge 的 User Data 目录，或含 Preferences 的个人资料目录。", ShowNewFolderButton = false }) {
                if (dlg.ShowDialog(this) == DialogResult.OK) AddPath(dlg.SelectedPath);
            }
        }
        void AddFile() {
            if (busy) return; using (OpenFileDialog dlg = new OpenFileDialog { Title = "选择 Edge 的 Preferences 文件", Filter = "Preferences 文件|Preferences|所有文件|*.*", CheckFileExists = true }) {
                if (dlg.ShowDialog(this) == DialogResult.OK) AddPath(dlg.FileName);
            }
        }
        void AddPath(string path) {
            try { if (Discovery.InRoot(path, "手动添加").Count == 0) throw new IOException("所选目录内未找到 Preferences。请在 Edge 的 edge://version 页面查看“个人资料路径”。"); manual.Add(path); RefreshProfiles(); }
            catch (Exception ex) { Error(ex.Message); }
        }
        List<Profile> Selected() { return profiles.CheckedItems.Cast<ListViewItem>().Select(delegate(ListViewItem i) { return (Profile)i.Tag; }).ToList(); }
        async void ApplySelected() {
            if (busy) return; int count;
            if (!int.TryParse(weeks.Text, out count) || count < 1 || count > 1000) { Error("请输入 1～1000 之间的整数周数。"); return; }
            List<Profile> targets = Selected(); if (targets.Count == 0) return;
            SetBusy(true); DateTime now = DateTime.UtcNow; List<string> good = new List<string>(), bad = new List<string>();
            try {
                await Task.Run(delegate {
                    Engine.RequireStopped();
                    List<Plan> plans = targets.Select(delegate(Profile p) { return Engine.Prepare(p.Path, count, now); }).ToList();
                    foreach (Plan plan in plans) try { Engine.Commit(plan, Engine.BackupRoot, Engine.RequireStopped); good.Add(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(plan.Before.Path))); }
                    catch (Exception ex) { bad.Add(plan.Before.Path + "\r\n" + ex.Message); }
                });
                if (bad.Count > 0) Error("成功 " + good.Count + " 个，失败 " + bad.Count + " 个：\r\n\r\n" + string.Join("\r\n\r\n", bad));
                else MessageBox.Show(this, "已将 " + good.Count + " 个配置延后 " + count + " 周。\r\n\r\n下次提醒时间：" + now.AddDays(count * 7).ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "\r\n\r\n配置已写入并读回验证。重新打开 Edge 后使用新时间。", "修改完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) { Error(ex.Message); } finally { SetBusy(false); RefreshProfiles(); }
        }
        async void RestoreSelected() {
            if (busy) return; List<Profile> targets = Selected(); if (targets.Count == 0) return;
            if (MessageBox.Show(this, "撤销所选配置上一次由本工具进行的提醒时间修改？\r\n\r\n只恢复提醒时间，保留其他浏览器设置。", "撤销上次修改", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetBusy(true); List<string> errors = new List<string>(); int restored = 0;
            try {
                await Task.Run(delegate {
                    Engine.RequireStopped(); foreach (Profile p in targets) try {
                        string record = Engine.LatestRecord(p.Path, Engine.BackupRoot); if (record == null) throw new IOException("没有本工具可撤销的修改。");
                        Engine.Restore(record, p.Path, Engine.RequireStopped); restored++;
                    } catch (Exception ex) { errors.Add(p.Name + "：" + ex.Message); }
                });
                MessageBox.Show(this, "已撤销 " + restored + " 个配置。" + (errors.Count == 0 ? "" : "\r\n\r\n" + string.Join("\r\n", errors)), "恢复结果", MessageBoxButtons.OK, errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            } catch (Exception ex) { Error(ex.Message); } finally { SetBusy(false); RefreshProfiles(); }
        }
        async void CloseEdge() {
            if (busy) return; List<Process> existing = Engine.Processes(); bool running = existing.Count > 0; foreach (Process p in existing) p.Dispose();
            if (!running) { UpdateProcess(); return; }
            if (MessageBox.Show(this, "请先保存网页中未提交的内容，并等待下载完成。\r\n\r\n将请求关闭当前 Windows 会话中的 Edge 窗口；仍在运行时会再次询问是否结束进程。", "关闭 Edge", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            SetBusy(true);
            try {
                await Task.Run(delegate {
                    List<Process> current = Engine.Processes(); foreach (Process p in current) using (p) { try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { } }
                    Thread.Sleep(2500);
                });
                List<Process> remaining = Engine.Processes();
                if (remaining.Count > 0 && MessageBox.Show(this, "Edge 仍有 " + remaining.Count + " 个进程。\r\n\r\n是否结束这些进程？这可能中断下载，并丢失尚未保存的网页内容。\r\n只处理 msedge.exe，不处理 WebView2。", "结束剩余 Edge 进程？", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    await Task.Run(delegate { foreach (Process p in remaining) { try { if (!p.HasExited) { p.Kill(); p.WaitForExit(3000); } } catch (InvalidOperationException) { } } });
                }
                foreach (Process p in remaining) p.Dispose(); UpdateProcess();
            } catch (Exception ex) { Error(ex.Message); } finally { SetBusy(false); }
        }
        void Error(string text) { MessageBox.Show(this, text, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        void ShowHelp() {
            using (Form help = new Form { Text = "说明与备份", StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(650, 480), Font = Font, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog }) {
                TextBox text = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, Text =
                    "使用方法\r\n\r\n1. 勾选需要修改的 Edge 配置。\r\n2. 输入 1～1000 周，并完全退出 Edge。\r\n3. 点击“应用提醒时间”，随后重新打开 Edge。\r\n\r\n路径兼容\r\n\r\n自动查找当前用户的稳定版、Beta、Dev、Canary 配置，策略指定的数据目录，以及运行进程中的 --user-data-dir。Edge 程序装在其他盘不影响配置修改。未找到时，在 Edge 地址栏打开 edge://version，按“个人资料路径”手动添加目录或 Preferences 文件。手动目录需在下次运行时重新选择。\r\n\r\n备份与恢复\r\n\r\n每次修改前备份 Preferences，撤销时只恢复提醒时间。如果该时间后来被其他程序改动，会停止恢复，避免覆盖新值。备份存放于：\r\n" + Engine.BackupRoot + "\r\n备份含浏览器配置，请勿随意分享。\r\n\r\n兼容范围\r\n\r\nWindows 10 / 11，使用系统 .NET Framework 4.x。无需 Python、安装程序或附带 DLL。当前方法已对 Edge 153.0.4234.32 的时间判断逻辑进行核验；不同及未来版本仍可能改变或重置此内部设置。Dev / Canary 可能本来就不显示此提醒。\r\n\r\n本工具直接设置提醒截止时间，菜单选项保持原样。保留开发者模式与解压扩展，不修改 Edge 程序、不添加策略、不联网。\r\n\r\n版本 1.0.0 · 独立工具，与 Microsoft 无隶属关系。" };
                Panel host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), BackColor = Color.White }; host.Controls.Add(text); help.Controls.Add(host);
                Button open = Button("打开备份文件夹", false); open.Dock = DockStyle.Bottom; open.Height = 42; open.Click += delegate { Directory.CreateDirectory(Engine.BackupRoot); Process.Start(new ProcessStartInfo { FileName = Engine.BackupRoot, UseShellExecute = true }); }; help.Controls.Add(open);
                float scale; using (Graphics g = help.CreateGraphics()) scale = g.DpiX / 96f; if (Math.Abs(scale - 1f) > .01f) help.Scale(new SizeF(scale, scale)); help.ShowDialog(this);
            }
        }
    }
}
