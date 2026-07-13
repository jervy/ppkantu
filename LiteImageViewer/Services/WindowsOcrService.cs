using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using LiteImageViewer.Models;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace LiteImageViewer.Services;

/// <summary>
/// Windows 内置 OCR 引擎（免费、离线、无需 API Key）
/// 使用 Windows 10/11 自带的 Windows.Media.Ocr API
/// </summary>
public class WindowsOcrService : IOcrService
{
    public string ServiceName => "Windows 内置 OCR";

    public async Task<Models.OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            // 读取图片文件
            var fileBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);

            using var stream = new MemoryStream(fileBytes);
            var randomAccessStream = stream.AsRandomAccessStream();

            // 解码图片
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            if (softwareBitmap == null)
            {
                return new Models.OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "无法解码图片格式。"
                };
            }

            // 使用默认语言（中文 + 英文）
            var language = new Windows.Globalization.Language("zh-Hans");
            var ocrEngine = OcrEngine.TryCreateFromLanguage(language);
            if (ocrEngine == null)
            {
                // 如果没有中文语言包，尝试英文
                language = new Windows.Globalization.Language("en-US");
                ocrEngine = OcrEngine.TryCreateFromLanguage(language);
            }

            if (ocrEngine == null)
            {
                return new Models.OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "未找到可用的 OCR 语言包。请在 Windows 设置中安装中文或英文语言包。"
                };
            }

            // 执行 OCR
            var ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);

            if (string.IsNullOrWhiteSpace(ocrResult.Text))
            {
                return new Models.OcrResult
                {
                    IsSuccess = true,
                    Text = "（未识别到文字）",
                    Blocks = new List<OcrTextBlock>()
                };
            }

            // 组装结果
            var textBuilder = new StringBuilder();
            var blocks = new List<OcrTextBlock>();

            foreach (var line in ocrResult.Lines)
            {
                textBuilder.AppendLine(line.Text);

                foreach (var word in line.Words)
                {
                    var rect = word.BoundingRect;
                    blocks.Add(new OcrTextBlock
                    {
                        Text = word.Text,
                        Confidence = 1.0,
                        X = rect.X,
                        Y = rect.Y,
                        Width = rect.Width,
                        Height = rect.Height
                    });
                }
            }

            return new Models.OcrResult
            {
                IsSuccess = true,
                Text = textBuilder.ToString().TrimEnd(),
                Blocks = blocks
            };
        }
        catch (OperationCanceledException)
        {
            return new Models.OcrResult
            {
                IsSuccess = false,
                ErrorMessage = "OCR 识别已取消。"
            };
        }
        catch (Exception ex)
        {
            return new Models.OcrResult
            {
                IsSuccess = false,
                ErrorMessage = $"OCR 识别失败: {ex.Message}"
            };
        }
    }
}
