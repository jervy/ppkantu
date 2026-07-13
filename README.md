# 鹏鹏看图 / LiteImageViewer

轻量、干净、无广告的 Windows 图片查看与处理工具。

## 项目概览

- 主程序：`LiteImageViewer`
- 辅助浮窗：`FloatingImageViewerApp`
- 技术栈：WPF / .NET 8
- 目标平台：`win-x64`
- 发布方式：框架依赖发布（`--self-contained false`）

## 主要能力

- 图片查看与快速浏览
- 截图、裁切、标注与编辑辅助
- OCR 识别支持
- 文件关联与右键打开支持
- 浮窗辅助展示

## 构建与发布

请优先阅读：`PACKAGING.md`

常用发布目录：

- 普通目录版：`artifacts/publish/LiteImageViewer/Release/win-x64/`
- 单文件版：`artifacts/publish/LiteImageViewer/Release/win-x64-single/`

## 许可与合规

- 本仓库代码以 **Apache-2.0** 许可发布，见 `LICENSE`
- 第三方依赖与归属说明见 `THIRD_PARTY_NOTICES.md`
- 仓库内图标等素材为项目内生成或项目自有内容

## 仓库约定

- 不要提交 `bin/`、`obj/`、`artifacts/` 等构建产物
- 发布前请先检查第三方依赖、素材和引用是否符合对应许可证要求
- 如需重新生成图标，可参考 `LiteImageViewer/Assets/generate_icon.py`

## 目录结构

```text
LiteImageViewer.sln
Directory.Build.props
Directory.Build.targets
LiteImageViewer/
FloatingImageViewerApp/
PACKAGING.md
THIRD_PARTY_NOTICES.md
```
