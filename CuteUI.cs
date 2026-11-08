using System.Drawing.Drawing2D;

namespace CacheMover;

public static class UiUtil
{
    public static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Font HandFont(float size) =>
        TryFont("华文行楷", size) ?? TryFont("楷体", size) ?? TryFont("KaiTi", size)
        ?? new Font("Microsoft YaHei UI", size, FontStyle.Italic);

    private static Font? TryFont(string name, float size)
    {
        try
        {
            var f = new Font(name, size);
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
        }
        catch { /* 字体不存在 */ }
        return null;
    }
}

/// <summary>
/// 自定义绘制控件的公共基类：背景真正透明（透出父控件的绘制），
/// 圆角内容画在透明表面上。所有界面控件继承此类以避免白块/黑块伪影。
/// </summary>
public abstract class TranslucentControl : Control
{
    protected TranslucentControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }
}

/// <summary>圆角卡片（可半透明填充，如侧边栏的磨砂白）。</summary>
public class RoundedPanel : Panel
{
    public int Radius { get; set; } = 16;
    public Color FillColor { get; set; } = Color.White;
    public Color BorderColor { get; set; } = Color.FromArgb(0xE9, 0xED, 0xF7);
    public bool ShowBorder { get; set; } = true;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiUtil.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using (var b = new SolidBrush(FillColor)) g.FillPath(b, path);
        if (ShowBorder) using (var p = new Pen(BorderColor)) g.DrawPath(p, path);
    }
}

/// <summary>圆角小图标按钮（输入框内的文件夹按钮、窗口控制按钮等）。</summary>
public class IconButton : TranslucentControl
{
    public Image? IconImage { get; set; }
    public Image? HoverImage { get; set; }
    private bool _hover;

    public IconButton() => Cursor = Cursors.Hand;

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover && Enabled)
            using (var path = UiUtil.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                g.FillPath(b, path);
        var icon = _hover && Enabled && HoverImage != null ? HoverImage : IconImage;
        if (icon == null) return;
        int size = Math.Min(Width, Height) - 8;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (!Enabled) ControlPaint.DrawImageDisabled(g, icon, (Width - size) / 2, (Height - size) / 2, Color.White);
        else g.DrawImage(icon, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size));
    }
}

/// <summary>侧边导航按钮。</summary>
public class NavButton : TranslucentControl
{
    public Image? Icon { get; set; }
    public Image? IconActive { get; set; }
    public bool Selected { get; set; }
    private bool _hover;

    private static readonly Font FontNormal = new("Microsoft YaHei UI", 10F);
    private static readonly Font FontSelected = new("Microsoft YaHei UI", 10F, FontStyle.Bold);

    public NavButton() => Cursor = Cursors.Hand;

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Selected || _hover)
        {
            using var path = UiUtil.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12);
            using var b = new SolidBrush(Selected ? Color.FromArgb(0xD9, 0xE6, 0xFB) : Color.FromArgb(150, 255, 255, 255));
            g.FillPath(b, path);
        }
        var color = Selected ? Color.FromArgb(0x3D, 0x6F, 0xD4) : Color.FromArgb(0x5E, 0x6B, 0x87);
        var icon = Selected ? IconActive : Icon;
        if (icon != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(icon, 16, (Height - 22) / 2, 22, 22);
        }
        TextRenderer.DrawText(g, Text, Selected ? FontSelected : FontNormal,
            new Point(50, (Height - Font.Height) / 2), color);
    }
}

/// <summary>蓝色渐变大按钮。</summary>
public class GradientButton : TranslucentControl
{
    private bool _hover, _down;

    public GradientButton()
    {
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 11.25F, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiUtil.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 14);
        Color top, bottom;
        if (!Enabled)
        {
            // 未就绪也保持蓝色系，只是浅一档
            top = Color.FromArgb(0x9A, 0xBA, 0xF5);
            bottom = Color.FromArgb(0xB3, 0xCC, 0xF9);
        }
        else if (_down)
        {
            top = bottom = Color.FromArgb(0x44, 0x79, 0xDB);
        }
        else
        {
            top = _hover ? Color.FromArgb(0x5D, 0x96, 0xF2) : Color.FromArgb(0x4F, 0x8B, 0xEF);
            bottom = _hover ? Color.FromArgb(0x8C, 0xBC, 0xFA) : Color.FromArgb(0x7F, 0xB0, 0xF7);
        }
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), top, bottom, 90f))
            g.FillPath(brush, path);

        var textColor = Color.White;
        var size = TextRenderer.MeasureText(Text, Font);
        const int iconSize = 18, gap = 8;
        int total = iconSize + gap + size.Width;
        int x0 = (Width - total) / 2;
        int cy = Height / 2;

        // 白色链条图标（与吉祥物徽章同一套链节造型）
        using (var linkIcon = DrawIcons.Make("link", textColor, 22))
            g.DrawImage(linkIcon, x0 - 2, cy - 11);
        TextRenderer.DrawText(g, Text, Font, new Point(x0 + iconSize + gap, (Height - Font.Height) / 2), textColor);
    }
}

/// <summary>次要白色小按钮（描边风格）。</summary>
public class GhostButton : TranslucentControl
{
    public bool ShowBorder { get; set; } = true;
    private bool _hover;

    public GhostButton()
    {
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9.5F);
        ForeColor = Color.FromArgb(0x5B, 0x8D, 0xEF);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiUtil.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 10);
        using (var b = new SolidBrush(_hover && Enabled ? Color.FromArgb(0xF1, 0xF5, 0xFE) : Color.White))
            g.FillPath(b, path);
        if (ShowBorder) using (var p = new Pen(Enabled ? Color.FromArgb(0xD9, 0xE1, 0xF2) : Color.FromArgb(0xEB, 0xEE, 0xF5))) g.DrawPath(p, path);
        var color = Enabled ? ForeColor : Color.FromArgb(0xB9, 0xC2, 0xD8);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>圆角输入框：无边框 TextBox + 占位文字 + 右侧文件夹按钮。</summary>
public class InputField : Panel
{
    public readonly TextBox Box = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 10F)
    };
    public readonly IconButton BrowseButton;
    private readonly Label _placeholder = new();
    private bool _focus;

    public InputField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 44;

        BrowseButton = new IconButton
        {
            Size = new Size(36, 36),
            IconImage = (Image?)IconAssets.FolderMini
                        ?? DrawIcons.Make("folder", Color.FromArgb(0x8A, 0x97, 0xB5), 18)
        };

        _placeholder.ForeColor = Color.FromArgb(0xB9, 0xC2, 0xD8);
        _placeholder.BackColor = Color.Transparent;
        _placeholder.Font = new Font("Microsoft YaHei UI", 9.75F);
        _placeholder.AutoSize = false;
        _placeholder.Cursor = Cursors.IBeam;

        Controls.Add(Box);
        Controls.Add(BrowseButton);
        Controls.Add(_placeholder);
        _placeholder.BringToFront();

        _placeholder.Click += (_, _) => Box.Focus();
        Click += (_, _) => Box.Focus();
        Box.Enter += (_, _) => { _focus = true; UpdatePlaceholder(); Invalidate(); };
        Box.Leave += (_, _) => { _focus = false; UpdatePlaceholder(); Invalidate(); };
        Box.TextChanged += (_, _) => UpdatePlaceholder();
        Resize += (_, _) => Relayout();
        AllowDrop = true;
        DragEnter += DragHandler;
        DragDrop += DropHandler;
        Box.AllowDrop = true;
        Box.DragEnter += DragHandler;
        Box.DragDrop += DropHandler;

        Relayout();
        UpdatePlaceholder();
    }

    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }

    private string _placeholderText = "";
    public string Placeholder
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value;
            _placeholder.Text = value;
            UpdatePlaceholder();
        }
    }

    private static void DragHandler(object? s, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private void DropHandler(object? s, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) Box.Text = paths[0];
    }

    private void Relayout()
    {
        int boxTop = (Height - Box.Height) / 2;
        Box.SetBounds(16, boxTop, Width - 16 - 52, Box.Height);
        BrowseButton.SetBounds(Width - 44, (Height - 36) / 2, 36, 36);
        _placeholder.SetBounds(17, (Height - _placeholder.Height) / 2, Width - 68, _placeholder.Height);
    }

    private void UpdatePlaceholder() => _placeholder.Visible = Box.Text.Length == 0 && !_focus;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiUtil.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using (var b = new SolidBrush(Color.White)) g.FillPath(b, path);
        using var pen = new Pen(_focus ? Color.FromArgb(0x6D, 0x9E, 0xF6) : Color.FromArgb(0xE1, 0xE6, 0xF2));
        g.DrawPath(pen, path);
        // 仅在 TextBox 被隐藏时（--screenshot 截图模式）绘制，避免与上层 Label 叠成重影
        if (Placeholder.Length > 0 && Box.Text.Length == 0 && !_focus && !Box.Visible)
            TextRenderer.DrawText(g, _placeholderText, _placeholder.Font,
                new Point(17, (Height - _placeholder.Font.Height) / 2), Color.FromArgb(0xB9, 0xC2, 0xD8));
    }
}

/// <summary>细圆角进度条。</summary>
public class CuteProgressBar : TranslucentControl
{
    public int Value { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = UiUtil.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
        using (var b = new SolidBrush(Color.FromArgb(0xE8, 0xED, 0xF8)))
            g.FillPath(b, path);
        int w = (int)((Width - 1) * Math.Clamp(Value, 0, 100) / 100.0);
        if (w > Height)
            using (var path = UiUtil.RoundedPath(new Rectangle(0, 0, w, Height - 1), Height / 2))
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, w, Height),
                Color.FromArgb(0x4F, 0x8B, 0xEF), Color.FromArgb(0x7F, 0xB0, 0xF7), 0f))
                g.FillPath(brush, path);
    }
}

/// <summary>磨砂对话气泡：半透明白底、极浅描边、文字靠左，无尾巴。</summary>
public class SpeechBubble : TranslucentControl
{
    public string Message { get; set; } = "";

    public SpeechBubble() => Font = new Font("Microsoft YaHei UI", 9F);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiUtil.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 16);
        using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillPath(b, path);
        using (var p = new Pen(Color.FromArgb(70, 0xC9, 0xDC, 0xF6))) g.DrawPath(p, path);
        var rect = new Rectangle(16, 8, Width - 32, Height - 16);
        TextRenderer.DrawText(g, Message, Font, rect, Color.FromArgb(0x8A, 0x97, 0xB5),
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>旋转的手写风格文字（右上角便签）。</summary>
public class NoteLabel : TranslucentControl
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(Width / 2f, Height / 2f);
        g.RotateTransform(-8f);
        using var f = UiUtil.HandFont(13.5F);
        using var b = new SolidBrush(Color.FromArgb(0xA5, 0xBE, 0xE8));
        var sz = g.MeasureString(Text, f);
        g.DrawString(Text, f, b, -sz.Width / 2, -sz.Height / 2);
    }
}

/// <summary>仓鼠吉祥物头像：内嵌图片圆形裁切 + 浅蓝描边 + 右下角链接小徽章。缺图时回退到手绘小猫。</summary>
public class MascotAvatar : TranslucentControl
{
    private static Image? _img;
    private static bool _tried;

    private static Image? MascotImage
    {
        get
        {
            if (!_tried)
            {
                _tried = true;
                try
                {
                    using var s = typeof(MascotAvatar).Assembly.GetManifestResourceStream("CacheMover.assets.mascot.png");
                    if (s != null) _img = Image.FromStream(s);
                }
                catch { /* 缺图时回退 */ }
            }
            return _img;
        }
    }

    /// <summary>是否在右下角画链接小徽章（标题区头像用）。</summary>
    public bool ShowLinkBadge { get; set; }

    /// <summary>自定义头像图（优先于内嵌吉祥物；图内自带元素时徽章不再叠加）。</summary>
    public Image? CustomImage { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        int size = Math.Min(Width, Height);
        var rect = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);

        if (CustomImage != null)
        {
            g.DrawImage(CustomImage, rect);
            return;
        }

        var img = MascotImage;
        if (img == null)
        {
            g.DrawImage(CatRenderer.Render(size), (Width - size) / 2, (Height - size) / 2, size, size);
            return;
        }

        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, rect);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            var oldClip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            g.DrawImage(img, rect);
            g.Clip = oldClip;
        }
        using (var ring = new Pen(Color.FromArgb(0xBF, 0xD5, 0xF2), Math.Max(1.6f, size / 30f)))
            g.DrawEllipse(ring, rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);

        if (ShowLinkBadge)
        {
            int bs = Math.Max(16, size / 3);
            var br = new Rectangle(rect.Right - bs + 2, rect.Bottom - bs + 2, bs, bs);
            using (var b = new SolidBrush(Color.FromArgb(0x6D, 0x9E, 0xF6))) g.FillEllipse(b, br);
            using (var edge = new Pen(Color.White, 2f)) g.DrawEllipse(edge, br);
            using var icon = DrawIcons.Make("link", Color.White, bs - 8);
            g.DrawImage(icon, br.X + 4, br.Y + 4);
        }
    }
}

public static class CatRenderer
{
    private static readonly Dictionary<int, Bitmap> Cache = new();

    public static Bitmap Render(int size)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(size, out var cached)) return cached;
            var bmp = Draw(size);
            Cache[size] = bmp;
            return bmp;
        }
    }

    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = size / 100f;
        g.ScaleTransform(k, k);

        using var white = new SolidBrush(Color.White);
        using var outline = new Pen(Color.FromArgb(0xD6, 0xDE, 0xEE), 2.6f);
        outline.LineJoin = LineJoin.Round;

        var earL = new PointF[] { new(22, 42), new(14, 12), new(44, 26) };
        var earR = new PointF[] { new(78, 42), new(86, 12), new(56, 26) };
        foreach (var ear in new[] { earL, earR })
        {
            g.FillPolygon(white, ear);
            g.DrawPolygon(outline, ear);
        }
        using var pink = new SolidBrush(Color.FromArgb(0xF9, 0xCF, 0xDF));
        g.FillPolygon(pink, new PointF[] { new(25, 34), new(20, 18), new(37, 27) });
        g.FillPolygon(pink, new PointF[] { new(75, 34), new(80, 18), new(63, 27) });

        g.FillEllipse(white, 12, 24, 76, 64);
        g.DrawEllipse(outline, 12, 24, 76, 64);

        using var scarf = new SolidBrush(Color.FromArgb(0x6D, 0x9E, 0xF6));
        using var scarfPen = new Pen(Color.FromArgb(0x5A, 0x8B, 0xE8), 1.8f);
        var scarfPts = new PointF[] { new(34, 80), new(66, 80), new(50, 97) };
        g.FillPolygon(scarf, scarfPts);
        g.DrawPolygon(scarfPen, scarfPts);

        using var eye = new SolidBrush(Color.FromArgb(0x3A, 0x42, 0x56));
        g.FillEllipse(eye, 33, 51, 10, 12);
        g.FillEllipse(eye, 57, 51, 10, 12);
        using var shine = new SolidBrush(Color.White);
        g.FillEllipse(shine, 36, 54, 3.4f, 3.8f);
        g.FillEllipse(shine, 60, 54, 3.4f, 3.8f);

        using var blush = new SolidBrush(Color.FromArgb(0xF9, 0xD2, 0xE0));
        g.FillEllipse(blush, 22, 63, 13, 7.5f);
        g.FillEllipse(blush, 65, 63, 13, 7.5f);

        using var mouth = new Pen(Color.FromArgb(0x3A, 0x42, 0x56), 2f);
        mouth.StartCap = LineCap.Round;
        mouth.EndCap = LineCap.Round;
        g.DrawArc(mouth, 41, 57, 9, 9, 200, 140);
        g.DrawArc(mouth, 50, 57, 9, 9, 200, 140);

        return bmp;
    }
}

/// <summary>玻璃质感文件夹图标（本地素材，缺失时界面回退到手绘版）。</summary>
public static class IconAssets
{
    private static Image? _src, _dst, _mini, _sticker, _cat, _gear;

    /// <summary>加载内嵌素材并等比缩放到 target 见方（透明留白补齐），失败返回 null。</summary>
    private static Image? Load(string resource, int target)
    {
        try
        {
            using var s = typeof(IconAssets).Assembly.GetManifestResourceStream(resource);
            if (s == null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            using var raw = Image.FromStream(ms);
            var bmp = new Bitmap(target, target);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Math.Min((float)target / raw.Width, (float)target / raw.Height);
            int w = Math.Max(1, (int)(raw.Width * k));
            int h = Math.Max(1, (int)(raw.Height * k));
            g.DrawImage(raw, new Rectangle((target - w) / 2, (target - h) / 2, w, h));
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>「要转移的文件夹」蓝色玻璃图标。</summary>
    public static Image? FolderSrc => _src ??= Load("CacheMover.assets.folder-src.png", 96);

    /// <summary>「目标文件夹」薄荷绿玻璃图标。</summary>
    public static Image? FolderDst => _dst ??= Load("CacheMover.assets.folder-dst.png", 96);

    /// <summary>输入框浏览按钮的蓝灰文件夹图标。</summary>
    public static Image? FolderMini => _mini ??= Load("CacheMover.assets.folder-mini.png", 48);

    /// <summary>侧边栏底部的云朵气泡+小猫贴纸。</summary>
    public static Image? CloudSticker => _sticker ??= Load("CacheMover.assets.cloud-sticker.png", 300);

    /// <summary>玻璃蓝圆里的萌系小猫头像（自带链接徽章）。</summary>
    public static Image? CatAvatar => _cat ??= Load("CacheMover.assets.cat-avatar.png", 160);

    /// <summary>设置导航的蓝灰齿轮图标。</summary>
    public static Image? Gear => _gear ??= Load("CacheMover.assets.gear.png", 44);
}

/// <summary>运行时绘制的扁平图标。</summary>
public static class DrawIcons
{
    public static Bitmap Make(string kind, Color color, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = Math.Max(1.8f, size / 10f);
        using var pen = new Pen(color, w)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.ScaleTransform(size / 24f, size / 24f);
        switch (kind)
        {
            case "home":
                g.DrawLines(pen, new PointF[] { new(3, 11), new(12, 3.5f), new(21, 11) });
                g.DrawLines(pen, new PointF[] { new(5.5f, 10), new(5.5f, 20), new(18.5f, 20), new(18.5f, 10) });
                g.DrawLines(pen, new PointF[] { new(10, 20), new(10, 14.5f), new(14, 14.5f), new(14, 20) });
                break;
            case "clock":
                g.DrawEllipse(pen, 4, 4, 16, 16);
                g.DrawLines(pen, new PointF[] { new(12, 7.5f), new(12, 12.5f), new(15.5f, 14.5f) });
                break;
            case "gear":
                // 经典齿轮：8 个梯形齿 + 圆盘，中心孔用 SourceCopy 擦除
                using (var gearPath = new GraphicsPath())
                using (var gearBrush = new SolidBrush(color))
                {
                    const float cx = 12, cy = 12, rRoot = 7.4f, rTip = 10.3f;
                    var pts = new List<PointF>();
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        PointF P(double ang, float r) => new(cx + (float)Math.Cos(ang) * r, cy + (float)Math.Sin(ang) * r);
                        pts.Add(P(a - 0.36, rRoot));  // 齿根左
                        pts.Add(P(a - 0.19, rTip));   // 齿尖左
                        pts.Add(P(a + 0.19, rTip));   // 齿尖右
                        pts.Add(P(a + 0.36, rRoot));  // 齿根右
                    }
                    gearPath.AddPolygon(pts.ToArray());
                    g.FillPath(gearBrush, gearPath);
                }
                {
                    var oldMode = g.CompositingMode;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using var erase = new SolidBrush(Color.Transparent);
                    g.FillEllipse(erase, 9.2f, 9.2f, 5.6f, 5.6f);
                    g.CompositingMode = oldMode;
                }
                break;
            case "folder":
                using (var folderPen = new Pen(color, 1.4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                })
                {
                    // 主体偏方、圆角 2；tab 左缘与主体左缘对齐
                    using (var path = UiUtil.RoundedPath(new Rectangle(6, 8, 14, 12), 2))
                        g.DrawPath(folderPen, path);
                    g.DrawLines(folderPen, new PointF[] { new(6f, 8), new(6f, 4.5f), new(10.5f, 4.5f), new(12.5f, 8) });
                }
                break;
            case "link":
                // 标准链条字形（Segoe UI Symbol 的 🔗），旋转 -45° 朝右上
                {
                    g.ResetTransform();
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    var linkState = g.Save();
                    g.TranslateTransform(size / 2f, size / 2f);
                    g.RotateTransform(-45);
                    using var linkFont = new Font("Segoe UI Symbol", size * 0.82f, FontStyle.Regular, GraphicsUnit.Pixel);
                    using var linkFormat = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    using var linkBrush = new SolidBrush(color);
                    g.DrawString("\uD83D\uDD17", linkFont, linkBrush,
                        new RectangleF(-size, -size, size * 2, size * 2), linkFormat);
                    g.Restore(linkState);
                }
                break;
            case "close":
                g.DrawLines(pen, new PointF[] { new(6.5f, 6.5f), new(17.5f, 17.5f) });
                g.DrawLines(pen, new PointF[] { new(17.5f, 6.5f), new(6.5f, 17.5f) });
                break;
            case "min":
                g.DrawLine(pen, 5.5f, 14, 18.5f, 14);
                break;
        }
        return bmp;
    }

    /// <summary>圆角方块底 + 文件夹图标（输入区标题图标）。</summary>
    public static Bitmap FolderTile(int size, Color tile, Color fg)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = UiUtil.RoundedPath(new Rectangle(0, 0, size - 1, size - 1), size / 4))
        using (var b = new SolidBrush(tile))
            g.FillPath(b, path);
        int isz = (int)(size * 0.56f);
        using var icon = Make("folder", fg, isz);
        g.DrawImage(icon, (size - isz) / 2f, (size - isz) / 2f);
        return bmp;
    }

    /// <summary>实心圆形信息图标（i）。</summary>
    public static Bitmap InfoBadge(int size, Color bg)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(bg)) g.FillEllipse(b, 0, 0, size - 1, size - 1);
        float w = Math.Max(2f, size / 10f);
        using var p = new Pen(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p, size / 2f, size * 0.46f, size / 2f, size * 0.72f);
        using (var wb = new SolidBrush(Color.White))
            g.FillEllipse(wb, size / 2f - w / 2f, size * 0.24f, w, w);
        return bmp;
    }
}
