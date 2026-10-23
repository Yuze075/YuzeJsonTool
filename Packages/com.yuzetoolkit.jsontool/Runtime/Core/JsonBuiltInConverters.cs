#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace YuzeToolkit.Json
{
    internal sealed class DefaultJsonTypeResolver : IJsonTypeResolver
    {
        public static DefaultJsonTypeResolver Instance { get; } = new();
        private readonly ConcurrentDictionary<Type, JsonTypeInfo> _cache = new();

        public bool TryGetTypeInfo(Type type, out JsonTypeInfo typeInfo)
        {
            try
            {
                typeInfo = GetRequired(type);
                return true;
            }
            catch (NotSupportedException)
            {
                typeInfo = null!;
                return false;
            }
        }

        public JsonTypeInfo GetRequired(Type type) => _cache.GetOrAdd(type, Create);

        private static JsonTypeInfo Create(Type type)
        {
            var converterAttribute = (JsonConverterAttribute?)Attribute.GetCustomAttribute(type, typeof(JsonConverterAttribute));
            if (converterAttribute != null)
            {
                var converter = Activator.CreateInstance(converterAttribute.ConverterType) as JsonConverter
                                ?? throw new JsonException($"{converterAttribute.ConverterType} is not a JsonConverter.");
                return new JsonTypeInfo(type, converter);
            }

            if (type == typeof(string)) return new JsonTypeInfo<string>(StringJsonConverter.Instance);
            if (type == typeof(bool)) return new JsonTypeInfo<bool>(BooleanJsonConverter.Instance);
            if (type == typeof(byte)) return new JsonTypeInfo<byte>(ByteJsonConverter.Instance);
            if (type == typeof(sbyte)) return new JsonTypeInfo<sbyte>(SByteJsonConverter.Instance);
            if (type == typeof(short)) return new JsonTypeInfo<short>(Int16JsonConverter.Instance);
            if (type == typeof(ushort)) return new JsonTypeInfo<ushort>(UInt16JsonConverter.Instance);
            if (type == typeof(int)) return new JsonTypeInfo<int>(Int32JsonConverter.Instance);
            if (type == typeof(uint)) return new JsonTypeInfo<uint>(UInt32JsonConverter.Instance);
            if (type == typeof(long)) return new JsonTypeInfo<long>(Int64JsonConverter.Instance);
            if (type == typeof(ulong)) return new JsonTypeInfo<ulong>(UInt64JsonConverter.Instance);
            if (type == typeof(float)) return new JsonTypeInfo<float>(SingleJsonConverter.Instance);
            if (type == typeof(double)) return new JsonTypeInfo<double>(DoubleJsonConverter.Instance);
            if (type == typeof(decimal)) return new JsonTypeInfo<decimal>(DecimalJsonConverter.Instance);
            if (type == typeof(char)) return new JsonTypeInfo<char>(CharJsonConverter.Instance);
            if (type == typeof(Guid)) return new JsonTypeInfo<Guid>(GuidJsonConverter.Instance);
            if (type == typeof(DateTime)) return new JsonTypeInfo<DateTime>(DateTimeJsonConverter.Instance);
            if (type == typeof(DateTimeOffset)) return new JsonTypeInfo<DateTimeOffset>(DateTimeOffsetJsonConverter.Instance);
            if (type == typeof(TimeSpan)) return new JsonTypeInfo<TimeSpan>(TimeSpanJsonConverter.Instance);
            if (type == typeof(JsonData)) return new JsonTypeInfo<JsonData>(JsonDataConverter.Instance);
            if (type == typeof(object)) return new JsonTypeInfo<object>(ObjectJsonConverter.Instance);

            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
                return new JsonTypeInfo(type, (JsonConverter)Activator.CreateInstance(
                    typeof(NullableJsonConverter<>).MakeGenericType(nullable))!);
            if (type.IsEnum) return new JsonTypeInfo(type, new EnumJsonConverter(type));
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                var arrayElementType = type.GetElementType()!;
                return new JsonTypeInfo(type, (JsonConverter)Activator.CreateInstance(
                    typeof(ArrayJsonConverter<>).MakeGenericType(arrayElementType))!);
            }

            if (TryGetDictionaryTypes(type, out var keyType, out var valueType))
            {
                if (keyType != typeof(string))
                    throw new NotSupportedException($"JSON dictionaries require string keys; {type} uses {keyType}.");
                return new JsonTypeInfo(type, (JsonConverter)Activator.CreateInstance(
                    typeof(DictionaryJsonConverter<>).MakeGenericType(valueType), type)!);
            }
            if (TryGetCollectionElement(type, out var elementType))
                return new JsonTypeInfo(type, (JsonConverter)Activator.CreateInstance(
                    typeof(CollectionJsonConverter<>).MakeGenericType(elementType), type)!);

            if (type.IsInterface || type.IsAbstract || type.IsPointer || type.ContainsGenericParameters)
                throw new NotSupportedException($"Type {type} cannot be created by the reflection JSON serializer.");
            return new JsonTypeInfo(type, new ReflectionObjectJsonConverter(type));
        }

        private static bool TryGetDictionaryTypes(Type type, out Type key, out Type value)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = type.GetGenericArguments(); key = args[0]; value = args[1]; return true;
            }
            foreach (var contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(IDictionary<,>)) continue;
                var args = contract.GetGenericArguments(); key = args[0]; value = args[1]; return true;
            }
            key = null!; value = null!; return false;
        }

        private static bool TryGetCollectionElement(Type type, out Type element)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                element = type.GetGenericArguments()[0]; return true;
            }
            foreach (var contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(ICollection<>)) continue;
                element = contract.GetGenericArguments()[0]; return true;
            }
            element = null!; return false;
        }
    }

    public sealed class JsonDataConverter : JsonConverter<JsonData>
    {
        public static JsonDataConverter Instance { get; } = new();

        public override JsonData Read(ref JsonReader reader, JsonSerializationContext context)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null: return JsonData.Null;
                case JsonTokenType.String: return new JsonData(reader.GetString());
                case JsonTokenType.True: return new JsonData(true);
                case JsonTokenType.False: return new JsonData(false);
                case JsonTokenType.Number: return JsonData.NumberValidated(reader.RawValueSpan);
                case JsonTokenType.StartArray:
                    var array = JsonData.NewArray();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        array.Add(Read(ref reader, context));
                    return array;
                case JsonTokenType.StartObject:
                    var obj = JsonData.NewObject();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName)
                            throw new JsonException("Expected a JSON property name.");
                        var name = reader.GetString();
                        if (!reader.Read()) throw new JsonException("Expected a JSON property value.");
                        if (context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error &&
                            obj.ContainsKey(name))
                            throw new JsonException($"Duplicate JSON property '{name}'.");
                        obj[name] = Read(ref reader, context);
                    }
                    return obj;
                default:
                    throw new JsonException($"Token {reader.TokenType} cannot begin a JSON value.");
            }
        }

        public override void Write(JsonWriter writer, JsonData? value, JsonSerializationContext context)
        {
            value ??= JsonData.Null;
            switch (value.Type)
            {
                case JsonDataType.Null: writer.WriteNullValue(); return;
                case JsonDataType.String: writer.WriteStringValue(value.AsString()); return;
                case JsonDataType.Boolean: writer.WriteBooleanValue(value.AsBoolean()); return;
                case JsonDataType.Number: value.WriteNumberTo(writer); return;
                case JsonDataType.Array:
                    writer.WriteStartArray();
                    foreach (var child in value) Write(writer, child, context);
                    writer.WriteEndArray();
                    return;
                case JsonDataType.Object:
                    writer.WriteStartObject();
                    foreach (var property in value.Properties)
                    {
                        writer.WritePropertyName(property.Key);
                        Write(writer, property.Value, context);
                    }
                    writer.WriteEndObject();
                    return;
                default: throw new ArgumentOutOfRangeException();
            }
        }
    }

    internal abstract class RequiredValueConverter<T> : JsonConverter<T> where T : struct
    {
        protected static void RejectNull(ref JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Null)
                throw new JsonException($"JSON null cannot be assigned to {typeof(T)}.");
        }
    }

    internal sealed class StringJsonConverter : JsonConverter<string>
    {
        public static StringJsonConverter Instance { get; } = new();
        public override string? Read(ref JsonReader reader, JsonSerializationContext context) => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            _ => throw new JsonException($"Expected JSON string, found {reader.TokenType}.")
        };
        public override void Write(JsonWriter writer, string? value, JsonSerializationContext context) => writer.WriteStringValue(value);
    }

    internal sealed class BooleanJsonConverter : RequiredValueConverter<bool>
    {
        public static BooleanJsonConverter Instance { get; } = new();
        public override bool Read(ref JsonReader reader, JsonSerializationContext context)
        {
            RejectNull(ref reader);
            return reader.GetBoolean();
        }
        public override void Write(JsonWriter writer, bool value, JsonSerializationContext context) => writer.WriteBooleanValue(value);
    }

    internal abstract class IntegerJsonConverter<T> : RequiredValueConverter<T> where T : struct
    {
        protected abstract T ConvertSigned(long value);
        protected abstract T ConvertUnsigned(ulong value);
        protected abstract void WriteValue(JsonWriter writer, T value);
        public override T Read(ref JsonReader reader, JsonSerializationContext context)
        {
            RejectNull(ref reader);
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException($"Expected JSON number, found {reader.TokenType}.");
            try
            {
                if (reader.RawValueSpan.Length > 0 && reader.RawValueSpan[0] == '-') return ConvertSigned(reader.GetInt64());
                if (reader.TryGetInt64(out var signed)) return ConvertSigned(signed);
                return ConvertUnsigned(reader.GetUInt64());
            }
            catch (OverflowException exception)
            {
                throw new JsonException($"JSON number is outside the range of {typeof(T)}.", exception);
            }
        }
        public override void Write(JsonWriter writer, T value, JsonSerializationContext context) => WriteValue(writer, value);
    }

    internal sealed class ByteJsonConverter : IntegerJsonConverter<byte> { public static ByteJsonConverter Instance { get; } = new(); protected override byte ConvertSigned(long v) => checked((byte)v); protected override byte ConvertUnsigned(ulong v) => checked((byte)v); protected override void WriteValue(JsonWriter w, byte v) => w.WriteNumberValue(v); }
    internal sealed class SByteJsonConverter : IntegerJsonConverter<sbyte> { public static SByteJsonConverter Instance { get; } = new(); protected override sbyte ConvertSigned(long v) => checked((sbyte)v); protected override sbyte ConvertUnsigned(ulong v) => checked((sbyte)v); protected override void WriteValue(JsonWriter w, sbyte v) => w.WriteNumberValue(v); }
    internal sealed class Int16JsonConverter : IntegerJsonConverter<short> { public static Int16JsonConverter Instance { get; } = new(); protected override short ConvertSigned(long v) => checked((short)v); protected override short ConvertUnsigned(ulong v) => checked((short)v); protected override void WriteValue(JsonWriter w, short v) => w.WriteNumberValue(v); }
    internal sealed class UInt16JsonConverter : IntegerJsonConverter<ushort> { public static UInt16JsonConverter Instance { get; } = new(); protected override ushort ConvertSigned(long v) => checked((ushort)v); protected override ushort ConvertUnsigned(ulong v) => checked((ushort)v); protected override void WriteValue(JsonWriter w, ushort v) => w.WriteNumberValue(v); }
    internal sealed class Int32JsonConverter : IntegerJsonConverter<int> { public static Int32JsonConverter Instance { get; } = new(); protected override int ConvertSigned(long v) => checked((int)v); protected override int ConvertUnsigned(ulong v) => checked((int)v); protected override void WriteValue(JsonWriter w, int v) => w.WriteNumberValue(v); }
    internal sealed class UInt32JsonConverter : IntegerJsonConverter<uint> { public static UInt32JsonConverter Instance { get; } = new(); protected override uint ConvertSigned(long v) => checked((uint)v); protected override uint ConvertUnsigned(ulong v) => checked((uint)v); protected override void WriteValue(JsonWriter w, uint v) => w.WriteNumberValue(v); }
    internal sealed class Int64JsonConverter : IntegerJsonConverter<long> { public static Int64JsonConverter Instance { get; } = new(); protected override long ConvertSigned(long v) => v; protected override long ConvertUnsigned(ulong v) => checked((long)v); protected override void WriteValue(JsonWriter w, long v) => w.WriteNumberValue(v); }
    internal sealed class UInt64JsonConverter : IntegerJsonConverter<ulong> { public static UInt64JsonConverter Instance { get; } = new(); protected override ulong ConvertSigned(long v) => checked((ulong)v); protected override ulong ConvertUnsigned(ulong v) => v; protected override void WriteValue(JsonWriter w, ulong v) => w.WriteNumberValue(v); }

    internal sealed class SingleJsonConverter : RequiredValueConverter<float>
    {
        public static SingleJsonConverter Instance { get; } = new();
        public override float Read(ref JsonReader reader, JsonSerializationContext context)
        {
            RejectNull(ref reader);
            var value = reader.GetDouble();
            if (value < -float.MaxValue || value > float.MaxValue)
                throw new JsonException("JSON number is outside the range of Single.");
            return (float)value;
        }
        public override void Write(JsonWriter writer, float value, JsonSerializationContext context) => writer.WriteNumberValue(value);
    }
    internal sealed class DoubleJsonConverter : RequiredValueConverter<double>
    {
        public static DoubleJsonConverter Instance { get; } = new();
        public override double Read(ref JsonReader reader, JsonSerializationContext context) { RejectNull(ref reader); return reader.GetDouble(); }
        public override void Write(JsonWriter writer, double value, JsonSerializationContext context) => writer.WriteNumberValue(value);
    }
    internal sealed class DecimalJsonConverter : RequiredValueConverter<decimal>
    {
        public static DecimalJsonConverter Instance { get; } = new();
        public override decimal Read(ref JsonReader reader, JsonSerializationContext context) { RejectNull(ref reader); return reader.GetDecimal(); }
        public override void Write(JsonWriter writer, decimal value, JsonSerializationContext context) => writer.WriteNumberValue(value);
    }
    internal sealed class CharJsonConverter : RequiredValueConverter<char>
    {
        public static CharJsonConverter Instance { get; } = new();
        public override char Read(ref JsonReader reader, JsonSerializationContext context)
        {
            RejectNull(ref reader);
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected a JSON string for Char.");
            var value = reader.GetString();
            return value.Length == 1 ? value[0] : throw new JsonException("A Char JSON value must contain exactly one UTF-16 code unit.");
        }
        public override void Write(JsonWriter writer, char value, JsonSerializationContext context) => writer.WriteStringValue(value.ToString());
    }

    internal sealed class GuidJsonConverter : RequiredValueConverter<Guid>
    {
        public static GuidJsonConverter Instance { get; } = new();
        public override Guid Read(ref JsonReader reader, JsonSerializationContext context) { RejectNull(ref reader); return Guid.Parse(reader.GetString()); }
        public override void Write(JsonWriter writer, Guid value, JsonSerializationContext context) => writer.WriteStringValue(value.ToString("D"));
    }
    internal sealed class DateTimeJsonConverter : RequiredValueConverter<DateTime>
    {
        public static DateTimeJsonConverter Instance { get; } = new();
        public override DateTime Read(ref JsonReader reader, JsonSerializationContext context) { RejectNull(ref reader); return DateTime.Parse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }
        public override void Write(JsonWriter writer, DateTime value, JsonSerializationContext context) => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
    internal sealed class DateTimeOffsetJsonConverter : RequiredValueConverter<DateTimeOffset>
    {
        public static DateTimeOffsetJsonConverter Instance { get; } = new();
        public override DateTimeOffset Read(ref JsonReader reader, JsonSerializationContext context) { RejectNull(ref reader); return DateTimeOffset.Parse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }
        public override void Write(JsonWriter writer, DateTimeOffset value, JsonSerializationContext context) => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
    internal sealed class TimeSpanJsonConverter : RequiredValueConverter<TimeSpan>
    {
        public static TimeSpanJsonConverter Instance { get; } = new();
        public override TimeSpan Read(ref JsonReader reader, JsonSerializationContext context) { RejectNull(ref reader); return TimeSpan.ParseExact(reader.GetString(), "c", CultureInfo.InvariantCulture); }
        public override void Write(JsonWriter writer, TimeSpan value, JsonSerializationContext context) => writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
    }

    internal sealed class NullableJsonConverter<T> : JsonConverter<T?> where T : struct
    {
        public override T? Read(ref JsonReader reader, JsonSerializationContext context)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return (T)context.GetConverter(typeof(T)).ReadObject(ref reader, context)!;
        }
        public override void Write(JsonWriter writer, T? value, JsonSerializationContext context)
        {
            if (!value.HasValue) { writer.WriteNullValue(); return; }
            context.GetConverter(typeof(T)).WriteObject(writer, value.Value, context);
        }
    }

    internal sealed class EnumJsonConverter : JsonConverter
    {
        private readonly Type _type;
        public EnumJsonConverter(Type type) => _type = type;
        public override Type TargetType => _type;
        internal override object ReadObject(ref JsonReader reader, JsonSerializationContext context)
        {
            if (reader.TokenType == JsonTokenType.String)
                return Enum.Parse(_type, reader.GetString(), ignoreCase: context.Options.PropertyNameCaseInsensitive);
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException($"Expected string or number for enum {_type}.");
            var underlying = Enum.GetUnderlyingType(_type);
            var number = context.GetConverter(underlying).ReadObject(ref reader, context)!;
            return Enum.ToObject(_type, number);
        }
        internal override void WriteObject(JsonWriter writer, object? value, JsonSerializationContext context)
        {
            if (value == null) throw new JsonException($"JSON null cannot be assigned to {_type}.");
            if (context.Options.EnumFormat == JsonEnumFormat.String) writer.WriteStringValue(value.ToString());
            else
            {
                var underlying = Enum.GetUnderlyingType(_type);
                context.GetConverter(underlying).WriteObject(writer, Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture), context);
            }
        }
    }

    internal sealed class ArrayJsonConverter<TElement> : JsonConverter<TElement[]>
    {
        public override TElement[]? Read(ref JsonReader reader, JsonSerializationContext context)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected JSON array for {typeof(TElement[])}.");

            var items = new List<TElement>();
            var converter = context.GetConverter(typeof(TElement));
            if (converter is JsonConverter<TElement> typedConverter)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    items.Add(typedConverter.Read(ref reader, context)!);
            }
            else
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    items.Add((TElement)converter.ReadObject(ref reader, context)!);
            }
            return items.ToArray();
        }

        public override void Write(JsonWriter writer, TElement[]? value, JsonSerializationContext context)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            var converter = context.GetConverter(typeof(TElement));
            writer.WriteStartArray();
            if (converter is JsonConverter<TElement> typedConverter)
            {
                for (var index = 0; index < value.Length; index++)
                    typedConverter.Write(writer, value[index], context);
            }
            else
            {
                for (var index = 0; index < value.Length; index++)
                    converter.WriteObject(writer, value[index], context);
            }
            writer.WriteEndArray();
        }
    }

    internal sealed class CollectionJsonConverter<TElement> : JsonConverter
    {
        private readonly Type _type;
        private readonly Type _concreteType;

        public CollectionJsonConverter(Type type)
        {
            _type = type;
            _concreteType = type.IsInterface || type.IsAbstract ? typeof(List<TElement>) : type;
            if (!_type.IsAssignableFrom(_concreteType))
                throw new NotSupportedException(
                    $"Collection {_type} requires a custom converter because List<{typeof(TElement)}> is not assignable to it.");
            if (_concreteType.GetConstructor(Type.EmptyTypes) == null)
                throw new NotSupportedException($"Collection {_concreteType} requires a public parameterless constructor.");
        }

        public override Type TargetType => _type;

        internal override object? ReadObject(ref JsonReader reader, JsonSerializationContext context)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected JSON array for {_type}.");

            var collection = (ICollection<TElement>)Activator.CreateInstance(_concreteType)!;
            var converter = context.GetConverter(typeof(TElement));
            if (converter is JsonConverter<TElement> typedConverter)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    collection.Add(typedConverter.Read(ref reader, context)!);
            }
            else
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    collection.Add((TElement)converter.ReadObject(ref reader, context)!);
            }
            return collection;
        }

        internal override void WriteObject(JsonWriter writer, object? value, JsonSerializationContext context)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            var collection = (IEnumerable<TElement>)value;
            var converter = context.GetConverter(typeof(TElement));
            writer.WriteStartArray();
            if (converter is JsonConverter<TElement> typedConverter)
            {
                foreach (var item in collection) typedConverter.Write(writer, item, context);
            }
            else
            {
                foreach (var item in collection) converter.WriteObject(writer, item, context);
            }
            writer.WriteEndArray();
        }
    }

    internal sealed class DictionaryJsonConverter<TValue> : JsonConverter
    {
        private readonly Type _type;
        private readonly Type _concreteType;

        public DictionaryJsonConverter(Type type)
        {
            _type = type;
            _concreteType = type.IsInterface || type.IsAbstract ? typeof(Dictionary<string, TValue>) : type;
            if (!_type.IsAssignableFrom(_concreteType))
                throw new NotSupportedException(
                    $"Dictionary {_type} requires a custom converter because Dictionary<string, {typeof(TValue)}> is not assignable to it.");
            if (_concreteType.GetConstructor(Type.EmptyTypes) == null)
                throw new NotSupportedException($"Dictionary {_concreteType} requires a public parameterless constructor.");
        }

        public override Type TargetType => _type;

        internal override object? ReadObject(ref JsonReader reader, JsonSerializationContext context)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected JSON object for {_type}.");

            var dictionary = (IDictionary<string, TValue>)Activator.CreateInstance(_concreteType)!;
            var converter = context.GetConverter(typeof(TValue));
            var names = context.Options.DuplicatePropertyHandling == JsonDuplicatePropertyHandling.Error
                ? new HashSet<string>(StringComparer.Ordinal) : null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                if (names != null && !names.Add(name)) throw new JsonException($"Duplicate JSON property '{name}'.");
                if (!reader.Read()) throw new JsonException("Expected dictionary value.");
                dictionary[name] = converter is JsonConverter<TValue> typedConverter
                    ? typedConverter.Read(ref reader, context)!
                    : (TValue)converter.ReadObject(ref reader, context)!;
            }
            return dictionary;
        }

        internal override void WriteObject(JsonWriter writer, object? value, JsonSerializationContext context)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            var dictionary = (IEnumerable<KeyValuePair<string, TValue>>)value;
            var converter = context.GetConverter(typeof(TValue));
            writer.WriteStartObject();
            foreach (var entry in dictionary)
            {
                if (context.Options.IgnoreNullValues && entry.Value is null) continue;
                writer.WritePropertyName(entry.Key);
                if (converter is JsonConverter<TValue> typedConverter)
                    typedConverter.Write(writer, entry.Value, context);
                else converter.WriteObject(writer, entry.Value, context);
            }
            writer.WriteEndObject();
        }
    }

    internal sealed class ObjectJsonConverter : JsonConverter<object>
    {
        public static ObjectJsonConverter Instance { get; } = new();
        public override object? Read(ref JsonReader reader, JsonSerializationContext context) =>
            JsonDataConverter.Instance.Read(ref reader, context);
        public override void Write(JsonWriter writer, object? value, JsonSerializationContext context)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            if (value.GetType() == typeof(object)) { writer.WriteStartObject(); writer.WriteEndObject(); return; }
            context.GetConverter(value.GetType()).WriteObject(writer, value, context);
        }
    }
}
