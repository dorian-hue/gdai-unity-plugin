# GDAI Unity Plugin Setup (alpha.7.1)

## 1) 下载插件包

- 文件名: `gdai-unity-0.1.0-alpha.7.1.unitypackage`
- 下载地址: **TODO(步骤 3.4 填入 GCS URL)**

## 2) 安装前置依赖 (先装再 Import)

在 Unity `Window -> Package Manager` 中安装以下依赖:

1. `com.unity.nuget.newtonsoft-json` (建议 `3.2.2`)
2. `com.unity.ugui` (建议 `2.0.0`)
3. TextMeshPro (`TMPro`)

说明:

- 在 Unity 6.4 (`6000.4.1f1`) 环境下，`TMPro` API 由 `com.unity.ugui` 提供。
- 在其他 Unity 版本中，如果没有 `TMPro`，请额外安装 TextMeshPro 对应包后再继续。

## 3) 导入插件

1. 打开目标 Unity 项目
2. 菜单: `Assets -> Import Package -> Custom Package...`
3. 选择 `gdai-unity-0.1.0-alpha.7.1.unitypackage`
4. 点击 `Import` 导入全部资源
5. 等待 Unity 编译完成，确认 Console 无错误

## 4) 首次配置 (Sync Panel)

1. 打开 GDAI Sync Panel (插件菜单入口)
2. 点击 `Login` 完成账号登录
3. 选择目标 `project`
4. 点击 `Pull` 拉取当前项目数据

## 5) 常见问题

- `Newtonsoft` namespace not found:
  - 未安装 `com.unity.nuget.newtonsoft-json`，先安装后重新编译。
- `TMPro` or `UnityEngine.UI` not found:
  - 未安装 `com.unity.ugui` 或当前工程未提供 TMPro，先补齐依赖再导入插件。
- Import 后出现其它编译错误:
  - 先确认项目使用 Unity `6000.4.1f1` 或兼容版本，再反馈完整 Console 报错。
