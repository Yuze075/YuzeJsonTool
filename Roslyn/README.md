# Yuze JSON Tool Roslyn Source Generator

生成器读取标记了 `YuzeToolkit.Json.JsonSerializableAttribute` 的 partial context，生成直接访问公开字段和属性的 `JsonConverter<T>`、`JsonTypeInfo<T>` 与 `IJsonTypeResolver`。

## 构建

在 `Game/YuzeJsonTool` 目录执行：

```bash
dotnet build Roslyn/src/YuzeJsonTool.SourceGenerator/YuzeJsonTool.SourceGenerator.csproj -c Release
```

Release 输出位于：

```text
Roslyn/src/YuzeJsonTool.SourceGenerator/bin/Release/netstandard2.0/YuzeJsonTool.SourceGenerator.dll
```

修改生成器后，用该 DLL 更新：

```text
Packages/com.yuzetoolkit.jsontool/Analyzers/YuzeJsonTool.SourceGenerator.dll
```

保留现有 `.meta` 与 `RoslynAnalyzer` label。UPM Package 只提交最终 Analyzer，不提交 Roslyn 的 `bin`、`obj` 或临时消费项目。
