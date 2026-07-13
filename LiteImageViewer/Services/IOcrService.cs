using LiteImageViewer.Models;

namespace LiteImageViewer.Services;

/// <summary>
/// OCR 服务接口（可替换架构）
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// 识别图片中的文字
    /// </summary>
    Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 服务名称
    /// </summary>
    string ServiceName { get; }
}
