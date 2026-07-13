# ppkantu Release / 鹏鹏看图发布

本文档用于说明 `ppkantu` / 鹏鹏看图仓库的公开发布口径，便于后续维护者保持一致。

## 发布目标

- 仓库名：`ppkantu`
- 对外项目名：`鹏鹏看图 / ppkantu`
- 公开仓库许可证：`Apache-2.0`

## 发布产物

### 1. 普通目录版

路径：

```text
artifacts/publish/ppkantu/Release/win-x64/
```

适合完整目录分发。目录内会包含主程序及其所需依赖文件。

### 2. 单文件版

路径：

```text
artifacts/publish/ppkantu/Release/win-x64-single/
```

适合更便于拷贝和分发的场景。单文件版仍然是框架依赖发布，不是自包含版。

## 发布前检查

发布前至少确认：

- `dotnet build ppkantu.sln -c Release` 无错误
- `README.md`、`LICENSE`、`THIRD_PARTY_NOTICES.md` 已存在
- 不把以下内容提交到 GitHub：
  - `bin/`
  - `obj/`
  - `artifacts/`
  - `*_wpftmp/`
  - 临时测试文件、日志、调试输出

## 合规说明

- 第三方 NuGet 依赖的许可见 `THIRD_PARTY_NOTICES.md`
- 如果后续新增图片、字体、音频或代码片段，需先确认来源许可
- 若引入新的第三方组件，应同步更新 `THIRD_PARTY_NOTICES.md`

## 维护建议

如果发布方式变更，请同步更新：

- `PACKAGING.md`
- `README.md`
- `THIRD_PARTY_NOTICES.md`
