#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace YuzeToolkit.Json
{
    public interface IJsonTypeResolver
    {
        bool TryGetTypeInfo(Type type, out JsonTypeInfo typeInfo);
    }

    public abstract class JsonConverter
    {
        public abstract Type TargetType { get; }
        public virtual bool CanConvert(Type type) => type == TargetType;
        internal abstract object? ReadObject(ref JsonReader reader, JsonSerializationContext context);
        internal abstract void WriteObject(JsonWriter writer, object? value, JsonSerializationContext context);
    }

    public abstract class JsonConverter<T> : JsonConverter
    {
        public sealed override Type TargetType => typeof(T);
        public abstract T? Read(ref JsonReader reader, JsonSerializationContext context);
        public abstract void Write(JsonWriter writer, T? value, JsonSerializationContext context);

        internal sealed override object? ReadObject(ref JsonReader reader, JsonSerializationContext context) =>
            Read(ref reader, context);

        internal sealed override void WriteObject(JsonWriter writer, object? value, JsonSerializationContext context)
        {
            if (value == null)
            {
                Write(writer, default, context);
                return;
            }
            if (value is not T typed)
                throw new JsonException($"Converter for {typeof(T)} cannot serialize a value of type {value.GetType()}.");
            Write(writer, typed, context);
        }
    }

    public class JsonTypeInfo
    {
        public Type Type { get; }
        public JsonConverter Converter { get; }
        public IJsonTypeResolver? Resolver { get; }

        public JsonTypeInfo(Type type, JsonConverter converter, IJsonTypeResolver? resolver = null)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Converter = converter ?? throw new ArgumentNullException(nameof(converter));
            if (!converter.CanConvert(type))
                throw new ArgumentException($"Converter {converter.GetType()} cannot convert {type}.", nameof(converter));
            Resolver = resolver;
        }
    }

    public sealed class JsonTypeInfo<T> : JsonTypeInfo
    {
        public new JsonConverter<T> Converter => (JsonConverter<T>)base.Converter;

        public JsonTypeInfo(JsonConverter<T> converter, IJsonTypeResolver? resolver = null)
            : base(typeof(T), converter, resolver)
        {
        }

        public static JsonTypeInfo<T> From(JsonConverter<T> converter, IJsonTypeResolver? resolver = null) =>
            new(converter, resolver);
    }

    public sealed class JsonTypeInfoResolver : IJsonTypeResolver
    {
        private readonly Dictionary<Type, JsonTypeInfo> _types = new();

        public JsonTypeInfoResolver Add(JsonTypeInfo typeInfo)
        {
            if (typeInfo == null) throw new ArgumentNullException(nameof(typeInfo));
            _types[typeInfo.Type] = typeInfo;
            return this;
        }

        public JsonTypeInfoResolver Add<T>(JsonConverter<T> converter) => Add(new JsonTypeInfo<T>(converter));
        public bool TryGetTypeInfo(Type type, out JsonTypeInfo typeInfo) => _types.TryGetValue(type, out typeInfo!);
    }

    public sealed class CompositeJsonTypeResolver : IJsonTypeResolver
    {
        private readonly IJsonTypeResolver[] _resolvers;

        public CompositeJsonTypeResolver(params IJsonTypeResolver[] resolvers)
        {
            _resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));
        }

        public bool TryGetTypeInfo(Type type, out JsonTypeInfo typeInfo)
        {
            foreach (var resolver in _resolvers)
            {
                if (resolver != null && resolver.TryGetTypeInfo(type, out typeInfo)) return true;
            }
            typeInfo = null!;
            return false;
        }
    }

    public static class JsonConverterRegistry
    {
        private static readonly ConcurrentDictionary<Type, JsonTypeInfo> Registered = new();

        public static void Register<T>(JsonConverter<T> converter) => Register(new JsonTypeInfo<T>(converter));

        public static void Register(JsonTypeInfo typeInfo)
        {
            if (typeInfo == null) throw new ArgumentNullException(nameof(typeInfo));
            Registered[typeInfo.Type] = typeInfo;
        }

        public static bool Unregister<T>() => Registered.TryRemove(typeof(T), out _);
        public static bool TryGet(Type type, out JsonTypeInfo typeInfo) => Registered.TryGetValue(type, out typeInfo!);
    }

    public sealed class JsonSerializationContext
    {
        private readonly IJsonTypeResolver? _explicitResolver;

        public JsonOptions Options { get; }

        internal JsonSerializationContext(JsonOptions options, IJsonTypeResolver? explicitResolver)
        {
            Options = options;
            _explicitResolver = explicitResolver;
        }

        public JsonConverter GetConverter<T>() => GetConverter(typeof(T));

        public T? ReadValue<T>(ref JsonReader reader)
        {
            var converter = GetConverter(typeof(T));
            if (converter is JsonConverter<T> typedConverter)
                return typedConverter.Read(ref reader, this);
            var value = converter.ReadObject(ref reader, this);
            return value == null ? default : (T)value;
        }

        public object? ReadValue(Type type, ref JsonReader reader) =>
            GetConverter(type).ReadObject(ref reader, this);

        public void WriteValue<T>(JsonWriter writer, T? value)
        {
            var converter = GetConverter(typeof(T));
            if (converter is JsonConverter<T> typedConverter)
            {
                typedConverter.Write(writer, value, this);
                return;
            }
            converter.WriteObject(writer, value, this);
        }

        public void WriteValue(JsonWriter writer, Type type, object? value) =>
            GetConverter(type).WriteObject(writer, value, this);

        public JsonConverter GetConverter(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (_explicitResolver != null && _explicitResolver.TryGetTypeInfo(type, out var explicitInfo))
                return explicitInfo.Converter;
            if (Options.Resolver != null && Options.Resolver.TryGetTypeInfo(type, out var optionInfo))
                return optionInfo.Converter;
            if (JsonConverterRegistry.TryGet(type, out var registeredInfo)) return registeredInfo.Converter;
            return DefaultJsonTypeResolver.Instance.GetRequired(type).Converter;
        }
    }
}
