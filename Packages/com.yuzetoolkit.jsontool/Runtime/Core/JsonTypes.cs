#nullable enable
using System;

namespace YuzeToolkit.Json
{
    public enum JsonTokenType
    {
        None,
        StartObject,
        EndObject,
        StartArray,
        EndArray,
        PropertyName,
        String,
        Number,
        True,
        False,
        Null
    }

    public enum JsonDataType
    {
        Null,
        Object,
        Array,
        String,
        Number,
        Boolean
    }

    public enum JsonUnknownMemberHandling
    {
        Ignore,
        Error
    }

    public enum JsonDuplicatePropertyHandling
    {
        Replace,
        Error
    }

    public enum JsonEnumFormat
    {
        Number,
        String
    }

    public sealed class JsonException : FormatException
    {
        public int Offset { get; }
        public int Line { get; }
        public int Column { get; }
        public string? Path { get; }

        public JsonException(string message) : base(message)
        {
            Offset = -1;
            Line = -1;
            Column = -1;
        }

        public JsonException(string message, Exception innerException) : base(message, innerException)
        {
            Offset = -1;
            Line = -1;
            Column = -1;
        }

        internal JsonException(string message, int offset, int line, int column, string? path = null)
            : base($"{message} (line {line}, column {column}, offset {offset})")
        {
            Offset = offset;
            Line = line;
            Column = column;
            Path = path;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
    public sealed class JsonNameAttribute : Attribute
    {
        public string Name { get; }

        public JsonNameAttribute(string name)
        {
            Name = string.IsNullOrEmpty(name)
                ? throw new ArgumentException("JSON member name cannot be empty.", nameof(name))
                : name;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class JsonIgnoreAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class JsonRequiredAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field |
                    AttributeTargets.Property)]
    public sealed class JsonConverterAttribute : Attribute
    {
        public Type ConverterType { get; }

        public JsonConverterAttribute(Type converterType)
        {
            ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class JsonSerializableAttribute : Attribute
    {
        public Type Type { get; }

        public JsonSerializableAttribute(Type type)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
        }
    }
}
