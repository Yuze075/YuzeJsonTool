# Changelog

## [Unreleased]

- Made `JsonData.Add` return its current array or object node so DOM construction supports fluent chains without a consumer-owned compatibility extension.
- Replaced all remaining project-owned LitJson consumers and removed `YuzeUtilityTool.LitJson`.

## [0.1.0] - 2026-08-27

- Added a strict pooled JSON reader and writer over UTF-16 text.
- Added the mutable `JsonData` DOM and exact JSON number representation.
- Added reflection serialization, built-in primitive/collection converters and configurable policies.
- Added per-call TypeInfo/resolver injection, options-level resolvers and static converter registration.
- Added the Roslyn source generator for direct DTO converters, resolvers and per-call type info.
- Added generated direct paths for arrays, lists, string-key dictionaries and explicitly listed nested DTOs.
- Removed numeric writer strings, per-item collection reflection and eager DOM number parsing from hot paths.
- Preserved raw DOM number text while parsing numeric values lazily on access.
- Added non-reflection converters for common Unity value types.
- Added JSONTestSuite compliance, functional, equivalence and performance tests.
- Kept the existing `YuzeToolkit.LitJson` implementation and all current consumers unchanged.
