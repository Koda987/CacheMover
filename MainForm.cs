using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CacheMover;

public class MainForm : Form
{
    private static readonly Color TextDark = Color.FromArgb(0x2F, 0x3A, 0x54);
    private static readonly Color TextGray = Color.FromArgb(0x7C, 0x8A, 0xA8);
    private static readonly Color TextBody = Color.FromArgb(0x44, 0x50, 0x6B);
    private static readonly Color Blue = Color.FromArgb(0x5B, 0x8D, 0xEF);

    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CacheMover");

    // ---------- 侧边栏 ----------
    private NavButton _navHome = null!, _navHistory = null!, _navSettings = null!;
    private SpeechBubble _bubble = null!;

    // ---------- 首页 ----------
    private MascotAvatar _avatarHeader = null!;
    private Label _lblAppTitle = null!, _lblAppSub = null!, _lblSizeLine = null!, _lblStatus = null!, _lblBak = null!;
    private NoteLabel _note = null!;
    private RoundedPanel _pageHome = null!;
    private InputField _inpSrc = null!, _inpDst = null!;
    private ListBox _lstChecks = null!;
    private GhostButton _btnPresets = null!, _btnCancelSmall = null!, _btnDeleteBak = null!, _btnOpenDst = null!;
    private GradientButton _btnGo = null!;
    private CuteProgressBar _progress = null!;

    // ---------- 历史页 ----------
    private RoundedPanel _pageHistory = null!;
    private DataGridView _grid = null!;
    private TextBox _txtLog = null!;
    private GhostButton _btnRefreshHistory = null!, _btnOpenDataFolder = null!;

    // ---------- 设置页 ----------
    private RoundedPanel _pageSettings = null!;

    private ContextMenuStrip _presetMenu = null!;
    private System.Windows.Forms.Timer _timer = null!;

    // ---------- 背景图 ----------
    private readonly Image? _bg;
    private readonly MemoryStream? _bgStream;

    private List<CheckResult> _results = new();
    private long _srcSize = -1;
    private string _srcInfo = "", _dstInfo = "";
    private int _checkRun;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _workCts;
    private bool _busy;
    private string? _bakPath;

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private static bool _screenshotMode;
    private static string _shotDir = ".";

    public MainForm(string[]? screenshotArgs = null)
    {
        if (screenshotArgs is { Length: >= 1 } && screenshotArgs[0] == "--screenshot")
        {
            _screenshotMode = true;
            _shotDir = screenshotArgs.Length >= 2 ? screenshotArgs[1] : ".";
        }

        Text = "C 盘腾腾酱 · 缓存搬家 CacheMover";
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1160, 768);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0xF2, 0xF5, 0xFC);
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? Icon; } catch { /* 任务栏图标兜底 */ }

        (_bg, _bgStream) = LoadBackground();

        BuildSidebar();
        BuildTitleBar();
        BuildHeader();
        _pageHome = BuildHomePage();
        _pageHistory = BuildHistoryPage();
        _pageSettings = BuildSettingsPage();
        _presetMenu = BuildPresetMenu();

        Controls.AddRange(new Control[]
        {
            _sidebar, _btnMin, _btnClose,
            _avatarHeader, _lblAppTitle, _lblAppSub, _note,
            _pageHome, _pageHistory, _pageSettings
        });

        _inpSrc.Box.TextChanged += (_, _) => OnPathChanged();
        _inpDst.Box.TextChanged += (_, _) => OnPathChanged();
        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                MessageBox.Show("正在迁移中，请等待完成或点「取消」。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        _timer = new System.Windows.Forms.Timer { Interval = 600 };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _ = RunCheckAsync();
        };

        ApplyTransparentLabels(this);
        EnableDragRecursively(this);
        // 窗体自身背景（右侧立绘露出的区域）也要能拖动
        MouseDown += (_, e) => StartDrag(e);
        ShowPage(0);
        RenderResults();
        UpdateButtons();

        if (_screenshotMode)
            Shown += async (_, _) => await RunScreenshotSequenceAsync();
    }

    // ================= 截图模式（--screenshot） =================

    private async Task RunScreenshotSequenceAsync()
    {
        try
        {
            string Shot(string name)
            {
                // 隐藏不透明 TextBox，让 OnPaint 里的占位文字出现在截图中
                bool v1 = _inpSrc.Box.Visible, v2 = _inpDst.Box.Visible;
                _inpSrc.Box.Visible = _inpDst.Box.Visible = false;
                using var bmp = new Bitmap(Width, Height);
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                _inpSrc.Box.Visible = v1;
                _inpDst.Box.Visible = v2;
                var path = Path.Combine(_shotDir, name);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return path;
            }

            await Task.Delay(500);
            ActiveControl = null; // 让输入框失焦，占位文字完整显示
            Shot("shot-home.png");
            ShowPage(1);
            await Task.Delay(500);
            Shot("shot-history.png");
            ShowPage(2);
            await Task.Delay(500);
            Shot("shot-settings.png");
            ShowPage(0);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("截图生成失败：" + ex.Message, Text);
            Close();
        }
    }

    // ================= 背景与窗口 =================

    private static (Image?, MemoryStream?) LoadBackground()
    {
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("CacheMover.assets.bg.jpg");
            if (s == null) return (null, null);
            var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            return (Image.FromStream(ms), ms);
        }
        catch
        {
            return (null, null);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_bg == null)
        {
            using var brush = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(0xF3, 0xF6, 0xFE), Color.FromArgb(0xF7, 0xF4, 0xFE), 100f);
            g.FillRectangle(brush, ClientRectangle);
            return;
        }
        // 高度铺满 + 整体右移：少女脸部完整露出在主卡片右侧，不被遮挡
        g.Clear(Color.FromArgb(0xF2, 0xF5, 0xFC));
        float scale = (float)Height / _bg.Height;
        int w = (int)(_bg.Width * scale + 0.5f);
        int x = Width - w + 120;
        g.DrawImage(_bg, x, 0, w, Height);
        if (x > 0)
        {
            // 左侧镜面扩图：把原图最左边的内容水平镜像填到左边缘，接缝处与主图完全连续
            var state = g.Save();
            g.TranslateTransform(x, 0);
            g.ScaleTransform(-1, 1);
            g.DrawImage(_bg, new Rectangle(0, 0, x, Height),
                new Rectangle(0, 0, (int)(x / scale) + 1, _bg.Height), GraphicsUnit.Pixel);
            g.Restore(state);
        }
        // 一层很淡的白纱，保证文字可读
        using var veil = new SolidBrush(Color.FromArgb(30, Color.White));
        g.FillRectangle(veil, ClientRectangle);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Region = new Region(UiUtil.RoundedPath(new Rectangle(0, 0, Width, Height), 22));
    }

    private void StartDrag(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }
    }

    private void EnableDragRecursively(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is InputField or ListBox or DataGridView or TextBox
                or GradientButton or GhostButton or IconButton or NavButton)
                continue;
            c.MouseDown += (_, e) => StartDrag(e);
            EnableDragRecursively(c);
        }
    }

    private static void ApplyTransparentLabels(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Label or PictureBox) c.BackColor = Color.Transparent;
            ApplyTransparentLabels(c);
        }
    }

    // ================= 界面搭建 =================

    private IconButton _btnMin = null!, _btnClose = null!;

    private void BuildTitleBar()
    {
        _btnMin = new IconButton
        {
            Left = 1070, Top = 18, Width = 30, Height = 30,
            IconImage = DrawIcons.Make("min", Color.FromArgb(0x5E, 0x6B, 0x87), 16)
        };
        _btnClose = new IconButton
        {
            Left = 1106, Top = 18, Width = 30, Height = 30,
            IconImage = DrawIcons.Make("close", Color.FromArgb(0x5E, 0x6B, 0x87), 16)
        };
        _btnClose.HoverImage = DrawIcons.Make("close", Color.White, 16);
        _btnMin.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _btnClose.Click += (_, _) => Close();
    }

    private RoundedPanel _sidebar = null!;

    private void BuildSidebar()
    {
        _sidebar = new RoundedPanel
        {
            Left = 12, Top = 12, Width = 234, Height = 744, Radius = 18,
            FillColor = Color.FromArgb(208, 255, 255, 255),
            ShowBorder = false
        };

        var avatar = new MascotAvatar { Left = 18, Top = 16, Width = 48, Height = 48, CustomImage = IconAssets.CatAvatar };
        var name = new Label
        {
            Text = "C 盘腾腾酱",
            Left = 74, Top = 22, Width = 152, Height = 24,
            Font = new Font("Microsoft YaHei UI", 11.25F, FontStyle.Bold),
            ForeColor = TextDark
        };
        var sub = new Label
        {
            Text = "让 C 盘，轻松腾出空间!",
            Left = 75, Top = 48, Width = 152, Height = 16,
            Font = new Font("Microsoft YaHei UI", 8.25F),
            ForeColor = Color.FromArgb(0x8A, 0x97, 0xB5)
        };

        _navHome = MakeNav("首页", "home", 0);
        _navHistory = MakeNav("历史记录", "clock", 1);
        _navSettings = MakeNav("设置", "gear", 2, IconAssets.Gear);
        _navHome.Click += (_, _) => ShowPage(0);
        _navHistory.Click += (_, _) => ShowPage(1);
        _navSettings.Click += (_, _) => ShowPage(2);

        if (IconAssets.CloudSticker != null)
        {
            // 云朵气泡+小猫贴纸（素材图自带文案）
            var sticker = new PictureBox
            {
                Left = 12, Top = 560, Width = 210, Height = 182,
                Image = IconAssets.CloudSticker,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            _sidebar.Controls.AddRange(new Control[] { avatar, name, sub, _navHome, _navHistory, _navSettings, sticker });
        }
        else
        {
            _bubble = new SpeechBubble
            {
                Left = 14, Top = 570, Width = 204, Height = 92,
                Message = "主人，\r\nC 盘空间还不够吗？\r\n我来帮你~"
            };
            var cat = new MascotAvatar { Left = 74, Top = 668, Width = 70, Height = 70, CustomImage = IconAssets.CatAvatar };
            _sidebar.Controls.AddRange(new Control[] { avatar, name, sub, _navHome, _navHistory, _navSettings, _bubble, cat });
        }
    }

    private NavButton MakeNav(string text, string icon, int index, Image? customIcon = null)
    {
        return new NavButton
        {
            Text = text,
            Left = 16, Top = 84 + index * 52, Width = 202, Height = 44,
            Icon = customIcon ?? DrawIcons.Make(icon, Color.FromArgb(0x5E, 0x6B, 0x87), 22, 16f),
            IconActive = customIcon ?? DrawIcons.Make(icon, Color.FromArgb(0x3D, 0x6F, 0xD4), 22, 16f)
        };
    }

    private void BuildHeader()
    {
        _avatarHeader = new MascotAvatar { Left = 270, Top = 24, Width = 80, Height = 80, ShowLinkBadge = true, CustomImage = IconAssets.CatAvatar };
        _lblAppTitle = new Label
        {
            Text = "C 盘腾腾酱",
            Left = 364, Top = 30, Width = 340, Height = 42,
            Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold),
            ForeColor = TextDark
        };
        _lblAppSub = new Label
        {
            Text = "通过软链接迁移缓存文件，释放 C 盘空间。",
            Left = 366, Top = 76, Width = 460, Height = 20,
            ForeColor = TextGray
        };
        _note = new NoteLabel { Text = "C 盘也要\n呼吸哦~", Left = 744, Top = 22, Width = 230, Height = 96 };
    }

    private RoundedPanel BuildHomePage()
    {
        var card = new RoundedPanel { Left = 264, Top = 132, Width = 576, Height = 624, Radius = 18 };

        var tileSrc = new PictureBox
        {
            Image = (Image?)IconAssets.FolderSrc ?? DrawIcons.FolderTile(34, Color.FromArgb(0xE8, 0xF0, 0xFE), Blue),
            Left = 18, Top = 22, Width = 68, Height = 68,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        var lblSrc = new Label
        {
            Text = "要转移的文件夹",
            Left = 100, Top = 24, Width = 200, Height = 20,
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
            ForeColor = TextDark
        };
        _btnPresets = new GhostButton
        {
            Text = "从常用位置选择 ▾",
            Left = 382, Top = 24, Width = 170, Height = 28,
            ShowBorder = false
        };
        _inpSrc = new InputField { Left = 88, Top = 56, Width = 466, Placeholder = "请选择要转移的文件夹路径" };
        _inpSrc.BrowseButton.Click += (_, _) => Browse(_inpSrc, "选择要转移的文件夹（C 盘缓存）");

        var tileDst = new PictureBox
        {
            Image = (Image?)IconAssets.FolderDst ?? DrawIcons.FolderTile(34, Color.FromArgb(0xE7, 0xF8, 0xF0), Color.FromArgb(0x34, 0xB3, 0x7E)),
            Left = 18, Top = 148, Width = 68, Height = 68,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        var lblDst = new Label
        {
            Text = "目标文件夹",
            Left = 100, Top = 150, Width = 200, Height = 20,
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
            ForeColor = TextDark
        };
        _inpDst = new InputField { Left = 88, Top = 182, Width = 466, Placeholder = "请选择目标文件夹路径" };
        _inpDst.BrowseButton.Click += (_, _) => Browse(_inpDst, "选择转移目标位置（其他盘）");

        _lblSizeLine = new Label { Left = 22, Top = 236, Width = 532, Height = 16, ForeColor = TextGray, AutoEllipsis = true };

        _lstChecks = new ListBox
        {
            Left = 22, Top = 254, Width = 532, Height = 58,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 19,
            IntegralHeight = false,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 8.75F)
        };
        _lstChecks.DrawItem += LstChecks_DrawItem;

        var infoCard = BuildInfoCard();

        _progress = new CuteProgressBar { Left = 22, Top = 462, Width = 532, Visible = false };
        _lblStatus = new Label
        {
            Left = 22, Top = 476, Width = 532, Height = 16,
            ForeColor = TextGray, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _btnGo = new GradientButton { Left = 163, Top = 500, Width = 250, Height = 46, Text = "创建软链接" };
        _btnCancelSmall = new GhostButton
        {
            Text = "取消", Left = 429, Top = 509, Width = 80, Height = 36, Visible = false
        };
        _btnDeleteBak = new GhostButton
        {
            Text = "删除备份，释放 C 盘空间", Left = 78, Top = 560, Width = 200, Height = 32, Visible = false
        };
        _btnOpenDst = new GhostButton
        {
            Text = "打开目标文件夹", Left = 298, Top = 560, Width = 200, Height = 32, Visible = false
        };
        _lblBak = new Label
        {
            Left = 22, Top = 598, Width = 532, Height = 16,
            ForeColor = Color.FromArgb(0x2E, 0x8B, 0x62), AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleCenter
        };

        card.Controls.AddRange(new Control[]
        {
            tileSrc, lblSrc, _btnPresets, _inpSrc,
            tileDst, lblDst, _inpDst,
            _lblSizeLine, _lstChecks, infoCard,
            _progress, _lblStatus, _btnGo, _btnCancelSmall,
            _btnDeleteBak, _btnOpenDst, _lblBak
        });

        _btnGo.Click += async (_, _) => await StartMoveAsync();
        _btnCancelSmall.Click += (_, _) => { _lblStatus.Text = "正在取消…"; _workCts?.Cancel(); };
        _btnDeleteBak.Click += async (_, _) => await DeleteBakAsync();
        _btnOpenDst.Click += (_, _) => OpenDst();
        _btnPresets.Click += (_, _) => _presetMenu.Show(_btnPresets, new Point(0, _btnPresets.Height));

        return card;
    }

    private RoundedPanel BuildInfoCard()
    {
        var card = new RoundedPanel
        {
            Left = 22, Top = 312, Width = 532, Height = 140, Radius = 12,
            FillColor = Color.FromArgb(0xEA, 0xF2, 0xFF),
            BorderColor = Color.FromArgb(0xD5, 0xE4, 0xFB)
        };
        var icon = new PictureBox
        {
            Image = DrawIcons.InfoBadge(22, Blue),
            Left = 14, Top = 12, Width = 22, Height = 22,
            SizeMode = PictureBoxSizeMode.CenterImage
        };
        var title = new Label
        {
            Text = "不建议转移的文件夹",
            Left = 44, Top = 15, Width = 250, Height = 18,
            Font = new Font("Microsoft YaHei UI", 9.75F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x3D, 0x6F, 0xD4)
        };
        var desc = new Label
        {
            Text = "以下类型的文件夹通常不适合创建软链接，可能会导致系统异常或无法正常运行：",
            Left = 14, Top = 38, Width = 504, Height = 30,
            ForeColor = Color.FromArgb(0x6E, 0x82, 0xA8),
            Font = new Font("Microsoft YaHei UI", 8.5F)
        };
        Label Bullet(string text, int x, int y) => new()
        {
            Text = "•  " + text,
            Left = x, Top = y, Width = 252, Height = 18,
            ForeColor = Color.FromArgb(0x55, 0x68, 0x8C),
            Font = new Font("Microsoft YaHei UI", 8.25F),
            AutoEllipsis = true
        };
        card.Controls.AddRange(new Control[]
        {
            icon, title, desc,
            Bullet("Windows 系统目录（如 C:\\Windows）", 14, 72),
            Bullet("Program Files 目录", 14, 94),
            Bullet("Program Files (x86) 目录", 14, 116),
            Bullet("用户个人目录（如 C:\\Users）", 278, 72),
            Bullet("系统设置相关目录（如 C:\\Windows\\System32）", 278, 94),
            Bullet("虚拟内存相关目录（如 pagefile.sys 所在目录）", 278, 116)
        });
        return card;
    }

    private RoundedPanel BuildHistoryPage()
    {
        var card = new RoundedPanel { Left = 264, Top = 26, Width = 576, Height = 730, Radius = 18, Visible = false };

        var title = new Label
        {
            Text = "历史记录",
            Left = 26, Top = 24, Width = 200, Height = 28,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = TextDark
        };
        _btnOpenDataFolder = new GhostButton { Text = "打开记录文件夹", Left = 290, Top = 20, Width = 150, Height = 32 };
        _btnRefreshHistory = new GhostButton { Text = "刷新", Left = 452, Top = 20, Width = 100, Height = 32 };

        _grid = new DataGridView
        {
            Left = 26, Top = 66, Width = 524, Height = 420,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 34,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(0xED, 0xF0, 0xF8),
            RowTemplate = { Height = 30 }
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(0xEE, 0xF3, 0xFC);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.ForeColor = TextBody;
        _grid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xE3, 0xEC, 0xFB);
        _grid.DefaultCellStyle.SelectionForeColor = TextBody;
        _grid.Columns.Add("time", "时间");
        _grid.Columns.Add("src", "原文件夹");
        _grid.Columns.Add("dst", "目标位置");
        _grid.Columns.Add("size", "大小");
        _grid.Columns[0].FillWeight = 22;
        _grid.Columns[1].FillWeight = 34;
        _grid.Columns[2].FillWeight = 30;
        _grid.Columns[3].FillWeight = 14;

        var lblLog = new Label
        {
            Text = "运行日志",
            Left = 26, Top = 498, Width = 200, Height = 20,
            Font = new Font("Microsoft YaHei UI", 9.75F, FontStyle.Bold),
            ForeColor = TextBody
        };
        _txtLog = new TextBox
        {
            Left = 26, Top = 522, Width = 524, Height = 182,
            ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 8.75F)
        };

        card.Controls.AddRange(new Control[] { title, _btnOpenDataFolder, _btnRefreshHistory, _grid, lblLog, _txtLog });
        _btnRefreshHistory.Click += (_, _) => LoadHistory();
        _btnOpenDataFolder.Click += (_, _) => OpenFolder(DataFolder);
        return card;
    }

    private RoundedPanel BuildSettingsPage()
    {
        var card = new RoundedPanel { Left = 264, Top = 26, Width = 576, Height = 730, Radius = 18, Visible = false };

        var title = new Label
        {
            Text = "设置",
            Left = 26, Top = 24, Width = 200, Height = 28,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = TextDark
        };

        var about = new RoundedPanel
        {
            Left = 26, Top = 66, Width = 524, Height = 150, Radius = 12,
            FillColor = Color.FromArgb(0xFA, 0xFB, 0xFF),
            BorderColor = Color.FromArgb(0xE9, 0xED, 0xF7)
        };
        var dfShown = _screenshotMode
            ? "%APPDATA%\\CacheMover\\history.jsonl（迁移记录）"
            : DataFolder + "\\history.jsonl（迁移记录）";
        var aboutText = new Label
        {
            Text = "缓存搬家 CacheMover  v1.2.0\r\n\r\n" +
                   "原理：创建目录联接（Junction），把 C 盘文件夹映射到其他盘。\r\n" +
                   "软件以为缓存还在 C 盘，实际数据在其他盘。全程无需管理员权限。\r\n\r\n" +
                   $"数据位置：{dfShown}",
            Left = 20, Top = 18, Width = 484, Height = 120,
            ForeColor = TextBody,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        about.Controls.Add(aboutText);

        var btnData = new GhostButton { Text = "打开迁移记录文件夹", Left = 26, Top = 232, Width = 180, Height = 34 };
        var btnApp = new GhostButton { Text = "打开程序所在文件夹", Left = 220, Top = 232, Width = 180, Height = 34 };

        var tips = new RoundedPanel
        {
            Left = 26, Top = 286, Width = 524, Height = 210, Radius = 12,
            FillColor = Color.FromArgb(0xEA, 0xF2, 0xFF),
            BorderColor = Color.FromArgb(0xD5, 0xE4, 0xFB)
        };
        var tipsText = new Label
        {
            Text = "安全提示\r\n\r\n" +
                   "•  迁移前先关闭正在使用对应缓存的软件（微信、浏览器、游戏等）。\r\n" +
                   "•  原文件夹会保留为 .bak 备份，确认软件正常后再删除备份释放空间。\r\n" +
                   "•  部分软件大版本更新可能使联接失效：若 C 盘空间回升，请检查原文件夹\r\n    是否变成了普通文件夹（迁移记录里有当时的路径）。\r\n" +
                   "•  游戏优先用 Steam / Epic 自带的移动库功能；OneDrive 目录不建议迁移。",
            Left = 20, Top = 16, Width = 488, Height = 184,
            ForeColor = Color.FromArgb(0x55, 0x68, 0x8C),
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        tips.Controls.Add(tipsText);

        card.Controls.AddRange(new Control[] { title, about, btnData, btnApp, tips });
        btnData.Click += (_, _) => OpenFolder(DataFolder);
        btnApp.Click += (_, _) => OpenFolder(AppContext.BaseDirectory);
        return card;
    }

    private ContextMenuStrip BuildPresetMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Font = new Font("Microsoft YaHei UI", 9.5F);
        foreach (var p in Presets.All)
        {
            var item = new ToolStripMenuItem(p.Name) { ToolTipText = p.Path };
            item.Click += (_, _) => UsePreset(p);
            menu.Items.Add(item);
        }
        return menu;
    }

    private void ShowPage(int index)
    {
        bool home = index == 0;
        _pageHome.Visible = home;
        _avatarHeader.Visible = _lblAppTitle.Visible = _lblAppSub.Visible = _note.Visible = home;
        _pageHistory.Visible = index == 1;
        _pageSettings.Visible = index == 2;
        _navHome.Selected = index == 0;
        _navHistory.Selected = index == 1;
        _navSettings.Selected = index == 2;
        _navHome.Invalidate();
        _navHistory.Invalidate();
        _navSettings.Invalidate();
        if (index == 1) LoadHistory();
    }

    // ================= 检查 =================

    private void OnPathChanged()
    {
        _btnGo.Text = "创建软链接";
        _timer.Stop();
        _timer.Start();
    }

    private async Task RunCheckAsync()
    {
        if (_busy) return;
        int run = Interlocked.Increment(ref _checkRun);
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        var ct = cts.Token;

        try
        {
            var src = _inpSrc.Text.Trim();
            var dst = _inpDst.Text.Trim();
            _srcSize = -1;

            if (src.Length > 0 && Directory.Exists(src))
            {
                _srcInfo = "正在统计文件夹大小…";
                UpdateSizeLine();
                var (files, bytes) = await Task.Run(() => Mover.Stats(src, ct), ct);
                if (run != _checkRun) return;
                _srcSize = bytes;
                _srcInfo = $"源：{Util.FmtBytes(bytes)}（{files:N0} 个文件）";
            }
            else if (src.Length > 0)
            {
                if (run != _checkRun) return;
                _srcSize = -1;
                _srcInfo = "源：路径不存在";
            }
            else
            {
                if (run != _checkRun) return;
                _srcInfo = "";
            }

            UpdateDstInfo();
            UpdateSizeLine();

            _results = await Task.Run(() => SafetyChecker.Check(src, dst, _srcSize, ct), ct);
            if (run != _checkRun || ct.IsCancellationRequested) return;
            RenderResults();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            if (run == _checkRun)
            {
                _results = new List<CheckResult> { new(CheckLevel.Fail, "检查过程出错：" + ex.Message) };
                RenderResults();
            }
        }
        finally
        {
            if (run == _checkRun) UpdateButtons();
        }
    }

    private void UpdateDstInfo()
    {
        var dst = _inpDst.Text.Trim();
        if (dst.Length == 0) { _dstInfo = ""; return; }
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dst));
            if (string.IsNullOrEmpty(root)) { _dstInfo = ""; return; }
            var drive = new DriveInfo(root);
            _dstInfo = drive.IsReady
                ? $"目标 {drive.Name} 剩余 {Util.FmtBytes(drive.AvailableFreeSpace)}（{drive.DriveFormat}）"
                : $"目标 {root} 分区不可用";
        }
        catch
        {
            _dstInfo = "";
        }
    }

    private void UpdateSizeLine()
    {
        _lblSizeLine.Text = string.Join("      ｜      ",
            new[] { _srcInfo, _dstInfo }.Where(s => s.Length > 0));
    }

    private void RenderResults()
    {
        _lstChecks.Items.Clear();
        if (_inpSrc.Text.Trim().Length == 0 && _inpDst.Text.Trim().Length == 0)
        {
            _lstChecks.Items.Add("ℹ 请先选择文件夹：手动浏览、拖拽进来，或从「从常用位置选择」一键填入");
            _lblStatus.Text = "";
            return;
        }
        foreach (var r in _results) _lstChecks.Items.Add(Symbol(r.Level) + " " + r.Message);
        var fails = _results.Count(r => r.Level == CheckLevel.Fail);
        var warns = _results.Count(r => r.Level == CheckLevel.Warn);
        _lblStatus.Text = _results.Count == 0
            ? "检查中…"
            : fails > 0
                ? $"发现 {fails} 个必须解决的问题，解决后才能迁移"
                : warns > 0
                    ? $"有 {warns} 条提醒，确认没问题后可以开始迁移"
                    : "一切正常，可以开始迁移";
    }

    private static string Symbol(CheckLevel level) => level switch
    {
        CheckLevel.Ok => "✔",
        CheckLevel.Warn => "⚠",
        _ => "✘"
    };

    private void LstChecks_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var text = _lstChecks.Items[e.Index].ToString() ?? "";
        var color = text.StartsWith("✘") ? Color.Firebrick
            : text.StartsWith("⚠") ? Color.FromArgb(0xC0, 0x8A, 0x2E)
            : text.StartsWith("✔") ? Color.FromArgb(0x2E, 0x8B, 0x62)
            : TextGray;
        e.DrawBackground();
        using var brush = new SolidBrush(color);
        e.Graphics.DrawString(text, _lstChecks.Font, brush, e.Bounds);
    }

    private void UpdateButtons()
    {
        bool noFail = _results.Count > 0 && _results.All(r => r.Level != CheckLevel.Fail);
        _btnGo.Enabled = !_busy && _srcSize >= 0 && noFail;
    }

    // ================= 迁移 =================

    private async Task StartMoveAsync()
    {
        var src = _inpSrc.Text.Trim();
        var dst = _inpDst.Text.Trim();
        if (_busy || src.Length == 0 || dst.Length == 0) return;

        int warns = _results.Count(r => r.Level == CheckLevel.Warn);
        var confirm = $"即将迁移：\n\n源（转移）：{src}\n目标：{dst}\n大小：{Util.FmtBytes(_srcSize)}\n\n" +
                      "⚠ 迁移前请先关闭正在使用该文件夹的软件（微信、浏览器、游戏等）。\n" +
                      "原文件夹会保留为备份，确认软件正常后再删除备份释放空间。\n" +
                      (warns > 0 ? $"\n注意：当前有 {warns} 条提醒，请确认可以接受。\n" : "") +
                      "\n确定开始吗？";
        if (MessageBox.Show(confirm, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetBusy(true);
        _lblBak.Text = "";
        _btnDeleteBak.Visible = false;
        _btnOpenDst.Visible = false;

        var log = new Progress<string>(s =>
        {
            Log(s);
            if (s.Length > 2 && "①②③④⑤⑥".Contains(s[0]))
                _lblStatus.Text = s[2..].Trim();
        });
        var pct = new Progress<int>(v => { _progress.Value = v; _progress.Invalidate(); });

        _workCts = new CancellationTokenSource();
        Log($"=== 开始迁移：{src}  →  {dst} ===");
        MoveResult result;
        try
        {
            result = await Mover.MoveAsync(src, dst, log, pct, _workCts.Token);
        }
        finally
        {
            SetBusy(false);
            _progress.Visible = false;
        }

        if (result.Success)
        {
            _lblStatus.Text = $"迁移完成！释放的 {Util.FmtBytes(result.Bytes)} 在删除备份后生效";
            _bakPath = result.BakPath;
            _lblBak.Text = $"备份保留在：{result.BakPath}（确认软件正常后再点下方按钮删除）";
            _btnDeleteBak.Visible = true;
            _btnOpenDst.Visible = true;
            _btnGo.Text = "迁移完成 ✔";
            Log("=== 迁移成功 ===");
            LoadHistory();
        }
        else
        {
            _lblStatus.Text = "迁移未完成：" + result.Error;
            Log("=== 迁移中止：" + result.Error + " ===");
            _ = RunCheckAsync();
        }
    }

    private async Task DeleteBakAsync()
    {
        if (_busy || _bakPath == null) return;
        var ask = MessageBox.Show(
            "确认永久删除备份吗？\n\n" + _bakPath +
            "\n\n删除后无法恢复。建议先打开使用该缓存的软件，确认一切正常。",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (ask != DialogResult.Yes) return;

        var bak = _bakPath;
        SetBusy(true);
        _btnGo.Text = "正在删除备份…";
        _lblStatus.Text = "正在删除备份…";
        try
        {
            await Task.Run(() => Mover.ForceDeleteDir(bak));
            _lblBak.Text = "备份已删除，C 盘空间已释放。";
            _lblStatus.Text = "备份删除完成";
            _btnDeleteBak.Visible = false;
            _bakPath = null;
            Log("备份已删除：" + bak);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "删除备份失败（可能仍被占用），可稍后重试或手动删除";
            Log("删除备份失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _inpSrc.Enabled = !busy;
        _inpDst.Enabled = !busy;
        _btnPresets.Enabled = !busy;
        _btnGo.Enabled = !busy;
        _btnCancelSmall.Visible = busy;
        if (busy)
        {
            _progress.Value = 0;
            _progress.Visible = true;
            _btnGo.Text = "正在迁移…";
        }
        else
        {
            _btnGo.Text = "创建软链接";
        }
        UseWaitCursor = busy;
    }

    // ================= 杂项 =================

    private void UsePreset(Preset p)
    {
        _inpSrc.Text = p.Path;
        if (_inpDst.Text.Trim().Length == 0)
        {
            var suggestion = SuggestDst(p.Path);
            if (suggestion != null) _inpDst.Text = suggestion;
        }
        Log($"选择常用位置：{p.Name} → {p.Path}" + (p.Note.Length > 0 ? "（" + p.Note + "）" : ""));
    }

    private static string? SuggestDst(string src)
    {
        try
        {
            var srcRoot = Path.GetPathRoot(Path.GetFullPath(src));
            var drive = DriveInfo.GetDrives()
                .Where(x => x.IsReady && x.DriveType == DriveType.Fixed
                            && !string.Equals(x.Name, srcRoot, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(x.Name, @"C:\", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.AvailableFreeSpace)
                .FirstOrDefault();
            if (drive == null) return null;
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(src)));
            return Path.Combine(drive.Name, "CacheLink", string.IsNullOrEmpty(name) ? "cache" : name);
        }
        catch
        {
            return null;
        }
    }

    private void Browse(InputField field, string title)
    {
        using var dlg = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) field.Text = dlg.SelectedPath;
    }

    private void OpenDst()
    {
        var dst = _inpDst.Text.Trim();
        if (dst.Length > 0) OpenFolder(dst);
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Process.Start("explorer.exe", path);
            else MessageBox.Show("文件夹不存在：" + path, Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开文件夹失败：" + ex.Message, Text);
        }
    }

    private void LoadHistory()
    {
        _grid.Rows.Clear();
        // 截图模式展示示例数据，不读取真实迁移记录
        if (_screenshotMode)
        {
            _grid.Rows.Add("2026-07-01 10:00:00", "C:\\Users\\示例用户\\AppData\\Local\\示例软件\\Cache", "D:\\CacheLink\\Cache", Util.FmtBytes(5368709120));
            _grid.Rows.Add("2026-07-15 18:30:00", "C:\\Users\\示例用户\\AppData\\Roaming\\另一软件\\Temp", "D:\\CacheLink\\Temp", Util.FmtBytes(1073741824));
            return;
        }
        try
        {
            var file = Path.Combine(DataFolder, "history.jsonl");
            if (!File.Exists(file)) return;
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.Trim().Length == 0) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    string S(string name) => r.TryGetProperty(name, out var e) ? e.ToString() : "";
                    long bytes = r.TryGetProperty("bytes", out var b) && b.TryGetInt64(out var v) ? v : 0;
                    _grid.Rows.Add(S("time"), S("src"), S("dst"), bytes > 0 ? Util.FmtBytes(bytes) : "");
                }
                catch { /* 单行解析失败跳过 */ }
            }
        }
        catch (Exception ex)
        {
            Log("读取历史记录失败：" + ex.Message);
        }
    }

    private void Log(string msg) => _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
}
