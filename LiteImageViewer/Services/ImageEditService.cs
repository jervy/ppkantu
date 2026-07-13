using System.IO;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using LiteImageViewer.Models;
using LiteImageViewer.Utils;

namespace LiteImageViewer.Services;

/// <summary>
/// 图片编辑服务
/// </summary>
public class ImageEditService
{
    /// <summary>
    /// 旋转图片（使用 ImageSharp 处理，支持超大图）
    /// </summary>
    public async Task<string> RotateImageAsync(string inputPath, string outputPath, int degrees, int jpegQuality = 90)
    {
        return await Task.Run(() =>
        {
            using var image = Image.Load(inputPath);
            image.Mutate(x => x.Rotate(degrees));

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
            return outputPath;
        });
    }

    /// <summary>
    /// 翻转图片
    /// </summary>
    public async Task<string> FlipImageAsync(string inputPath, string outputPath, bool horizontal, int jpegQuality = 90)
    {
        return await Task.Run(() =>
        {
            using var image = Image.Load(inputPath);
            if (horizontal)
                image.Mutate(x => x.Flip(FlipMode.Horizontal));
            else
                image.Mutate(x => x.Flip(FlipMode.Vertical));

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
            return outputPath;
        });
    }

    /// <summary>
    /// 裁剪图片
    /// </summary>
    public async Task<string> CropImageAsync(string inputPath, string outputPath, Rectangle cropArea, int jpegQuality = 90)
    {
        return await Task.Run(() =>
        {
            using var image = Image.Load(inputPath);

            var startX = Math.Clamp(cropArea.X, 0, Math.Max(0, image.Width - 1));
            var startY = Math.Clamp(cropArea.Y, 0, Math.Max(0, image.Height - 1));
            var maxWidth = Math.Max(0, image.Width - startX);
            var maxHeight = Math.Max(0, image.Height - startY);
            var validWidth = Math.Max(0, Math.Min(cropArea.Width, maxWidth));
            var validHeight = Math.Max(0, Math.Min(cropArea.Height, maxHeight));

            if (validWidth <= 0 || validHeight <= 0)
                throw new InvalidOperationException("裁切区域超出图片范围或尺寸无效。");

            var validArea = new Rectangle(startX, startY, validWidth, validHeight);

            image.Mutate(x => x.Crop(validArea));

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
            return outputPath;
        });
    }

    /// <summary>
    /// 调整图片尺寸
    /// </summary>
    public async Task<string> ResizeImageAsync(string inputPath, string outputPath, int width, int height, int jpegQuality = 90)
    {
        return await Task.Run(() =>
        {
            using var image = Image.Load(inputPath);
            image.Mutate(x => x.Resize(width, height));

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
            return outputPath;
        });
    }

    /// <summary>
    /// 添加文字水印
    /// </summary>
    public async Task<string> AddTextAsync(string inputPath, string outputPath, string text,
        float x, float y, float fontSize = 24, string colorName = "Red", int jpegQuality = 90)
    {
        return await Task.Run(() =>
        {
            using var image = Image.Load(inputPath);
            // TODO: 实现文字绘制（需要引入字体处理）
            // SixLabors.ImageSharp.Drawing 可以实现文字绘制
            // image.Mutate(x => x.DrawText(text, font, color, new PointF(x, y)));
            _ = text; _ = x; _ = y; _ = fontSize; _ = colorName;

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
            return outputPath;
        });
    }

    /// <summary>
    /// 添加马赛克
    /// </summary>
    public async Task<string> AddMosaicAsync(string inputPath, string outputPath,
        int x, int y, int width, int height, int blockSize = 10, int jpegQuality = 90)
    {
        return await Task.Run(() =>
        {
            using var image = Image.Load<Rgba32>(inputPath);

            // 确保区域在图片范围内
            var startX = Math.Max(0, x);
            var startY = Math.Max(0, y);
            var endX = Math.Min(x + width, image.Width);
            var endY = Math.Min(y + height, image.Height);

            // 马赛克实现：将区域分成小块，每块用平均色填充
            for (int bx = startX; bx < endX; bx += blockSize)
            {
                for (int by = startY; by < endY; by += blockSize)
                {
                    var bw = Math.Min(blockSize, endX - bx);
                    var bh = Math.Min(blockSize, endY - by);

                    // 计算小块的平均颜色 (使用 Image<Rgba32> 直接索引)
                    float r = 0, g = 0, b = 0;
                    int count = 0;

                    for (int px = bx; px < bx + bw && px < image.Width; px++)
                    {
                        for (int py = by; py < by + bh && py < image.Height; py++)
                        {
                            var pixel = image[px, py];
                            r += pixel.R;
                            g += pixel.G;
                            b += pixel.B;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        var avgR = (byte)(r / count);
                        var avgG = (byte)(g / count);
                        var avgB = (byte)(b / count);

                        for (int px = bx; px < bx + bw && px < image.Width; px++)
                        {
                            for (int py = by; py < by + bh && py < image.Height; py++)
                            {
                                var old = image[px, py];
                                image[px, py] = new Rgba32(avgR, avgG, avgB, old.A);
                            }
                        }
                    }
                }
            }

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
            return outputPath;
        });
    }

    /// <summary>
    /// 保存图片（支持格式转换和质量设置）
    /// </summary>
    public async Task SaveAsync(string inputPath, string outputPath, int jpegQuality = 90)
    {
        await Task.Run(() =>
        {
            using var image = Image.Load(inputPath);
            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
        });
    }

    /// <summary>
    /// 根据扩展名获取编码器
    /// </summary>
    private static IImageEncoder GetEncoder(string extension, int jpegQuality) =>
        ImageFormatHelper.GetEncoder(extension, jpegQuality);

    /// <summary>
    /// 绘制标注到图片上（像素级绘制，兼容所有 ImageSharp 版本）
    /// </summary>
    public async Task DrawAnnotationsAsync(string inputPath, string outputPath,
        List<AnnotationShape> shapes, int imageWidth, int imageHeight, int jpegQuality = 90)
    {
        await Task.Run(() =>
        {
            using var image = Image.Load<Rgba32>(inputPath);

            foreach (var shape in shapes)
            {
                var c = ParseColorToRgba(shape.Color);
                var t = Math.Max(1, (int)shape.StrokeThickness);

                switch (shape.Tool)
                {
                    case AnnotationTool.Rectangle:
                    {
                        var x1 = Math.Max(0, (int)Math.Min(shape.Start.X, shape.End.X));
                        var y1 = Math.Max(0, (int)Math.Min(shape.Start.Y, shape.End.Y));
                        var x2 = Math.Min(image.Width - 1, (int)Math.Max(shape.Start.X, shape.End.X));
                        var y2 = Math.Min(image.Height - 1, (int)Math.Max(shape.Start.Y, shape.End.Y));
                        DrawRectOutline(image, x1, y1, x2, y2, c, t);
                        break;
                    }
                    case AnnotationTool.Ellipse:
                    {
                        var x1 = Math.Max(0, (int)Math.Min(shape.Start.X, shape.End.X));
                        var y1 = Math.Max(0, (int)Math.Min(shape.Start.Y, shape.End.Y));
                        var x2 = Math.Min(image.Width - 1, (int)Math.Max(shape.Start.X, shape.End.X));
                        var y2 = Math.Min(image.Height - 1, (int)Math.Max(shape.Start.Y, shape.End.Y));
                        DrawEllipseOutline(image, x1, y1, x2, y2, c, t);
                        break;
                    }
                    case AnnotationTool.Line:
                    {
                        DrawLine(image, (int)shape.Start.X, (int)shape.Start.Y,
                            (int)shape.End.X, (int)shape.End.Y, c, t);
                        break;
                    }
                    case AnnotationTool.Arrow:
                    {
                        DrawArrow(image, shape, c, t);
                        break;
                    }
                    case AnnotationTool.Brush:
                    {
                        if (shape.Points != null && shape.Points.Count >= 2)
                        {
                            for (int i = 0; i < shape.Points.Count - 1; i++)
                            {
                                DrawLine(image,
                                    (int)shape.Points[i].X, (int)shape.Points[i].Y,
                                    (int)shape.Points[i + 1].X, (int)shape.Points[i + 1].Y,
                                    c, t);
                            }
                        }
                        break;
                    }
                    case AnnotationTool.Text:
                    {
                        DrawTextAnnotation(image, shape, c);
                        break;
                    }
                }
            }

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
        });
    }

    /// <summary>
    /// 绘制矩形边框
    /// </summary>
    private static void DrawRectOutline(Image<Rgba32> image, int x1, int y1, int x2, int y2, Rgba32 color, int thickness)
    {
        for (int t = 0; t < thickness; t++)
        {
            // 上边
            for (int x = x1; x <= x2; x++) SetPixelSafe(image, x, y1 + t, color);
            // 下边
            for (int x = x1; x <= x2; x++) SetPixelSafe(image, x, y2 - t, color);
            // 左边
            for (int y = y1; y <= y2; y++) SetPixelSafe(image, x1 + t, y, color);
            // 右边
            for (int y = y1; y <= y2; y++) SetPixelSafe(image, x2 - t, y, color);
        }
    }

    /// <summary>
    /// 绘制椭圆边框（中点椭圆算法）
    /// </summary>
    private static void DrawEllipseOutline(Image<Rgba32> image, int x1, int y1, int x2, int y2, Rgba32 color, int thickness)
    {
        int cx = (x1 + x2) / 2, cy = (y1 + y2) / 2;
        int a = Math.Abs(x2 - x1) / 2, b = Math.Abs(y2 - y1) / 2;
        if (a == 0 || b == 0) return;

        for (int t = 0; t < thickness; t++)
        {
            int aa = a + t, bb = b + t;
            if (aa == 0 || bb == 0) continue;
            long aa2 = (long)aa * aa, bb2 = (long)bb * bb;

            int x = 0, y = bb;
            long d1 = bb2 - aa2 * bb + aa2 / 4;

            while (2 * bb2 * x <= 2 * aa2 * y)
            {
                SetPixelSafe(image, cx + x, cy + y, color);
                SetPixelSafe(image, cx - x, cy + y, color);
                SetPixelSafe(image, cx + x, cy - y, color);
                SetPixelSafe(image, cx - x, cy - y, color);
                x++;
                if (d1 < 0) d1 += 2 * bb2 * x + bb2;
                else { y--; d1 += 2 * bb2 * x - 2 * aa2 * y + bb2; }
            }

            long d2 = bb2 * (x * 2 + 1) * (x * 2 + 1) / 4 + aa2 * ((y - 1) * (y - 1)) - aa2 * bb2;
            while (y >= 0)
            {
                SetPixelSafe(image, cx + x, cy + y, color);
                SetPixelSafe(image, cx - x, cy + y, color);
                SetPixelSafe(image, cx + x, cy - y, color);
                SetPixelSafe(image, cx - x, cy - y, color);
                y--;
                if (d2 > 0) d2 -= 2 * aa2 * y + aa2;
                else { x++; d2 += 2 * bb2 * x - 2 * aa2 * y + aa2; }
            }
        }
    }

    /// <summary>
    /// Bresenham 画线：线宽以当前点为中心扩展，避免旧实现只向右/向下加粗导致箭头和线身视觉偏移。
    /// </summary>
    private static void DrawLine(Image<Rgba32> image, int x0, int y0, int x1, int y1, Rgba32 color, int thickness)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            DrawBrushPoint(image, x0, y0, color, thickness);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void DrawBrushPoint(Image<Rgba32> image, int cx, int cy, Rgba32 color, int thickness)
    {
        if (thickness <= 1)
        {
            SetPixelSafe(image, cx, cy, color);
            return;
        }

        var radius = Math.Max(1, thickness / 2);
        var radiusSq = radius * radius;
        for (var ox = -radius; ox <= radius; ox++)
        {
            for (var oy = -radius; oy <= radius; oy++)
            {
                if (ox * ox + oy * oy <= radiusSq)
                    SetPixelSafe(image, cx + ox, cy + oy, color);
            }
        }
    }

    /// <summary>
    /// 绘制箭头：用同一条轴线绘制线身，再从同一个终点绘制两条箭头翼，避免填充三角形产生错位/畸形。
    /// </summary>
    private static void DrawArrow(Image<Rgba32> image, AnnotationShape shape, Rgba32 color, int thickness)
    {
        var x1 = (int)Math.Round(shape.Start.X);
        var y1 = (int)Math.Round(shape.Start.Y);
        var x2 = (int)Math.Round(shape.End.X);
        var y2 = (int)Math.Round(shape.End.Y);

        DrawLine(image, x1, y1, x2, y2, color, thickness);

        var dx = x2 - x1;
        var dy = y2 - y1;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 5) return;

        var angle = Math.Atan2(dy, dx);
        var headLen = Math.Min(22.0, len * 0.28);
        headLen = Math.Max(headLen, thickness * 4.0);
        headLen = Math.Min(headLen, len * 0.45);

        var wingAngle = Math.PI / 7.0;
        var hx1 = (int)Math.Round(x2 - headLen * Math.Cos(angle - wingAngle));
        var hy1 = (int)Math.Round(y2 - headLen * Math.Sin(angle - wingAngle));
        var hx2 = (int)Math.Round(x2 - headLen * Math.Cos(angle + wingAngle));
        var hy2 = (int)Math.Round(y2 - headLen * Math.Sin(angle + wingAngle));

        DrawLine(image, x2, y2, hx1, hy1, color, thickness);
        DrawLine(image, x2, y2, hx2, hy2, color, thickness);
    }

    private static void SetPixelSafe(Image<Rgba32> image, int x, int y, Rgba32 color)
    {
        if (x >= 0 && x < image.Width && y >= 0 && y < image.Height)
        {
            image[x, y] = color;
        }
    }

    private static void DrawTextAnnotation(Image<Rgba32> image, AnnotationShape shape, Rgba32 color)
    {
        if (string.IsNullOrWhiteSpace(shape.Text)) return;

        var font = CreateAnnotationFont((float)Math.Max(1, shape.FontSize));
        var options = new RichTextOptions(font)
        {
            Origin = new PointF((float)shape.Start.X, (float)shape.Start.Y)
        };
        var brush = new SolidBrush(Color.FromPixel(color));

        image.Mutate(ctx => ctx.DrawText(options, shape.Text, brush));
    }

    private static Font CreateAnnotationFont(float fontSize)
    {
        var preferredFonts = new[]
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun",
            "Arial Unicode MS", "Segoe UI", "Arial"
        };

        foreach (var fontName in preferredFonts)
        {
            if (SystemFonts.TryGet(fontName, out var family))
                return family.CreateFont(fontSize, FontStyle.Regular);
        }

        var firstFamily = SystemFonts.Families.FirstOrDefault();
        if (firstFamily.Name != null)
            return firstFamily.CreateFont(fontSize, FontStyle.Regular);

        throw new InvalidOperationException("未找到可用于绘制标注文字的系统字体。");
    }

    /// <summary>
    /// 解析十六进制颜色字符串为 Rgba32
    /// </summary>
    private static Rgba32 ParseColorToRgba(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex[..2], 16);
                var g = Convert.ToByte(hex[2..4], 16);
                var b = Convert.ToByte(hex[4..6], 16);
                return new Rgba32(r, g, b, 255);
            }
            if (hex.Length == 8)
            {
                // WPF/ColorConverter 使用 #AARRGGBB；ImageSharp 的 Rgba32 需要 RGBA。
                // 之前按 #RRGGBBAA 解析会把默认红色 #FFE53935 写成偏黄色。
                var a = Convert.ToByte(hex[..2], 16);
                var r = Convert.ToByte(hex[2..4], 16);
                var g = Convert.ToByte(hex[4..6], 16);
                var b = Convert.ToByte(hex[6..8], 16);
                return new Rgba32(r, g, b, a);
            }
        }
        catch { }
        return new Rgba32(255, 0, 0, 255);
    }

    /// <summary>
    /// 按原文件格式压缩图片（保留格式和透明通道）
    /// </summary>
    public async Task CompressImageAsync(string inputPath, string outputPath, int quality = 80)
    {
        await Task.Run(() =>
        {
            using var image = Image.Load(inputPath);
            var ext = Path.GetExtension(outputPath);
            if (!ImageFormatHelper.IsSaveSupportedExtension(ext))
                throw new NotSupportedException($"不支持压缩 {ext} 格式。请先另存为 JPG、PNG、BMP、GIF、WebP 或 TIFF 后再压缩。");
            var encoder = GetEncoder(ext, quality);
            image.Save(outputPath, encoder);
        });
    }

    /// <summary>
    /// Draw doodle/brush strokes on image
    /// </summary>
    public async Task DrawDoodleAsync(string inputPath, string outputPath,
        List<AnnotationShape> doodles, int imageWidth, int imageHeight, int jpegQuality = 90)
    {
        await Task.Run(() =>
        {
            using var image = Image.Load<Rgba32>(inputPath);

            foreach (var doodle in doodles)
            {
                if (doodle.Points == null || doodle.Points.Count < 2) continue;
                var c = ParseColorToRgba(doodle.Color);
                var t = Math.Max(1, (int)doodle.StrokeThickness);

                for (int i = 0; i < doodle.Points.Count - 1; i++)
                {
                    DrawLine(image,
                        (int)doodle.Points[i].X, (int)doodle.Points[i].Y,
                        (int)doodle.Points[i + 1].X, (int)doodle.Points[i + 1].Y,
                        c, t);
                }
            }

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
        });
    }

    /// <summary>
    /// Apply mosaic (pixelation) effect along a freehand path
    /// </summary>
    public async Task ApplyMosaicBrushAsync(string inputPath, string outputPath,
        List<System.Windows.Point> points, int radius, int blockSize = 10, int jpegQuality = 90)
    {
        await Task.Run(() =>
        {
            using var image = Image.Load<Rgba32>(inputPath);
            int imgW = image.Width;
            int imgH = image.Height;
            if (imgW <= 0 || imgH <= 0 || points.Count == 0 || radius <= 0) return;

            radius = Math.Max(1, radius);
            blockSize = Math.Max(1, blockSize);

            // 只为笔迹外接矩形分配掩码，避免为整张大图创建 bool[width,height]；
            // 同时避免 HashSet<(int,int)> 的大量装箱/哈希开销。
            int minX = imgW - 1, minY = imgH - 1;
            int maxX = 0, maxY = 0;
            foreach (var pt in points)
            {
                var x = (int)Math.Round(pt.X);
                var y = (int)Math.Round(pt.Y);
                minX = Math.Min(minX, Math.Max(0, x - radius));
                minY = Math.Min(minY, Math.Max(0, y - radius));
                maxX = Math.Max(maxX, Math.Min(imgW - 1, x + radius));
                maxY = Math.Max(maxY, Math.Min(imgH - 1, y + radius));
            }

            if (maxX < minX || maxY < minY) return;

            int maskW = maxX - minX + 1;
            int maskH = maxY - minY + 1;
            var mask = new bool[maskW * maskH];
            int r2 = radius * radius;
            int sampleStep = Math.Max(1, radius / 2);

            void StampDisk(int cx, int cy)
            {
                int left = Math.Max(minX, cx - radius);
                int right = Math.Min(maxX, cx + radius);
                int top = Math.Max(minY, cy - radius);
                int bottom = Math.Min(maxY, cy + radius);

                for (int py = top; py <= bottom; py++)
                {
                    int dy = py - cy;
                    int dy2 = dy * dy;
                    int rowOffset = (py - minY) * maskW;
                    for (int px = left; px <= right; px++)
                    {
                        int dx = px - cx;
                        if (dx * dx + dy2 <= r2)
                            mask[rowOffset + (px - minX)] = true;
                    }
                }
            }

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                StampDisk((int)Math.Round(p.X), (int)Math.Round(p.Y));

                // 鼠标事件较稀疏时补采样，避免快速拖动留下断点。
                if (i == points.Count - 1) continue;
                var next = points[i + 1];
                double dx = next.X - p.X;
                double dy = next.Y - p.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                int steps = (int)(distance / sampleStep);
                for (int s = 1; s < steps; s++)
                {
                    double t = (double)s / steps;
                    StampDisk((int)Math.Round(p.X + dx * t), (int)Math.Round(p.Y + dy * t));
                }
            }

            // Apply mosaic block-by-block within the bounding box
            for (int bx = minX; bx <= maxX; bx += blockSize)
            {
                for (int by = minY; by <= maxY; by += blockSize)
                {
                    int bw = Math.Min(blockSize, maxX - bx + 1);
                    int bh = Math.Min(blockSize, maxY - by + 1);

                    float rSum = 0, gSum = 0, bSum = 0;
                    int count = 0;

                    for (int py = by; py < by + bh; py++)
                    {
                        int rowOffset = (py - minY) * maskW;
                        for (int px = bx; px < bx + bw; px++)
                        {
                            if (!mask[rowOffset + (px - minX)]) continue;
                            var pixel = image[px, py];
                            rSum += pixel.R;
                            gSum += pixel.G;
                            bSum += pixel.B;
                            count++;
                        }
                    }

                    if (count == 0) continue;

                    byte avgR = (byte)(rSum / count);
                    byte avgG = (byte)(gSum / count);
                    byte avgB = (byte)(bSum / count);

                    for (int py = by; py < by + bh; py++)
                    {
                        int rowOffset = (py - minY) * maskW;
                        for (int px = bx; px < bx + bw; px++)
                        {
                            if (!mask[rowOffset + (px - minX)]) continue;
                            var old = image[px, py];
                            image[px, py] = new Rgba32(avgR, avgG, avgB, old.A);
                        }
                    }
                }
            }

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
        });
    }


    /// <summary>
    /// Draw text annotations on image (pixel-based, no font dependency)
    /// </summary>
    public async Task DrawTextOnImageAsync(string inputPath, string outputPath,
        List<AnnotationShape> textShapes, int imageWidth, int imageHeight, int jpegQuality = 90)
    {
        await Task.Run(() =>
        {
            using var image = Image.Load<Rgba32>(inputPath);

            foreach (var shape in textShapes)
            {
                if (string.IsNullOrEmpty(shape.Text)) continue;
                var c = ParseColorToRgba(shape.Color);
                DrawTextAnnotation(image, shape, c);
            }

            var encoder = GetEncoder(Path.GetExtension(outputPath), jpegQuality);
            image.Save(outputPath, encoder);
        });
    }
}
