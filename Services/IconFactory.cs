using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ImageSource = System.Windows.Media.ImageSource;

// ============================================================
// 用途：应用与菜单图标生成——加载内嵌/外部图标并随主题单色着色，
// 带缓存与运行时回退绘制扬声器造型。
// ============================================================
namespace AudioSwitcher.Services;

/// <summary>
/// 应用图标：优先使用内嵌的自定义图标（Assets/app-icon.png），
/// 其次程序目录下的图标文件（icon.ico / icon.png / app.ico），
/// 最后回退为运行时绘制的扬声器造型。
/// </summary>
public static class IconFactory
{
    private const string EmbeddedIconName = "AudioSwitcher.Assets.app-icon.png";
    private static readonly Color Bg = Color.FromArgb(0x00, 0x49, 0xFF);

    // 菜单音量/麦克风图标缓存：资源名 + 主题 只有有限的几种组合，
    // 缓存冻结后的 BitmapSource，避免滑块拖动时每次 ValueChanged 都重新解码/着色。
    private static readonly object MenuIconCacheLock = new();
    private static readonly Dictionary<(string Resource, bool Dark), ImageSource> MenuIconCache = new();

    /// <summary>获取应用图标。优先使用内嵌的自定义图标，其次程序目录下的图标文件，最后绘制扬声器图标。</summary>
    /// <param name="size">目标图标尺寸。</param>
    /// <param name="dark">是否处于深色主题。深色任务栏用白色图标，浅色任务栏用黑色图标。</param>
    public static Icon Create(int size = 32, bool dark = true)
    {
        return LoadEmbeddedIcon(size, dark)
            ?? LoadCustomIcon(size, dark)
            ?? DrawSpeakerIcon(size);
    }

    /// <summary>加载内嵌的自定义图标（Assets/app-icon.png）。</summary>
    private static Icon? LoadEmbeddedIcon(int size, bool dark)
    {
        try
        {
            var asm = typeof(IconFactory).Assembly;
            using var stream = asm.GetManifestResourceStream(EmbeddedIconName);
            if (stream == null) return null;
            using var img = Image.FromStream(stream);
            return FromImage(img, size, dark);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按约定路径查找自定义图标文件并加载，找不到或损坏时返回 null。</summary>
    private static Icon? LoadCustomIcon(int size, bool dark)
    {
        string[] dirs =
        {
            AppContext.BaseDirectory,          // 程序（exe）所在目录
            Directory.GetCurrentDirectory(),   // 当前工作目录
        };
        string[] names = { "icon.ico", "icon.png", "app.ico" };

        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                try
                {
                    return FromFile(path, size, dark);
                }
                catch
                {
                    // 图标文件损坏时忽略，继续查找或回退到绘制图标
                }
            }
        }
        return null;
    }

    /// <summary>从图片文件加载图标并统一到目标尺寸。</summary>
    private static Icon FromFile(string path, int size, bool dark)
    {
        // .ico 直接取最接近目标尺寸的帧；其余格式按位图缩放
        if (string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            using var icon = new Icon(path);
            using var sized = new Icon(icon, size, size);
            return (Icon)sized.Clone();
        }

        using var img = Image.FromFile(path);
        return FromImage(img, size, dark);
    }

    /// <summary>把任意图片缩放为目标尺寸并按主题单调着色后转成图标。</summary>
    private static Icon FromImage(Image img, int size, bool dark)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, 0, 0, size, size);
        }
        Recolor(bmp, dark ? Color.White : Color.Black);
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// 把所有不透明像素统一着成目标颜色（保留透明度），形成纯色剪影，
    /// 保证在深色任务栏（白）与浅色任务栏（黑）上都清晰可见，不受原图颜色影响。
    /// 用 LockBits 整块读写内存，替代逐像素 GetPixel/SetPixel 封送，速度更快。
    /// </summary>
    private static void Recolor(Bitmap bmp, Color target)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int len = data.Stride * data.Height;
            var bytes = new byte[len];
            Marshal.Copy(data.Scan0, bytes, 0, len);
            for (int y = 0; y < data.Height; y++)
            {
                int rowStart = y * data.Stride;
                for (int x = 0; x < data.Width; x++)
                {
                    int i = rowStart + x * 4;
                    if (bytes[i + 3] < 32) continue; // 忽略近透明像素
                    bytes[i] = target.B;
                    bytes[i + 1] = target.G;
                    bytes[i + 2] = target.R;
                }
            }
            Marshal.Copy(bytes, 0, data.Scan0, len);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// 加载内嵌 PNG 菜单图标并按主题单色化（深色用白、浅色用黑），返回 WPF 位图源。
    /// 音量/麦克风图标原图为白色剪影，需随菜单背景着色才能在浅色主题下清晰可见。
    /// </summary>
    /// <param name="resourceName">内嵌资源名（如 "AudioSwitcher.Assets.volume.png"）。</param>
    /// <param name="dark">是否处于深色主题。</param>
    public static ImageSource? LoadMenuIcon(string resourceName, bool dark)
    {
        // 命中缓存直接返回冻结的位图源（滑块拖动热路径）
        var key = (resourceName, dark);
        lock (MenuIconCacheLock)
        {
            if (MenuIconCache.TryGetValue(key, out var cached)) return cached;
        }

        try
        {
            var asm = typeof(IconFactory).Assembly;
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var img = Image.FromStream(stream);
            using var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, img.Width, img.Height);
            }
            Recolor(bmp, dark ? Color.White : Color.Black);
            IntPtr hBitmap = bmp.GetHbitmap();
            ImageSource source;
            try
            {
                source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero,
                    Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
            }
            finally
            {
                DeleteObject(hBitmap);
            }
            lock (MenuIconCacheLock)
            {
                MenuIconCache[key] = source;
            }
            return source;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>运行时绘制扬声器造型图标（回退方案）。</summary>
    private static Icon DrawSpeakerIcon(int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var rect = new RectangleF(1, 1, size - 2, size - 2);
            using var path = RoundedRect(rect, size * 0.20f);
            using var bgBrush = new SolidBrush(Bg);
            g.FillPath(bgBrush, path);

            using var white = new SolidBrush(Color.White);
            float w = size;
            // 扬声器箱体
            g.FillRectangle(white, w * 0.22f, w * 0.36f, w * 0.20f, w * 0.28f);
            // 扬声器锥体
            var tri = new[]
            {
                new PointF(w * 0.42f, w * 0.34f),
                new PointF(w * 0.68f, w * 0.12f),
                new PointF(w * 0.68f, w * 0.88f),
            };
            g.FillPolygon(white, tri);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, r.Width);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
