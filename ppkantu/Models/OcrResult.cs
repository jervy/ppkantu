namespace Ppkantu.Models;

/// <summary>
/// OCR 识别结果
/// </summary>
public class OcrResult
{
    /// <summary>
    /// 识别出的全部文本
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 识别出的文本块列表
    /// </summary>
    public List<OcrTextBlock> Blocks { get; set; } = new();

    /// <summary>
    /// 是否识别成功
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// 错误信息（识别失败时）
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// OCR 识别出的文本块
/// </summary>
public class OcrTextBlock
{
    /// <summary>
    /// 文本内容
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 置信度 (0.0 ~ 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 边界框 X 坐标
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// 边界框 Y 坐标
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// 边界框宽度
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// 边界框高度
    /// </summary>
    public double Height { get; set; }
}
