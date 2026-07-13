using System.Windows;

namespace Ppkantu.Models;

/// <summary>
/// 标注工具类型
/// </summary>
public enum AnnotationTool
{
    None,
    Rectangle,
    Ellipse,
    Arrow,
    Line,
    Brush,
    Text
}

/// <summary>
/// 标注形状（坐标为图像像素坐标）
/// </summary>
public class AnnotationShape
{
    public AnnotationTool Tool { get; set; }
    public Point Start { get; set; }
    public Point End { get; set; }
    public string Color { get; set; } = "#FFE53935";
    public double StrokeThickness { get; set; } = 3;
    public string? Text { get; set; }
    public List<Point>? Points { get; set; }
    public double FontSize { get; set; } = 24;
}
