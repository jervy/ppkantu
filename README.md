# 鹏鹏看图（ppkantu）

![鹏鹏看图](assets/readme-cover.svg)

[![CI](https://github.com/jervy/ppkantu/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/jervy/ppkantu/actions/workflows/ci.yml)

轻量、干净、无广告的 Windows 图片查看与处理工具。

## 项目概览

- 主程序：`鹏鹏看图.exe`
- 辅助浮窗：`FloatingImageViewerApp`（内置组件）
- 技术栈：WPF / .NET 8
- 目标平台：`win-x64`
- 发布方式：框架依赖发布，另提供单文件版

## 主要能力

- 图片查看与快速浏览
- 截图、裁切、标注与编辑辅助
- Windows OCR 与可选 API OCR
- 文件关联与右键打开支持
- 浮窗辅助展示
- 明暗主题与便携式配置

## 构建与发布

请优先阅读 [`PACKAGING.md`](PACKAGING.md)。常用命令：

```bash
dotnet build ppkantu.sln -c Release
dotnet publish ppkantu/ppkantu.csproj -c Release -r win-x64 --self-contained false
dotnet publish ppkantu/ppkantu.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

发布输出位于：

- 普通目录版：`artifacts/publish/ppkantu/Release/win-x64/`
- 单文件版：`artifacts/publish/ppkantu/Release/win-x64-single/`

- 发布资产：
  - `ppkantu-win-x64.zip`
  - `ppkantu.exe`（程序内部显示为“鹏鹏看图 V1.0”）

两种版本均为框架依赖版，目标机器需要 .NET 8 Windows Desktop Runtime。

## 配置与 OCR

默认配置文件位于 `%LOCALAPPDATA%\\ppkantu\\appsettings.json`。便携版可将配置放在程序目录或 `Data` 子目录。

- 默认 OCR 提供者：`Mock`（安全、无需网络）
- 设置 `OcrProvider` 为空可启用 Windows OCR
- API OCR 可通过配置文件或 `OCR_API_KEY`、`OCR_API_ENDPOINT` 环境变量配置

## 许可与合规

- 本仓库代码以 **Apache-2.0** 许可发布，见 [`LICENSE`](LICENSE)
- 第三方依赖与归属说明见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)
- 发布规范见 [`RELEASE.md`](RELEASE.md)

## 目录结构

```text
ppkantu.sln
Directory.Build.props
Directory.Build.targets
ppkantu/
FloatingImageViewerApp/
assets/
.github/workflows/
PACKAGING.md
README.md
```

## 项目定位

鹏鹏看图专注于“打开图片、看清图片、处理图片”这条主流程：启动快、界面干净、无广告，不把不相关的功能塞进主界面。
