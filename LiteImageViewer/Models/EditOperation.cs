namespace LiteImageViewer.Models;

/// <summary>
/// 编辑操作类型枚举
/// </summary>
public enum EditOperationType
{
    /// <summary>
    /// 无操作
    /// </summary>
    None,

    /// <summary>
    /// 左旋转 90°
    /// </summary>
    RotateLeft,

    /// <summary>
    /// 右旋转 90°
    /// </summary>
    RotateRight,

    /// <summary>
    /// 水平翻转
    /// </summary>
    FlipHorizontal,

    /// <summary>
    /// 垂直翻转
    /// </summary>
    FlipVertical,

    /// <summary>
    /// 裁剪
    /// </summary>
    Crop,

    /// <summary>
    /// 调整尺寸
    /// </summary>
    Resize,

    /// <summary>
    /// 添加文字
    /// </summary>
    AddText,

    /// <summary>
    /// 添加马赛克
    /// </summary>
    Mosaic,

    /// <summary>
    /// 涂鸦画笔
    /// </summary>
    Draw,

    /// <summary>
    /// 添加箭头
    /// </summary>
    Arrow,

    /// <summary>
    /// 添加矩形框
    /// </summary>
    Rectangle,

    /// <summary>
    /// 图片压缩
    /// </summary>
    Compress
}

/// <summary>
/// 编辑操作记录（用于撤销/重做）
/// </summary>
public class EditOperation
{
    public EditOperationType Type { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 操作参数（如裁剪区域、文字内容等）
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}
