#nullable enable
using System;
using UnityEngine;

namespace YuzeToolkit.Json.Unity
{
    public static class UnityJsonConverters
    {
        public static UnityJsonTypeResolver Resolver => UnityJsonTypeResolver.Instance;

        public static void RegisterDefaults()
        {
            JsonConverterRegistry.Register(Vector2JsonConverter.Instance);
            JsonConverterRegistry.Register(Vector3JsonConverter.Instance);
            JsonConverterRegistry.Register(Vector4JsonConverter.Instance);
            JsonConverterRegistry.Register(Vector2IntJsonConverter.Instance);
            JsonConverterRegistry.Register(Vector3IntJsonConverter.Instance);
            JsonConverterRegistry.Register(QuaternionJsonConverter.Instance);
            JsonConverterRegistry.Register(ColorJsonConverter.Instance);
            JsonConverterRegistry.Register(Color32JsonConverter.Instance);
            JsonConverterRegistry.Register(RectJsonConverter.Instance);
            JsonConverterRegistry.Register(RectIntJsonConverter.Instance);
            JsonConverterRegistry.Register(BoundsJsonConverter.Instance);
            JsonConverterRegistry.Register(BoundsIntJsonConverter.Instance);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterRuntimeDefaults() => RegisterDefaults();
    }

    public sealed class UnityJsonTypeResolver : IJsonTypeResolver
    {
        public static UnityJsonTypeResolver Instance { get; } = new();
        private readonly JsonTypeInfoResolver _resolver = new JsonTypeInfoResolver()
            .Add(Vector2JsonConverter.Instance)
            .Add(Vector3JsonConverter.Instance)
            .Add(Vector4JsonConverter.Instance)
            .Add(Vector2IntJsonConverter.Instance)
            .Add(Vector3IntJsonConverter.Instance)
            .Add(QuaternionJsonConverter.Instance)
            .Add(ColorJsonConverter.Instance)
            .Add(Color32JsonConverter.Instance)
            .Add(RectJsonConverter.Instance)
            .Add(RectIntJsonConverter.Instance)
            .Add(BoundsJsonConverter.Instance)
            .Add(BoundsIntJsonConverter.Instance);

        public bool TryGetTypeInfo(Type type, out JsonTypeInfo typeInfo) => _resolver.TryGetTypeInfo(type, out typeInfo);
    }

    internal static class UnityJsonObjectReader
    {
        internal struct FloatValues
        {
            public float X;
            public float Y;
            public float Z;
            public float W;

            public float this[int index]
            {
                get => index switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
                set
                {
                    switch (index)
                    {
                        case 0: X = value; break;
                        case 1: Y = value; break;
                        case 2: Z = value; break;
                        case 3: W = value; break;
                        default: throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }
            }
        }

        internal struct IntValues
        {
            public int X;
            public int Y;
            public int Z;
            public int W;

            public int this[int index]
            {
                get => index switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
                set
                {
                    switch (index)
                    {
                        case 0: X = value; break;
                        case 1: Y = value; break;
                        case 2: Z = value; break;
                        case 3: W = value; break;
                        default: throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }
            }
        }

        public static FloatValues ReadFloats(ref JsonReader reader, JsonSerializationContext context,
            string[] names, FloatValues values = default)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a JSON object.");
            Span<bool> assigned = stackalloc bool[names.Length];
            assigned.Clear();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var index = Find(names, ref reader, context.Options.PropertyNameCaseInsensitive);
                string? unknownName = null;
                if (index < 0 && context.Options.UnknownMemberHandling == JsonUnknownMemberHandling.Error)
                    unknownName = reader.GetString();
                if (!reader.Read()) throw new JsonException("Unity value property is missing its value.");
                if (index < 0)
                {
                    if (context.Options.UnknownMemberHandling == JsonUnknownMemberHandling.Error)
                        throw new JsonException($"Unknown Unity value member '{unknownName}'.");
                    reader.SkipCurrentValue();
                    continue;
                }
                if (assigned[index] && context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error)
                    throw new JsonException($"Duplicate Unity value member '{names[index]}'.");
                var number = reader.GetDouble();
                if (number < -float.MaxValue || number > float.MaxValue)
                    throw new JsonException($"Unity value member '{names[index]}' is outside the range of Single.");
                values[index] = (float)number;
                assigned[index] = true;
            }
            return values;
        }

        public static IntValues ReadInts(ref JsonReader reader, JsonSerializationContext context,
            string[] names, IntValues values = default)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a JSON object.");
            Span<bool> assigned = stackalloc bool[names.Length];
            assigned.Clear();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var index = Find(names, ref reader, context.Options.PropertyNameCaseInsensitive);
                string? unknownName = null;
                if (index < 0 && context.Options.UnknownMemberHandling == JsonUnknownMemberHandling.Error)
                    unknownName = reader.GetString();
                if (!reader.Read()) throw new JsonException("Unity value property is missing its value.");
                if (index < 0)
                {
                    if (context.Options.UnknownMemberHandling == JsonUnknownMemberHandling.Error)
                        throw new JsonException($"Unknown Unity value member '{unknownName}'.");
                    reader.SkipCurrentValue();
                    continue;
                }
                if (assigned[index] && context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error)
                    throw new JsonException($"Duplicate Unity value member '{names[index]}'.");
                values[index] = checked((int)reader.GetInt64());
                assigned[index] = true;
            }
            return values;
        }

        public static bool NameEquals(string expected, string actual, JsonSerializationContext context) =>
            string.Equals(expected, actual, context.Options.PropertyNameCaseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

        private static int Find(string[] names, ref JsonReader reader, bool ignoreCase)
        {
            if (!ignoreCase)
            {
                for (var index = 0; index < names.Length; index++)
                    if (reader.ValueTextEquals(names[index])) return index;
                return -1;
            }
            var value = reader.GetString();
            for (var index = 0; index < names.Length; index++)
                if (string.Equals(names[index], value, StringComparison.OrdinalIgnoreCase)) return index;
            return -1;
        }
    }

    internal static class UnityJsonObjectWriter
    {
        public static void WriteFloats(JsonWriter writer, string[] names, ReadOnlySpan<float> values)
        {
            writer.WriteStartObject();
            for (var index = 0; index < names.Length; index++)
            {
                writer.WritePropertyName(names[index]);
                writer.WriteNumberValue(values[index]);
            }
            writer.WriteEndObject();
        }

        public static void WriteInts(JsonWriter writer, string[] names, ReadOnlySpan<int> values)
        {
            writer.WriteStartObject();
            for (var index = 0; index < names.Length; index++)
            {
                writer.WritePropertyName(names[index]);
                writer.WriteNumberValue(values[index]);
            }
            writer.WriteEndObject();
        }
    }

    public sealed class Vector2JsonConverter : JsonConverter<Vector2>
    {
        internal static readonly string[] Names = { "x", "y" };
        public static Vector2JsonConverter Instance { get; } = new();
        public override Vector2 Read(ref JsonReader reader, JsonSerializationContext context) { var v = UnityJsonObjectReader.ReadFloats(ref reader, context, Names); return new Vector2(v.X, v.Y); }
        public override void Write(JsonWriter writer, Vector2 value, JsonSerializationContext context) { Span<float> v = stackalloc float[2] { value.x, value.y }; UnityJsonObjectWriter.WriteFloats(writer, Names, v); }
    }
    public sealed class Vector3JsonConverter : JsonConverter<Vector3>
    {
        internal static readonly string[] Names = { "x", "y", "z" };
        public static Vector3JsonConverter Instance { get; } = new();
        public override Vector3 Read(ref JsonReader reader, JsonSerializationContext context) { var v = UnityJsonObjectReader.ReadFloats(ref reader, context, Names); return new Vector3(v.X, v.Y, v.Z); }
        public override void Write(JsonWriter writer, Vector3 value, JsonSerializationContext context) { Span<float> v = stackalloc float[3] { value.x, value.y, value.z }; UnityJsonObjectWriter.WriteFloats(writer, Names, v); }
    }
    public sealed class Vector4JsonConverter : JsonConverter<Vector4>
    {
        internal static readonly string[] Names = { "x", "y", "z", "w" };
        public static Vector4JsonConverter Instance { get; } = new();
        public override Vector4 Read(ref JsonReader reader, JsonSerializationContext context) { var v = UnityJsonObjectReader.ReadFloats(ref reader, context, Names); return new Vector4(v.X, v.Y, v.Z, v.W); }
        public override void Write(JsonWriter writer, Vector4 value, JsonSerializationContext context) { Span<float> v = stackalloc float[4] { value.x, value.y, value.z, value.w }; UnityJsonObjectWriter.WriteFloats(writer, Names, v); }
    }
    public sealed class QuaternionJsonConverter : JsonConverter<Quaternion>
    {
        public static QuaternionJsonConverter Instance { get; } = new();
        public override Quaternion Read(ref JsonReader reader, JsonSerializationContext context) { var v = UnityJsonObjectReader.ReadFloats(ref reader, context, Vector4JsonConverter.Names); return new Quaternion(v.X, v.Y, v.Z, v.W); }
        public override void Write(JsonWriter writer, Quaternion value, JsonSerializationContext context) { Span<float> v = stackalloc float[4] { value.x, value.y, value.z, value.w }; UnityJsonObjectWriter.WriteFloats(writer, Vector4JsonConverter.Names, v); }
    }
    public sealed class Vector2IntJsonConverter : JsonConverter<Vector2Int>
    {
        public static Vector2IntJsonConverter Instance { get; } = new();
        public override Vector2Int Read(ref JsonReader reader, JsonSerializationContext context) { var v = UnityJsonObjectReader.ReadInts(ref reader, context, Vector2JsonConverter.Names); return new Vector2Int(v.X, v.Y); }
        public override void Write(JsonWriter writer, Vector2Int value, JsonSerializationContext context) { Span<int> v = stackalloc int[2] { value.x, value.y }; UnityJsonObjectWriter.WriteInts(writer, Vector2JsonConverter.Names, v); }
    }
    public sealed class Vector3IntJsonConverter : JsonConverter<Vector3Int>
    {
        public static Vector3IntJsonConverter Instance { get; } = new();
        public override Vector3Int Read(ref JsonReader reader, JsonSerializationContext context) { var v = UnityJsonObjectReader.ReadInts(ref reader, context, Vector3JsonConverter.Names); return new Vector3Int(v.X, v.Y, v.Z); }
        public override void Write(JsonWriter writer, Vector3Int value, JsonSerializationContext context) { Span<int> v = stackalloc int[3] { value.x, value.y, value.z }; UnityJsonObjectWriter.WriteInts(writer, Vector3JsonConverter.Names, v); }
    }
    public sealed class ColorJsonConverter : JsonConverter<Color>
    {
        private static readonly string[] Names = { "r", "g", "b", "a" };
        public static ColorJsonConverter Instance { get; } = new();
        public override Color Read(ref JsonReader reader, JsonSerializationContext context) { var defaults = new UnityJsonObjectReader.FloatValues { W = 1f }; var v = UnityJsonObjectReader.ReadFloats(ref reader, context, Names, defaults); return new Color(v.X, v.Y, v.Z, v.W); }
        public override void Write(JsonWriter writer, Color value, JsonSerializationContext context) { Span<float> v = stackalloc float[4] { value.r, value.g, value.b, value.a }; UnityJsonObjectWriter.WriteFloats(writer, Names, v); }
    }
    public sealed class Color32JsonConverter : JsonConverter<Color32>
    {
        private static readonly string[] Names = { "r", "g", "b", "a" };
        public static Color32JsonConverter Instance { get; } = new();
        public override Color32 Read(ref JsonReader reader, JsonSerializationContext context) { var defaults = new UnityJsonObjectReader.IntValues { W = 255 }; var v = UnityJsonObjectReader.ReadInts(ref reader, context, Names, defaults); return new Color32(checked((byte)v.X), checked((byte)v.Y), checked((byte)v.Z), checked((byte)v.W)); }
        public override void Write(JsonWriter writer, Color32 value, JsonSerializationContext context) { Span<int> v = stackalloc int[4] { value.r, value.g, value.b, value.a }; UnityJsonObjectWriter.WriteInts(writer, Names, v); }
    }
    public sealed class RectJsonConverter : JsonConverter<Rect>
    {
        private static readonly string[] Names = { "x", "y", "width", "height" };
        public static RectJsonConverter Instance { get; } = new();
        public override Rect Read(ref JsonReader reader, JsonSerializationContext context) { var v = UnityJsonObjectReader.ReadFloats(ref reader, context, Names); return new Rect(v.X, v.Y, v.Z, v.W); }
        public override void Write(JsonWriter writer, Rect value, JsonSerializationContext context) { Span<float> v = stackalloc float[4] { value.x, value.y, value.width, value.height }; UnityJsonObjectWriter.WriteFloats(writer, Names, v); }
    }
    public sealed class RectIntJsonConverter : JsonConverter<RectInt>
    {
        private static readonly string[] Names = { "x", "y", "width", "height" };
        public static RectIntJsonConverter Instance { get; } = new();
        public override RectInt Read(ref JsonReader reader, JsonSerializationContext context) { var v = UnityJsonObjectReader.ReadInts(ref reader, context, Names); return new RectInt(v.X, v.Y, v.Z, v.W); }
        public override void Write(JsonWriter writer, RectInt value, JsonSerializationContext context) { Span<int> v = stackalloc int[4] { value.x, value.y, value.width, value.height }; UnityJsonObjectWriter.WriteInts(writer, Names, v); }
    }

    public sealed class BoundsJsonConverter : JsonConverter<Bounds>
    {
        public static BoundsJsonConverter Instance { get; } = new();
        public override Bounds Read(ref JsonReader reader, JsonSerializationContext context)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a JSON object for Bounds.");
            var center = default(Vector3); var size = default(Vector3);
            var centerAssigned = false; var sizeAssigned = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString(); if (!reader.Read()) throw new JsonException("Bounds property is missing its value.");
                if (UnityJsonObjectReader.NameEquals("center", name, context))
                {
                    if (centerAssigned && context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error)
                        throw new JsonException($"Duplicate Unity value member '{name}'.");
                    center = Vector3JsonConverter.Instance.Read(ref reader, context); centerAssigned = true;
                }
                else if (UnityJsonObjectReader.NameEquals("size", name, context))
                {
                    if (sizeAssigned && context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error)
                        throw new JsonException($"Duplicate Unity value member '{name}'.");
                    size = Vector3JsonConverter.Instance.Read(ref reader, context); sizeAssigned = true;
                }
                else
                {
                    if (context.Options.UnknownMemberHandling == JsonUnknownMemberHandling.Error)
                        throw new JsonException($"Unknown Unity value member '{name}'.");
                    reader.SkipCurrentValue();
                }
            }
            return new Bounds(center, size);
        }
        public override void Write(JsonWriter writer, Bounds value, JsonSerializationContext context) { writer.WriteStartObject(); writer.WritePropertyName("center"); Vector3JsonConverter.Instance.Write(writer, value.center, context); writer.WritePropertyName("size"); Vector3JsonConverter.Instance.Write(writer, value.size, context); writer.WriteEndObject(); }
    }
    public sealed class BoundsIntJsonConverter : JsonConverter<BoundsInt>
    {
        public static BoundsIntJsonConverter Instance { get; } = new();
        public override BoundsInt Read(ref JsonReader reader, JsonSerializationContext context)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a JSON object for BoundsInt.");
            var position = default(Vector3Int); var size = default(Vector3Int);
            var positionAssigned = false; var sizeAssigned = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString(); if (!reader.Read()) throw new JsonException("BoundsInt property is missing its value.");
                if (UnityJsonObjectReader.NameEquals("position", name, context))
                {
                    if (positionAssigned && context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error)
                        throw new JsonException($"Duplicate Unity value member '{name}'.");
                    position = Vector3IntJsonConverter.Instance.Read(ref reader, context); positionAssigned = true;
                }
                else if (UnityJsonObjectReader.NameEquals("size", name, context))
                {
                    if (sizeAssigned && context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error)
                        throw new JsonException($"Duplicate Unity value member '{name}'.");
                    size = Vector3IntJsonConverter.Instance.Read(ref reader, context); sizeAssigned = true;
                }
                else
                {
                    if (context.Options.UnknownMemberHandling == JsonUnknownMemberHandling.Error)
                        throw new JsonException($"Unknown Unity value member '{name}'.");
                    reader.SkipCurrentValue();
                }
            }
            return new BoundsInt(position, size);
        }
        public override void Write(JsonWriter writer, BoundsInt value, JsonSerializationContext context) { writer.WriteStartObject(); writer.WritePropertyName("position"); Vector3IntJsonConverter.Instance.Write(writer, value.position, context); writer.WritePropertyName("size"); Vector3IntJsonConverter.Instance.Write(writer, value.size, context); writer.WriteEndObject(); }
    }
}
