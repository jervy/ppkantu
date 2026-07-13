using System.IO;
using Ppkantu.Models;

namespace Ppkantu.Services;

/// <summary>
/// 模拟 OCR 服务（用于开发调试）
/// </summary>
public class MockOcrService : IOcrService
{
    public string ServiceName => "Mock OCR (模拟)";

    public async Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        // 模拟识别延迟
        await Task.Delay(1500, cancellationToken);

        var fileName = Path.GetFileName(imagePath);

        // 模拟识别结果
        var result = new OcrResult
        {
            IsSuccess = true,
            Text = "[Mock OCR 识别结果]\n\n" +
                   "这是对图片 \"" + fileName + "\" 的模拟识别结果。\n\n" +
                   "实际使用时，请配置真实的 OCR 服务（如百度OCR、腾讯OCR等）。\n\n" +
                   "识别时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                   "置信度: 98.5%",
            Blocks = new List<OcrTextBlock>
            {
                new() { Text = "这是对图片 \"" + fileName + "\" 的模拟识别结果。", Confidence = 0.985, X = 10, Y = 10, Width = 400, Height = 30 },
                new() { Text = "实际使用时，请配置真实的 OCR 服务。", Confidence = 0.972, X = 10, Y = 50, Width = 380, Height = 30 },
                new() { Text = "识别时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Confidence = 0.99, X = 10, Y = 90, Width = 300, Height = 25 },
                new() { Text = "置信度: 98.5%", Confidence = 0.985, X = 10, Y = 120, Width = 200, Height = 25 }
            }
        };

        return result;
    }
}
