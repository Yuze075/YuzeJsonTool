#nullable enable
using System;

namespace YuzeToolkit.Json
{
    public static class YuzeJson
    {
        public static JsonData Parse(string json, JsonOptions? options = null) =>
            Deserialize(json ?? throw new ArgumentNullException(nameof(json)),
                JsonTypeInfo<JsonData>.From(JsonDataConverter.Instance), options) ?? JsonData.Null;

        public static JsonData Parse(ReadOnlySpan<char> json, JsonOptions? options = null) =>
            Deserialize(json, JsonTypeInfo<JsonData>.From(JsonDataConverter.Instance), options) ?? JsonData.Null;

        public static string Serialize<T>(T? value, JsonOptions? options = null)
        {
            var effectiveOptions = options ?? JsonOptions.Default;
            effectiveOptions.Validate();
            var context = new JsonSerializationContext(effectiveOptions, explicitResolver: null);
            using var writer = new JsonWriter(effectiveOptions);
            context.GetConverter(typeof(T)).WriteObject(writer, value, context);
            return writer.ToString();
        }

        public static string Serialize<T>(T? value, JsonConverter<T> converter, JsonOptions? options = null) =>
            Serialize(value, JsonTypeInfo<T>.From(converter), options);

        public static string Serialize<T>(T? value, JsonTypeInfo<T> typeInfo, JsonOptions? options = null)
        {
            if (typeInfo == null) throw new ArgumentNullException(nameof(typeInfo));
            var effectiveOptions = options ?? JsonOptions.Default;
            effectiveOptions.Validate();
            var context = new JsonSerializationContext(effectiveOptions, typeInfo.Resolver);
            using var writer = new JsonWriter(effectiveOptions);
            typeInfo.Converter.Write(writer, value, context);
            return writer.ToString();
        }

        public static T? Deserialize<T>(string json, JsonOptions? options = null)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return Deserialize<T>(json.AsSpan(), options);
        }

        public static T? Deserialize<T>(ReadOnlySpan<char> json, JsonOptions? options = null)
        {
            var effectiveOptions = options ?? JsonOptions.Default;
            effectiveOptions.Validate();
            var context = new JsonSerializationContext(effectiveOptions, explicitResolver: null);
            var reader = new JsonReader(json, effectiveOptions);
            try
            {
                if (!reader.Read()) throw new JsonException("JSON input is empty.");
                var result = context.GetConverter(typeof(T)).ReadObject(ref reader, context);
                if (reader.Read()) throw new JsonException("Unexpected content after the root JSON value.");
                return result == null ? default : (T)result;
            }
            finally
            {
                reader.Dispose();
            }
        }

        public static T? Deserialize<T>(string json, JsonConverter<T> converter, JsonOptions? options = null) =>
            Deserialize(json ?? throw new ArgumentNullException(nameof(json)), JsonTypeInfo<T>.From(converter), options);

        public static T? Deserialize<T>(ReadOnlySpan<char> json, JsonConverter<T> converter,
            JsonOptions? options = null) => Deserialize(json, JsonTypeInfo<T>.From(converter), options);

        public static T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo, JsonOptions? options = null)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return Deserialize(json.AsSpan(), typeInfo, options);
        }

        public static T? Deserialize<T>(ReadOnlySpan<char> json, JsonTypeInfo<T> typeInfo,
            JsonOptions? options = null)
        {
            if (typeInfo == null) throw new ArgumentNullException(nameof(typeInfo));
            var effectiveOptions = options ?? JsonOptions.Default;
            effectiveOptions.Validate();
            var context = new JsonSerializationContext(effectiveOptions, typeInfo.Resolver);
            var reader = new JsonReader(json, effectiveOptions);
            try
            {
                if (!reader.Read()) throw new JsonException("JSON input is empty.");
                var result = typeInfo.Converter.Read(ref reader, context);
                if (reader.Read()) throw new JsonException("Unexpected content after the root JSON value.");
                return result;
            }
            finally
            {
                reader.Dispose();
            }
        }
    }
}
