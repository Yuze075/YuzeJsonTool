# Yuze JSON Tool

Yuze JSON Tool 是面向 Unity 与普通 .NET 环境的通用 JSON 库。它提供严格 JSON 读写、可变 DOM、默认反射序列化，以及无需反射的可插拔类型信息路径，并作为 Yuze Package 共用的 JSON 基础依赖。

## 设计目标

- Core 程序集不引用 Unity，核心 API 可移植到支持 .NET Standard 2.1 的环境。
- `JsonReader` 是基于 `ReadOnlySpan<char>` 的前向读取器，容器栈使用池化数组；`JsonWriter` 使用池化字符缓冲区并把数值直接格式化到 Span，不创建中间数字字符串。
- `JsonData` 提供类似 LitJson 的对象、数组、字符串、数字、布尔值和显式 JSON null 操作。
- 默认反射模式支持对象、结构体、数组、集合、字符串键字典、枚举、可空类型和常用基础类型。
- `JsonConverter<T>`、`JsonTypeInfo<T>` 与 `IJsonTypeResolver` 为手写代码、Editor 代码生成器或 Roslyn Source Generator 提供同一条无 DOM 快速路径。
- Unity 程序集为常用数学结构提供无反射 Converter，并在运行时和 Editor 自动注册。

## 基本用法

```csharp
var data = JsonData.Parse("{\"name\":\"Yuze\",\"values\":[1,2,3]}");
data["enabled"] = true;
var text = data.ToJson(new JsonOptions { WriteIndented = true });

var json = YuzeJson.Serialize(model);
var copy = YuzeJson.Deserialize<MyModel>(json);
```

`JsonReader`、`YuzeJson.Parse` 和 `YuzeJson.Deserialize` 同时接受 `string` 与 `ReadOnlySpan<char>`。反射对象成员可使用：

- `[JsonName("jsonName")]`
- `[JsonIgnore]`
- `[JsonRequired]`
- `[JsonConverter(typeof(MyConverter))]`

## Source Generator 与高性能可插拔路径

Package 自带 `YuzeJsonTool.SourceGenerator.dll`。声明一个 partial context，并列出需要生成的 DTO：

```csharp
[JsonSerializable(typeof(PlayerData))]
[JsonSerializable(typeof(PlayerSettings))]
public static partial class GameJsonContext
{
}

var typeInfo = GameJsonContext.GetTypeInfo<PlayerData>();
var json = YuzeJson.Serialize(player, typeInfo);
var copy = YuzeJson.Deserialize(json, typeInfo);
```

生成器直接访问公开字段和公开属性，支持 `JsonName`、`JsonIgnore`、`JsonRequired` 与成员/类型 `JsonConverter`。一维数组、`List<T>`、`Dictionary<string,T>` 和基础数值使用直接生成路径；同一 context 中列出的嵌套 DTO 也直接调用生成 converter，不经过运行时反射。未列入 context 的复杂成员仍按 Resolver 优先级处理。

`GameJsonContext.Resolver` 可以传入 `JsonOptions.Resolver`，`GetTypeInfo<T>()` 用于单次调用。构造器、成员可写性、重复 JSON 名称和 Converter 类型不满足生成条件时，生成器会给出 `YJSG001` 至 `YJSG007` 编译错误，不会静默退回反射。

手写 Converter 使用同一条扩展路径：

实现 `JsonConverter<T>` 后，可以只影响一次调用：

```csharp
var typeInfo = JsonTypeInfo<PlayerData>.From(PlayerDataJsonConverter.Instance, generatedResolver);
var json = YuzeJson.Serialize(player, typeInfo);
var copy = YuzeJson.Deserialize(json, typeInfo);
```

生成或手写的 Converter 直接操作 `JsonReader` / `JsonWriter`，不会构造中间 DOM，也不会对目标 DTO 做反射。Converter 通过 `JsonSerializationContext.ReadValue` 与 `WriteValue` 处理未生成的嵌套类型，因此一组生成类型可以封装在同一个 Resolver 中。

解析器的优先级固定为：

1. 本次调用的 `JsonTypeInfo.Resolver`
2. `JsonOptions.Resolver`
3. `JsonConverterRegistry` 的进程级静态注册
4. 内置类型与缓存后的反射类型信息

Unity 常用类型也可以显式使用 `UnityJsonConverters.Resolver`，或调用 `UnityJsonConverters.RegisterDefaults()` 进行静态注册。当前覆盖 `Vector2/3/4`、`Vector2Int/3Int`、`Quaternion`、`Color/Color32`、`Rect/RectInt` 与 `Bounds/BoundsInt`。

## AOT 与 IL2CPP

反射模式是默认便利路径，不承诺对任意用户类型提供 AOT 保留能力。IL2CPP 生产路径应使用手写或生成的 Converter/TypeInfo，并把完整 DTO 图中的对象类型列入同一个生成 context。生成代码、一维数组、`List<T>`、字符串键 `Dictionary` 与已列出的嵌套 DTO 不依赖运行时反射；未列出的复杂类型仍需要自定义 Converter 或可用 Resolver。Core 与 Unity Converter 本身不依赖运行时代码生成。

## 行为与限制

- 默认遵循严格 JSON；注释和尾逗号必须通过 `JsonOptions` 显式开启。
- JSON 对象重复字段默认后值覆盖，也可以配置为报错。
- 字典键目前必须是字符串。
- 反射反序列化引用类型需要 public 无参构造；特殊构造、不规则集合和多态模型使用自定义 Converter。
- `NaN`、正负无穷和未配对 UTF-16 surrogate 会被拒绝。
- 0.1 版是内存文本 API；网络流可在边界完成解码后把 Span/String 交给 Core。原生增量 UTF-8/Stream 管线不在本版范围内。

## 测试

- Core 单元测试覆盖 tokenizer、writer、DOM、反射、Converter/TypeInfo 优先级和 Unity 类型。
- `Tests/External/JSONTestSuite` 固定收录 `nst/JSONTestSuite` 语料：`y_` 必须接受、`n_` 必须拒绝、`i_` 保留为实现定义样例。
- Performance Test Framework 基准分别测量反射与生成式 Converter 的序列化、反序列化路径；基准记录趋势，不绑定脆弱的绝对耗时阈值。

## 程序集

- `YuzeJsonTool.Core`：无 Unity 引用的 reader、writer、DOM 与序列化框架。
- `YuzeJsonTool.Unity`：Unity 值类型 Converter 与默认注册。
- `YuzeJsonTool.Editor`：Editor 域注册入口。
- `YuzeJsonTool.SourceGenerator.dll`：随 Package 导入的 Roslyn Analyzer；源码位于 `Game/YuzeJsonTool/Roslyn`。
