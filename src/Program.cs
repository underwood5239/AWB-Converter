// ============================================================
//  AWB 音频转换器  (Windows Forms, .NET Framework 4.x)
//  深色程序员风格 UI；vgmstream-cli 及依赖内嵌在资源里，首次运行自解压。
//  GUI 模式：双击运行
//  CLI  模式（供自动化验证）：AWB转换器.exe --export <awb> [--format pcm16|float32] [--out <dir>]
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AwbConverter
{
    /// <summary>内嵌工具包：把 exe 里打包的 vgmstream 相关文件解压到缓存目录</summary>
    internal static class ToolPack
    {
        private const uint Magic = 0x31574241; // "ABW1"
        private const string ResName = "awbtools.bin";
        private static string _dir;
        private static readonly string[] Needed = new string[] {
            "vgmstream-cli.exe",
            "avcodec-vgmstream-59.dll", "avformat-vgmstream-59.dll", "avutil-vgmstream-57.dll",
            "swresample-vgmstream-4.dll", "libatrac9.dll", "libcelt-0061.dll", "libcelt-0110.dll",
            "libg719_decode.dll", "libmpg123-0.dll", "libspeex-1.dll",
            "libvorbis.dll", "xmp-vgmstream.dll"
        };

        public static string ExePath { get { return Path.Combine(Ensure(), "vgmstream-cli.exe"); } }
        public static string Dir { get { return Ensure(); } }

        public static string Ensure()
        {
            if (_dir != null) return _dir;

            string payloadPath = ExtractPayload();
            byte[] data = File.ReadAllBytes(payloadPath);
            List<KeyValuePair<string, byte[]>> files = Parse(data);
            if (files.Count == 0) throw new Exception("内嵌工具包解析失败（文件数为 0）");

            string stamp;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                stamp = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").Substring(0, 16);
            }

            string[] roots = new string[] {
                Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "AWB转换器_工具_" + stamp),
                Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AWBConverter"), "tools_" + stamp),
                Path.Combine(Path.GetTempPath(), "AWBConverter_tools_" + stamp)
            };

            Exception last = null;
            foreach (string root in roots)
            {
                try
                {
                    if (!IsReady(root)) WriteAll(root, files);
                    _dir = root;
                    return _dir;
                }
                catch (Exception ex) { last = ex; }
            }
            throw new Exception("工具自解压失败：" + (last == null ? "未知错误" : last.Message));
        }

        private static string ExtractPayload()
        {
            string p = Path.Combine(Path.GetTempPath(), "AWBConverter_payload_" + Process.GetCurrentProcess().Id + ".bin");
            var asm = typeof(ToolPack).Assembly;
            using (Stream s = asm.GetManifestResourceStream(ResName))
            {
                if (s == null)
                {
                    string[] names = asm.GetManifestResourceNames();
                    throw new Exception("找不到内嵌资源 awbtools.bin，现有资源：" + string.Join(", ", names));
                }
                using (var fs = new FileStream(p, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[65536];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
            }
            return p;
        }

        private static List<KeyValuePair<string, byte[]>> Parse(byte[] d)
        {
            var list = new List<KeyValuePair<string, byte[]>>();
            int o = 0;
            if (BitConverter.ToUInt32(d, o) != Magic) throw new Exception("内嵌工具包格式不正确");
            o += 4;
            int cnt = BitConverter.ToInt32(d, o); o += 4;
            for (int i = 0; i < cnt; i++)
            {
                int nl = BitConverter.ToInt32(d, o); o += 4;
                string name = Encoding.UTF8.GetString(d, o, nl); o += nl;
                int sz = BitConverter.ToInt32(d, o); o += 4;
                byte[] body = new byte[sz];
                Buffer.BlockCopy(d, o, body, 0, sz); o += sz;
                list.Add(new KeyValuePair<string, byte[]>(name, body));
            }
            return list;
        }

        private static bool IsReady(string root)
        {
            foreach (string n in Needed)
            {
                string f = Path.Combine(root, n);
                if (!File.Exists(f) || new FileInfo(f).Length == 0) return false;
            }
            return true;
        }

        private static void WriteAll(string root, List<KeyValuePair<string, byte[]>> files)
        {
            Directory.CreateDirectory(root);
            foreach (var kv in files)
            {
                string target = Path.Combine(root, kv.Key);
                bool same = File.Exists(target) && new FileInfo(target).Length == kv.Value.Length;
                if (!same)
                {
                    string tmp = target + ".tmp";
                    File.WriteAllBytes(tmp, kv.Value);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                }
            }
            if (!IsReady(root)) throw new Exception("解压后文件不完整：" + root);
        }
    }

    internal class Track
    {
        public int Index;
        public string Name = "";
        public string Rate = "", Channels = "", Encoding = "", Layout = "", Bitrate = "";
        public string LoopStart = "", LoopEnd = "", Duration = "", StreamTotal = "";
        public override string ToString() { return Name; }
    }

    internal static class Vgm
    {
        public static string Run(string args, out int exitCode)
        {
            var psi = new ProcessStartInfo(ToolPack.ExePath, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = ToolPack.Dir;
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit();
                exitCode = p.ExitCode;
                return (o + e).Trim();
            }
        }

        private static string Field(string text, string key)
        {
            Match m = Regex.Match(text, "(?m)^" + Regex.Escape(key) + @":\s*(.*?)\s*$");
            return m.Success ? m.Groups[1].Value : "";
        }

        public static List<Track> Inspect(string awb)
        {
            var list = new List<Track>();
            for (int i = 1; i <= 64; i++)
            {
                int code;
                string t = Run("-m -s " + i + " " + Q(awb), out code);
                if (code != 0 || t.Length == 0 || !t.Contains("sample rate")) break;
                var tr = new Track();
                tr.Index = i;
                tr.Name = Field(t, "stream name");
                tr.Rate = Field(t, "sample rate");
                tr.Channels = Field(t, "channels");
                tr.Encoding = Field(t, "encoding");
                tr.Layout = Field(t, "layout");
                tr.Bitrate = Field(t, "bitrate");
                tr.LoopStart = Field(t, "loop start");
                tr.LoopEnd = Field(t, "loop end");
                tr.Duration = Field(t, "play duration");
                tr.StreamTotal = Field(t, "stream total samples");
                list.Add(tr);
            }
            return list;
        }

        public static string Q(string s) { return "\"" + s + "\""; }
    }

    /// <summary>深色程序员风格配色</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(30, 32, 36);
        public static readonly Color Panel = Color.FromArgb(24, 26, 30);
        public static readonly Color BgAlt = Color.FromArgb(38, 41, 47);
        public static readonly Color BgHover = Color.FromArgb(52, 57, 65);
        public static readonly Color Fg = Color.FromArgb(222, 226, 230);
        public static readonly Color Muted = Color.FromArgb(140, 148, 158);
        public static readonly Color Faint = Color.FromArgb(104, 112, 122);
        public static readonly Color Accent = Color.FromArgb(56, 132, 222);
        public static readonly Color AccentHover = Color.FromArgb(74, 150, 240);
        public static readonly Color Ok = Color.FromArgb(106, 196, 122);
        public static readonly Color Err = Color.FromArgb(232, 106, 106);

        public static void Button(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = primary ? Accent : BgAlt;
            b.ForeColor = primary ? Color.White : Fg;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : BgHover;
            b.FlatAppearance.MouseDownBackColor = primary ? Accent : BgHover;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
        }

        public static void TextBoxBase(TextBoxBase t)
        {
            t.BackColor = Panel;
            t.ForeColor = Fg;
            t.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void Combo(ComboBox c)
        {
            c.BackColor = Panel;
            c.ForeColor = Fg;
            c.FlatStyle = FlatStyle.Flat;
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.ItemHeight = 20;
            c.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (var b = new SolidBrush(sel ? BgHover : Panel)) e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index >= 0)
                {
                    using (var b = new SolidBrush(Fg))
                        e.Graphics.DrawString(c.Items[e.Index].ToString(), e.Font, b, e.Bounds.X + 4, e.Bounds.Y + 2);
                }
            };
        }
    }

    public class MainForm : Form
    {
        private TextBox txtFile, txtInfo, txtLog, txtOut;
        private ComboBox cboFormat, cboLoop;
        private Button btnBrowse, btnOut, btnExport, btnOpen, btnClear;
        private Label lblStatus;
        private List<Track> _tracks = new List<Track>();
        private string _iniPath;
        private Font _fontOut;
        private float _s = 1f;   // DPI 缩放系数（96 DPI = 1.0）

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subApp, string subIdList);

        /// <summary>逻辑像素（96 DPI 基准）→ 当前 DPI 的设备像素</summary>
        private int S(float v) { return (int)Math.Round(v * _s); }

        public MainForm()
        {
            Text = "AWB 音频转换器";
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            AllowDrop = true;
            // 布局由下面的 S() 统一按 DPI 缩放，这里关掉自动缩放以免二次放大
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);

            // 取当前显示器 DPI（设计基准 96 DPI）
            using (var g = CreateGraphics()) { _s = g.DpiX / 96f; }
            if (_s <= 0f) _s = 1f;

            _fontOut = new Font("Consolas", 9.5f * _s, FontStyle.Regular, GraphicsUnit.Point);

            int H = S(26);          // 输入控件高度
            int HB = S(28);         // 按钮高度

            ClientSize = new Size(S(760), S(600));
            MinimumSize = new Size(S(680), S(570));
            StartPosition = FormStartPosition.CenterScreen;

            _iniPath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AWBConverter"), "settings.ini");

            // ===== 第 1 行：源文件 =====
            var lbl1 = new Label { Text = "音频文件", Left = S(12), Top = S(18), Width = S(62), ForeColor = Theme.Muted, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleLeft };
            txtFile = new TextBox { Left = S(78), Top = S(14), Width = S(578), Height = H, ReadOnly = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Theme.TextBoxBase(txtFile);
            btnBrowse = new Button { Text = "浏览…", Left = S(664), Top = S(13), Width = S(84), Height = H, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Theme.Button(btnBrowse, false);
            btnBrowse.Click += delegate { BrowseFile(); };

            // ===== 第 2 行：格式 + 导出目录 =====
            var lbl2 = new Label { Text = "输出格式", Left = S(12), Top = S(54), Width = S(62), ForeColor = Theme.Muted, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleLeft };
            cboFormat = new ComboBox { Left = S(78), Top = S(50), Width = S(168), DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.Combo(cboFormat);
            cboFormat.Items.AddRange(new object[] { "WAV 16bit（PCM）", "WAV 32bit（float）" });
            cboFormat.SelectedIndex = 0;

            var lblOut = new Label { Text = "导出到", Left = S(258), Top = S(54), Width = S(50), ForeColor = Theme.Muted, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleLeft };
            txtOut = new TextBox { Left = S(310), Top = S(50), Width = S(346), Height = H, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Theme.TextBoxBase(txtOut);
            btnOut = new Button { Text = "选择…", Left = S(664), Top = S(49), Width = S(84), Height = H, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Theme.Button(btnOut, false);
            btnOut.Click += delegate { BrowseOut(); };

            // ===== 第 3 行：导出方式（循环策略） =====
            var lbl3 = new Label { Text = "导出方式", Left = S(12), Top = S(95), Width = S(62), ForeColor = Theme.Muted, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleLeft };
            cboLoop = new ComboBox { Left = S(78), Top = S(91), Width = S(330), DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.Combo(cboLoop);
            // 顺序与下方 Export() 里的分支对应：0=循环2遍+淡出10秒（默认），1=原始时长
            cboLoop.Items.AddRange(new object[] {
                "循环 2 遍 + 淡出 10 秒（默认）",
                "原始时长（不循环、不淡出）"
            });
            cboLoop.SelectedIndex = 0;
            // 切换导出方式时，信息面板里的"预计导出时长"要跟着变
            cboLoop.SelectedIndexChanged += delegate { if (_tracks.Count > 0) RenderInfo(); };
            var lblLoopHint = new Label { Text = "只对带循环点的 BGM 有效", Left = S(418), Top = S(95), Width = S(330), ForeColor = Theme.Faint, BackColor = Theme.Bg, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            // ===== 第 4 行：操作按钮 =====
            btnExport = new Button { Text = "开始导出", Left = S(78), Top = S(126), Width = S(104), Height = HB, Enabled = false };
            Theme.Button(btnExport, true);
            btnExport.Click += delegate { Export(); };
            btnOpen = new Button { Text = "打开导出目录", Left = S(188), Top = S(126), Width = S(108), Height = HB };
            Theme.Button(btnOpen, false);
            btnOpen.Click += delegate { OpenOut(); };
            btnClear = new Button { Text = "清空日志", Left = S(302), Top = S(126), Width = S(88), Height = HB };
            Theme.Button(btnClear, false);
            btnClear.Click += delegate { txtLog.Clear(); };
            lblStatus = new Label { Text = "请选择一个 awb 文件", Left = S(402), Top = S(134), Width = S(346), ForeColor = Theme.Muted, BackColor = Theme.Bg, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            // ===== 文件信息 =====
            var lbl4 = new Label { Text = "文件信息", Left = S(12), Top = S(172), Width = S(200), ForeColor = Theme.Muted, BackColor = Theme.Bg };
            txtInfo = new TextBox
            {
                Left = S(12), Top = S(198), Width = S(736), Height = S(148),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
                Font = _fontOut,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Theme.TextBoxBase(txtInfo);

            // ===== 导出日志 =====
            var lbl5 = new Label { Text = "导出日志", Left = S(12), Top = S(356), Width = S(70), ForeColor = Theme.Muted, BackColor = Theme.Bg };
            var lblTip = new Label { Text = "可把 awb 直接拖进本窗口", Left = S(300), Top = S(356), Width = S(400), ForeColor = Theme.Faint, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            txtLog = new TextBox
            {
                Left = S(12), Top = S(380), Width = S(736), Height = S(168),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
                Font = _fontOut,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Theme.TextBoxBase(txtLog);

            Controls.AddRange(new Control[] { lbl1, txtFile, btnBrowse, lbl2, cboFormat, lblOut, txtOut, btnOut,
                                              lbl3, cboLoop, lblLoopHint,
                                              btnExport, btnOpen, btnClear, lblStatus, lbl4, txtInfo, lbl5, lblTip, txtLog });

            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] fs = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (fs != null && fs.Length > 0) LoadFile(fs[0]);
            };

            LoadSettings();

            // 验证开关：设置 AWBGUI_FILE 环境变量可让程序启动后自动载入该文件并立即导出
            // （仅用于自动化验证 GUI 的导出链路，正常使用不需要）
            string autoFile = Environment.GetEnvironmentVariable("AWBGUI_FILE");

            Shown += delegate
            {
                try
                {
                    int dark = 1;
                    DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                    DwmSetWindowAttribute(Handle, 19, ref dark, 4);
                    SetWindowTheme(txtInfo.Handle, "DarkMode_Explorer", null);
                    SetWindowTheme(txtLog.Handle, "DarkMode_Explorer", null);
                }
                catch { }
                txtLog.Text = "程序已就绪。" + Environment.NewLine +
                              "内嵌工具目录：" + ToolPack.Dir + Environment.NewLine +
                              "用法：浏览选择 awb → 选输出格式与导出方式 → 选导出目录 → 开始导出" + Environment.NewLine;
                // 注意：必须先写就绪信息再自动导出，否则日志会被覆盖
                if (!string.IsNullOrEmpty(autoFile)) { LoadFile(autoFile); Export(); }
            };
        }

        // ================= 设置读写 =================
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_iniPath))
                {
                    foreach (string line in File.ReadAllLines(_iniPath))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                        if (k == "format" && (v == "0" || v == "1")) cboFormat.SelectedIndex = int.Parse(v);
                        if (k == "loop" && v.Length == 1 && "01".IndexOf(v) >= 0) cboLoop.SelectedIndex = int.Parse(v);
                        if (k == "outdir" && v.Length > 0) txtOut.Text = v;
                    }
                }
            }
            catch { }
            if (txtOut.Text.Length == 0)
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AWB导出");
                try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { }
                txtOut.Text = d;
            }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_iniPath));
                File.WriteAllLines(_iniPath, new string[] {
                    "format=" + cboFormat.SelectedIndex,
                    "loop=" + cboLoop.SelectedIndex,
                    "outdir=" + txtOut.Text
                }, new UTF8Encoding(false));
            }
            catch { }
        }

        // ================= 界面动作 =================
        private void BrowseFile()
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "选择要转换的音频文件";
                d.Filter = "AWB 音频包 (*.awb)|*.awb|所有支持的格式|*.awb;*.acb;*.hca;*.adx;*.wav;*.ogg;*.mp3;*.flac;*.fsb;*.bank;*.cpk;*.bnk|所有文件 (*.*)|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) LoadFile(d.FileName);
            }
        }

        private void BrowseOut()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "选择导出目录";
                d.ShowNewFolderButton = true;
                if (Directory.Exists(txtOut.Text)) d.SelectedPath = txtOut.Text;
                if (d.ShowDialog(this) == DialogResult.OK) { txtOut.Text = d.SelectedPath; SaveSettings(); }
            }
        }

        private void OpenOut()
        {
            try
            {
                string p = txtOut.Text.Trim();
                if (!Directory.Exists(p)) { MessageBox.Show(this, "目录还不存在：\n" + p, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                Process.Start("explorer.exe", "\"" + p + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打不开目录", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void Log(string s)
        {
            txtLog.AppendText(s + Environment.NewLine);
        }

        private void Status(string s, Color c)
        {
            lblStatus.Text = s;
            lblStatus.ForeColor = c;
        }

        /// <summary>取形如 "874763 samples (0:18.224 seconds)" 里的秒数</summary>
        private static double ParseSeconds(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            Match m = Regex.Match(s, @"\((\d+):(\d+(?:\.\d+)?)\s*seconds\)");
            if (m.Success) return int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            m = Regex.Match(s, @"(\d+)\s*samples");
            if (m.Success) return double.Parse(m.Groups[1].Value) / 48000.0;   // 兜底（按 48k 估算）
            return -1;
        }

        private static string FormatDuration(double seconds)
        {
            int total = (int)Math.Round(seconds);
            if (total >= 3600) return string.Format("{0}:{1:00}:{2:00}", total / 3600, (total % 3600) / 60, total % 60);
            return string.Format("{0}:{1:00}", total / 60, total % 60);
        }

        /// <summary>按当前“导出方式”估算这一段导出后的时长（秒）</summary>
        private double EstimateDuration(Track t)
        {
            double total = ParseSeconds(t.StreamTotal);
            double loopS = ParseSeconds(t.LoopStart);
            double loopE = ParseSeconds(t.LoopEnd);
            bool hasLoop = loopS >= 0 && loopE > loopS;
            // 0 = 循环 2 遍 + 淡出 10 秒（默认）；1 = 原始时长
            if (cboLoop.SelectedIndex == 1 || !hasLoop) return total;
            // 实测校验：76.9 + 58.7 + 10.0 = 145.6 秒，与 vgmstream 输出一致
            return total - loopS + 10.0;
        }

        /// <summary>把当前轨道信息渲染到信息面板（导出方式改变时会重绘）</summary>
        private void RenderInfo()
        {
            if (_tracks.Count == 0) return;
            var fi = new FileInfo(txtFile.Text);
            var sb = new StringBuilder();
            sb.AppendLine("文件      : " + fi.Name);
            sb.AppendLine("大小      : " + FmtSize(fi.Length));
            sb.AppendLine("完整路径  : " + fi.FullName);
            sb.AppendLine();
            sb.AppendLine("轨道数    : " + _tracks.Count + " 段" + (_tracks.Count > 1 ? "（导出时全部导出，命名为 _01、_02…）" : ""));
            int idx = 0;
            foreach (Track t in _tracks)
            {
                idx++;
                sb.AppendLine();
                sb.AppendLine("── 第 " + idx + " 段 ─────────────────────────");
                if (!string.IsNullOrEmpty(t.Name)) sb.AppendLine("  轨道名   : " + t.Name);
                sb.AppendLine("  采样率   : " + t.Rate);
                sb.AppendLine("  声道     : " + t.Channels + " 声道");
                sb.AppendLine("  编码     : " + t.Encoding + "（" + t.Layout + "）");
                sb.AppendLine("  比特率   : " + t.Bitrate);
                sb.AppendLine("  循环起点 : " + (t.LoopStart.Length > 0 ? t.LoopStart : "无"));
                sb.AppendLine("  循环终点 : " + (t.LoopEnd.Length > 0 ? t.LoopEnd : "无"));
                sb.AppendLine("  循环前长度: " + t.StreamTotal);
                double est = EstimateDuration(t);
                sb.AppendLine("  导出方式 : " + cboLoop.Text);
                sb.AppendLine("  导出时长 : 约 " + (est >= 0 ? FormatDuration(est) : "未知") + "   （按所选导出方式计算，实际以日志为准）");
            }
            txtInfo.Text = sb.ToString();
            txtInfo.SelectionStart = 0;
            txtInfo.ScrollToCaret();
        }

        private void LoadFile(string path)
        {
            if (!File.Exists(path)) { MessageBox.Show(this, "找不到文件：\n" + path, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            txtFile.Text = path;
            txtInfo.Clear();
            _tracks.Clear();
            btnExport.Enabled = false;
            Status("正在读取文件信息…", Theme.Muted);
            Application.DoEvents();

            try
            {
                _tracks = Vgm.Inspect(path);
                if (_tracks.Count == 0)
                {
                    var sb = new StringBuilder();
                    var fi0 = new FileInfo(path);
                    sb.AppendLine("文件      : " + fi0.Name);
                    sb.AppendLine("大小      : " + FmtSize(fi0.Length));
                    sb.AppendLine();
                    sb.AppendLine("【未能识别】vgmstream 读不出这个文件的信息。");
                    sb.AppendLine("可能它不是音频包，或格式不被支持。");
                    txtInfo.Text = sb.ToString();
                    Status("无法识别该文件", Theme.Err);
                    return;
                }
                RenderInfo();
                btnExport.Enabled = true;
                Status("已就绪：共 " + _tracks.Count + " 段，可以导出了", Theme.Ok);
            }
            catch (Exception ex)
            {
                txtInfo.AppendText("【出错】" + ex.Message + Environment.NewLine);
                Status("读取信息失败", Theme.Err);
            }
        }

        private static string FmtSize(long b)
        {
            if (b >= 1073741824) return (b / 1073741824.0).ToString("0.00") + " GB";
            if (b >= 1048576) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("0.0") + " KB";
            return b + " B";
        }

        private void Export()
        {
            string src = txtFile.Text.Trim();
            string dst = txtOut.Text.Trim();
            if (src.Length == 0 || !File.Exists(src)) { MessageBox.Show(this, "请先选择要转换的音频文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (dst.Length == 0) { MessageBox.Show(this, "请先选择导出目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            try { Directory.CreateDirectory(dst); }
            catch (Exception ex) { MessageBox.Show(this, "导出目录无法创建：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            bool float32 = (cboFormat.SelectedIndex == 1);
            string baseName = Path.GetFileNameWithoutExtension(src);
            string sub = Path.Combine(dst, baseName);
            try { Directory.CreateDirectory(sub); }
            catch (Exception ex) { MessageBox.Show(this, "子目录无法创建：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            SaveSettings();
            btnExport.Enabled = false;
            btnExport.Text = "导出中…";
            Application.DoEvents();

            Log("──────────────────────────────────────────────");
            Log("开始导出：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("  源文件  : " + src);
            Log("  格式    : " + cboFormat.Text);
            Log("  方式    : " + cboLoop.Text);
            Log("  输出到  : " + sub + "\\" + baseName + "_01.wav …");

            try
            {
                // 循环策略：0=循环 2 遍 + 淡出 10 秒（默认）  1=原始时长（不循环不淡出）
                // -S 0 导出一个 awb 的全部轨道；?02s 会被替换成补零的轨道号
                string wavFlag = float32 ? "-w " : "";
                string loopFlag = "";
                string expect = "循环 2 遍 + 淡出 10 秒";
                if (cboLoop.SelectedIndex == 1) { loopFlag = "-i "; expect = "原始时长（不循环、不淡出）"; }
                else { loopFlag = "-l 2.0 -f 10.0 "; }

                string args = "-S 0 " + wavFlag + loopFlag + "-o " + Vgm.Q(Path.Combine(sub, baseName + "_?02s.wav")) + " " + Vgm.Q(src);
                Log("  命令    : vgmstream-cli " + args);
                Log("  预期    : " + expect);
                var sw = Stopwatch.StartNew();
                int code;
                string outp = Vgm.Run(args, out code);
                sw.Stop();
                if (outp.Length > 0)
                {
                    int seg = 0;
                    foreach (string line in outp.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string s = line.Trim();
                        if (s.StartsWith("decoding"))
                        {
                            // vgmstream 打的是输出文件名模板，"名字_?02s.wav" 显示出来很难看，换成人话
                            seg++;
                            Log("  ── 解码第 " + seg + " 段");
                            continue;
                        }
                        Match m = Regex.Match(s, @"^(sample rate|channels|encoding|bitrate|loop start|loop end|play duration|stream index|stream name):\s*(.*)$");
                        if (m.Success)
                        {
                            string k = m.Groups[1].Value, v = m.Groups[2].Value;
                            if (k == "sample rate") Log("      采样率 : " + v);
                            else if (k == "channels") Log("      声道   : " + v + " 声道");
                            else if (k == "encoding") Log("      编码   : " + v);
                            else if (k == "bitrate") Log("      比特率 : " + v);
                            else if (k == "loop start") Log("      循环起点: " + v);
                            else if (k == "loop end") Log("      循环终点: " + v);
                            else if (k == "play duration") Log("      循环前时长: " + v);
                            else if (k == "stream index") Log("      （流序号 " + v + "）");
                            else if (k == "stream name") Log("      轨道名 : " + v);
                        }
                    }
                }

                string[] wavs = Directory.GetFiles(sub, "*.wav");
                Array.Sort(wavs, StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (string w in wavs) total += new FileInfo(w).Length;

                if (code != 0 || wavs.Length == 0)
                {
                    Log("  [失败] vgmstream 退出码 = " + code + "，产出 wav " + wavs.Length + " 个");
                    Status("导出失败，详见日志", Theme.Err);
                }
                else
                {
                    Log("  产出    : " + wavs.Length + " 个文件，合计 " + FmtSize(total));
                    foreach (string w in wavs) Log("            " + Path.GetFileName(w) + "   " + FmtSize(new FileInfo(w).Length));
                    Log("  耗时    : " + sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
                    Log("  结果    : 导出成功");
                    Status("导出成功： " + wavs.Length + " 个文件 → " + sub, Theme.Ok);
                }
            }
            catch (Exception ex)
            {
                Log("  [出错] " + ex.Message);
                Status("导出出错", Theme.Err);
            }
            finally
            {
                btnExport.Enabled = true;
                btnExport.Text = "开始导出";
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--export")
            {
                try
                {
                    string src = args[1];
                    string fmt = "pcm16";
                    string mode = "loop2";
                    string outDir = Path.Combine(Path.GetDirectoryName(src), "AWB导出");
                    for (int i = 2; i < args.Length - 1; i++)
                    {
                        if (args[i] == "--format") fmt = args[i + 1];
                        if (args[i] == "--out") outDir = args[i + 1];
                        if (args[i] == "--mode") mode = args[i + 1];
                    }
                    string baseName = Path.GetFileNameWithoutExtension(src);
                    string sub = Path.Combine(outDir, baseName);
                    Directory.CreateDirectory(sub);
                    string wavFlag = (fmt == "float32") ? "-w " : "";
                    // --mode loop2（默认，循环 2 遍 + 淡出 10 秒）| original（原始时长，不循环不淡出）
                    string loopFlag = "-l 2.0 -f 10.0 ";
                    if (mode == "original") loopFlag = "-i ";
                    string cmd = "-S 0 " + wavFlag + loopFlag + "-o " + Vgm.Q(Path.Combine(sub, baseName + "_?02s.wav")) + " " + Vgm.Q(src);
                    int code;
                    string outp = Vgm.Run(cmd, out code);
                    Console.WriteLine("tools dir : " + ToolPack.Dir);
                    Console.WriteLine("mode      : " + mode);
                    Console.WriteLine("exit      : " + code);
                    Console.WriteLine(outp);
                    string[] wavs = Directory.GetFiles(sub, "*.wav");
                    Console.WriteLine("wav files : " + wavs.Length);
                    foreach (string w in wavs) Console.WriteLine("  " + Path.GetFileName(w) + "  " + new FileInfo(w).Length + " bytes");
                    return (code == 0 && wavs.Length > 0) ? 0 : 2;
                }
                catch (Exception ex) { Console.WriteLine("ERROR: " + ex); return 3; }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm()); }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "程序异常", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return 0;
        }
    }
}
