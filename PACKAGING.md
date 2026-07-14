# ppkantu 发布打包规则

本文档用于固定 Kantu / 鹏鹏看图项目的发布方式，防止不同 AI 或开发者生成不一致的目录结构、单文件版位置或多余文件。

## 1. 项目信息

- 解决方案：`ppkantu.sln`
- 主项目：`ppkantu/ppkantu.csproj`
- 目标框架：`net8.0-windows10.0.19041.0`
- 目标运行时：`win-x64`
- 发布配置：`Release`
- 发布方式：框架依赖发布，即 `--self-contained false`

注意：单文件版不是自包含版。它仍依赖目标机器安装 .NET Windows Desktop Runtime。

## 2. 固定输出目录

所有构建和发布产物必须使用项目根目录下的 `artifacts/`，不要输出到项目内的 `bin/`、`obj/`、`publish/`、`release/` 或临时自定义目录。

当前目录规则由根目录 `Directory.Build.props` 控制：

```text
artifacts/
  bin/ppkantu/<Debug|Release>/...
  obj/ppkantu/<Debug|Release>/...
  publish/win-x64/
  publish/win-x64-single/
```

## 3. 普通目录版生成规则

普通目录版使用以下命令生成：

```bash
dotnet publish ppkantu/ppkantu.csproj -c Release -r win-x64 --self-contained false -v minimal
```

输出目录必须是：

```text
artifacts/publish/win-x64/
```

Windows 路径为：

```text
D:\work\ppkantu\artifacts\publish\win-x64\
```

该目录中应包含：

```text
ppkantu.exe
ppkantu.dll
ppkantu.deps.json
ppkantu.runtimeconfig.json
相关依赖 dll
```

## 4. 单文件版生成规则

单文件版必须使用以下命令生成：

```bash
dotnet publish ppkantu/ppkantu.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -v minimal
```

输出目录必须是：

```text
artifacts/publish/win-x64-single/
```

Windows 路径为：

```text
D:\work\ppkantu\artifacts\publish\win-x64-single\
```

单文件版主程序必须是：

```text
artifacts/publish/win-x64-single/ppkantu.exe
```

Windows 路径为：

```text
D:\work\ppkantu\artifacts\publish\win-x64-single\ppkantu.exe
```

## 5. 运行时安装包规则

当前单文件版为框架依赖单文件版，不是自包含版，因此目标机器需要 .NET Windows Desktop Runtime。

如果单文件版目录中需要携带运行时安装包，文件应放在：

```text
artifacts/publish/win-x64-single/
```

当前推荐文件名：

```text
windowsdesktop-runtime-8.0.28-win-x64.exe
```

也就是说，单文件版目录通常至少包含：

```text
ppkantu.exe
windowsdesktop-runtime-8.0.28-win-x64.exe
```

注意：不要把运行时安装包打进 `ppkantu.exe`。它只是放在同一目录，方便分发。

## 6. 禁止事项

其他 AI 或开发者生成发布包时，不要使用以下做法：

1. 不要手动指定 `-p:PublishDir=...` 到其他目录。
2. 不要输出到源码目录下的 `release/`、`publish/`、`bin/Release/publish/` 等非标准目录。
3. 不要生成自包含单文件版，除非用户明确要求。也就是不要使用：

```bash
--self-contained true
```

4. 不要把普通目录版和单文件版混在同一个目录。
5. 不要把 `.pdb` 调试文件放进 Release 发布包。
6. 不要把旧的 `associate.cmd`、`unassociate.cmd` 等脚本放进发布包，文件关联功能应以内置界面为准。
7. 不要留下 `*_wpftmp`、`.tmp` 等临时构建产物。
8. 不要为了“修复” `MSB3539` 警告，把 `BaseIntermediateOutputPath` 简单移动到根目录 `Directory.Build.props`。

## 7. WPF 中间产物与 MSB3539 规则

本项目是 WPF 多项目解决方案，主程序还会在构建时发布并嵌入 `FloatingImageViewerApp.exe`。
这里的中间产物路径规则是稳定构建的一部分，不要随意改。

### 7.1 当前正确规则

两个 WPF 项目的 `.csproj` 中必须保留按项目隔离的中间产物路径：

```xml
<BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)..\artifacts\obj\$(MSBuildProjectName)\</BaseIntermediateOutputPath>
```

并保留：

```xml
<NoWarn>$(NoWarn);MSB3539</NoWarn>
```

也就是说，下面两个文件都应包含上述配置：

```text
ppkantu/ppkantu.csproj
FloatingImageViewerApp/FloatingImageViewerApp.csproj
```

### 7.2 为什么不能移到 Directory.Build.props

理论上 `BaseIntermediateOutputPath` 越早设置越好，因此有些 Agent 可能会尝试把它移动到根目录
`Directory.Build.props`。**不要这样做。**

已经验证过：将 `BaseIntermediateOutputPath` 提前到 `Directory.Build.props` 会导致 WPF 生成的临时项目
`*_wpftmp.csproj` 找不到自己的 NuGet assets 文件，典型错误如下：

```text
NETSDK1004: 找不到资产文件 "...\artifacts\obj\ppkantu_xxxxx_wpftmp\project.assets.json"。
NETSDK1005: 资产文件没有 net8.0-windows / net8.0-windows10.0.19041.0 的目标。
```

因此当前采用的是：

1. `.csproj` 内按项目设置 `BaseIntermediateOutputPath`，保证 WPF `*_wpftmp` 构建稳定。
2. 使用 `NoWarn` 压制已知无害的 `MSB3539`。
3. 发布前通过真实 `dotnet build/publish` 验证，而不是机械移动属性。

### 7.3 如果再次出现警告或 NETSDK1004/1005

如果构建日志重新出现 `MSB3539`，先检查两个 `.csproj` 是否仍保留：

```xml
<NoWarn>$(NoWarn);MSB3539</NoWarn>
```

如果出现 `NETSDK1004` / `NETSDK1005`，优先检查是否有人：

- 删除了 `.csproj` 内的 `BaseIntermediateOutputPath`；
- 把 `BaseIntermediateOutputPath` 移到了 `Directory.Build.props`；
- 让多个项目共享同一个 `artifacts/obj/project.assets.json`。

修复后应清理中间产物再验证：

```bash
cd /d/work/ppkantu
rm -rf artifacts/obj
dotnet restore ppkantu.sln -v minimal
dotnet build ppkantu.sln -c Release -v minimal --no-restore
```

## 8. 推荐完整发布流程

在项目根目录执行：

```bash
cd /d/work/ppkantu

# 构建验证
dotnet build ppkantu.sln -c Release -v minimal

# 主应用：普通目录版（浮窗子应用同步发布到 FloatingImageViewerPublish/）
dotnet publish ppkantu/ppkantu.csproj -c Release -r win-x64 --self-contained false -v minimal
dotnet publish FloatingImageViewerApp/FloatingImageViewerApp.csproj -c Release -r win-x64 --self-contained false -p:PublishDir="D:\work\ppkantu\artifacts\publish\win-x64\FloatingImageViewerPublish" -v minimal

# 主应用：单文件版（浮窗子应用已嵌入主exe，无需单独发布子目录）
dotnet publish ppkantu/ppkantu.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -v minimal
```

> **说明：单文件版与普通目录版的差异**
>
> - **普通目录版**：`win-x64/` 下含 `FloatingImageViewerPublish/` 子目录，"固定到桌面"直接使用该目录下的 exe。
> - **单文件版**：`win-x64-single/` 下只有 `ppkantu.exe` + 运行时安装包，无需附带 `FloatingImageViewerPublish/` 子目录。浮窗 exe 已嵌入主 exe，首次使用"固定到桌面"时自动解压到 `%TEMP%/FloatingImageViewerPublish/`。

发布前的 `dotnet build` 期望结果应为：

```text
0 个警告
0 个错误
```

如果不是 0 警告 0 错误，先按本文档第 7 节排查中间产物路径、`MSB3539`、`NETSDK1004/1005`，不要直接忽略。

## 9. 发布后检查

执行：

```bash
cd /d/work/ppkantu

find artifacts/publish -maxdepth 3 \( -iname '*.pdb' -o -iname '*.cmd' -o -iname '*tmp*' -o -iname '*wpftmp*' \) -print

find artifacts/publish/win-x64-single -maxdepth 1 -type f -printf '%f\n' | sort
```

期望结果：

1. 第一条检查命令无输出，表示没有 PDB、CMD、临时文件残留。
2. 第二条检查命令至少包含：

```text
ppkantu.exe
windowsdesktop-runtime-8.0.28-win-x64.exe
```

如果没有携带运行时安装包，则至少必须包含：

```text
ppkantu.exe
```

## 10. 启动验证

发布后可用以下命令做基础启动验证：

```bash
timeout 5s /d/work/ppkantu/artifacts/publish/win-x64-single/ppkantu.exe; printf 'single_exit=%s\n' $?
```

如果输出为：

```text
single_exit=124
```

表示程序启动后保持运行，5 秒后被 timeout 结束，属于基础启动验证通过。

## 11. 最终交付口径

每次修复后交付时，应说明两个版本均已更新：

普通目录版：

```text
D:\work\ppkantu\artifacts\publish\win-x64\
```

单文件版：

```text
D:\work\ppkantu\artifacts\publish\win-x64-single\ppkantu.exe
```

## 12. 版本号更新方法

当需要发布新版本时，请按以下顺序更新版本号：

1. 修改主项目 `ppkantu/ppkantu.csproj` 中的 `<Version>`，例如从 `1.0.0` 改为 `1.0.1`。
2. 如果你**确实需要**让界面标题同步显示版本号，再一并修改：
   - `ppkantu/MainWindow.xaml` 中的窗口 `Title`
   - `ppkantu/ViewModels/MainViewModel.cs` 中默认的 `_title`
3. 如 README 或发布说明中写死了版本号，也同步改成新版本。
4. 重新执行构建与发布验证：
   - `dotnet build ppkantu.sln -c Release --no-restore`
   - `dotnet publish ppkantu/ppkantu.csproj -c Release -r win-x64 --self-contained false --no-restore`
   - `dotnet publish ppkantu/ppkantu.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true --no-restore`
5. 检查 `artifacts/publish/win-x64/` 和 `artifacts/publish/win-x64-single/` 的实际文件是否符合预期。
6. 提交并推送到 `main`，再按需要创建 GitHub Release。
