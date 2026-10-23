# Yuze JSON Tool

Yuze JSON Tool 是由 Yuze 维护、面向 Unity 与普通 .NET 的通用 JSON 库。仓库同时保存可发布的 UPM Package 与 Roslyn Source Generator 源码。

## 目录

```text
Packages/com.yuzetoolkit.jsontool/  Unity Package 与已构建 Analyzer
Roslyn/src/                        Source Generator 源码
```

运行时 API、Source Generator 用法、AOT 边界与测试策略见 [Package README](Packages/com.yuzetoolkit.jsontool/README.md)。生成器构建与部署见 [Roslyn README](Roslyn/README.md)。

当前 RelicLight 通过 `Packages/manifest.json` 的本地 file dependency 引用该 Package。Yuze Eval Tool、Yuze Dialogue System、Yuze UI Framework 与项目 Editor JSON 工具均直接依赖该 Package，旧 `YuzeToolkit.LitJson` 实现已移除。
