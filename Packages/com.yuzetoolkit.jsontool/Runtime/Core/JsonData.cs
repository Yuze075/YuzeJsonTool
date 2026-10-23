#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace YuzeToolkit.Json
{
    /// <summary>Mutable JSON DOM node with explicit JSON null support.</summary>
    public sealed class JsonData : IEnumerable<JsonData>, IEquatable<JsonData>
    {
        private sealed class ObjectStorage
        {
            private const int IndexThreshold = 8;
            private readonly List<KeyValuePair<string, JsonData>> _items = new();
            private Dictionary<string, int>? _indices;

            public int Count => _items.Count;
            public IEnumerable<string> Keys
            {
                get
                {
                    foreach (var item in _items) yield return item.Key;
                }
            }
            public IEnumerable<KeyValuePair<string, JsonData>> Properties => _items;

            public JsonData this[string key]
            {
                get => TryGetIndex(key, out var index)
                    ? _items[index].Value
                    : throw new KeyNotFoundException($"JSON property '{key}' was not found.");
                set
                {
                    if (TryGetIndex(key, out var index))
                    {
                        _items[index] = new KeyValuePair<string, JsonData>(key, value);
                        return;
                    }
                    _items.Add(new KeyValuePair<string, JsonData>(key, value));
                    if (_indices != null) _indices.Add(key, _items.Count - 1);
                    else if (_items.Count >= IndexThreshold) BuildIndex();
                }
            }

            public JsonData this[int index]
            {
                get => _items[index].Value;
                set
                {
                    var pair = _items[index];
                    _items[index] = new KeyValuePair<string, JsonData>(pair.Key, value);
                }
            }

            public bool Contains(string key) => TryGetIndex(key, out _);
            public bool TryGetValue(string key, out JsonData value)
            {
                if (TryGetIndex(key, out var index))
                {
                    value = _items[index].Value;
                    return true;
                }
                value = Null;
                return false;
            }

            public bool Remove(string key)
            {
                if (!TryGetIndex(key, out var index)) return false;
                _items.RemoveAt(index);
                if (_indices != null) BuildIndex();
                return true;
            }

            private bool TryGetIndex(string key, out int index)
            {
                if (_indices != null) return _indices.TryGetValue(key, out index);
                for (var current = 0; current < _items.Count; current++)
                {
                    if (string.Equals(_items[current].Key, key, StringComparison.Ordinal))
                    {
                        index = current;
                        return true;
                    }
                }
                index = -1;
                return false;
            }

            private void BuildIndex()
            {
                _indices ??= new Dictionary<string, int>(StringComparer.Ordinal);
                _indices.Clear();
                for (var index = 0; index < _items.Count; index++) _indices.Add(_items[index].Key, index);
            }
        }

        private static readonly JsonData NullInstance = new(JsonDataType.Null, null);
        private static readonly object TrueValue = true;
        private static readonly object FalseValue = false;
        private object? _value;

        public static JsonData Null => NullInstance;
        public JsonDataType Type { get; }
        public bool IsNull => Type == JsonDataType.Null;
        public bool IsObject => Type == JsonDataType.Object;
        public bool IsArray => Type == JsonDataType.Array;
        public bool IsString => Type == JsonDataType.String;
        public bool IsNumber => Type == JsonDataType.Number;
        public bool IsBoolean => Type == JsonDataType.Boolean;
        public int Count => Type switch
        {
            JsonDataType.Object => Object.Count,
            JsonDataType.Array => Array.Count,
            _ => throw new InvalidOperationException("Only JSON objects and arrays have a count.")
        };
        public IEnumerable<string> Keys => Object.Keys;
        public IEnumerable<KeyValuePair<string, JsonData>> Properties => Object.Properties;

        public JsonData this[string key]
        {
            get => Object[key ?? throw new ArgumentNullException(nameof(key))];
            set => Object[key ?? throw new ArgumentNullException(nameof(key))] = value ?? Null;
        }

        public JsonData this[int index]
        {
            get => Type == JsonDataType.Array ? Array[index] : Object[index];
            set
            {
                value ??= Null;
                if (Type == JsonDataType.Array) Array[index] = value;
                else Object[index] = value;
            }
        }

        public JsonData(string value) : this(JsonDataType.String, value ?? throw new ArgumentNullException(nameof(value))) { }
        public JsonData(bool value) : this(JsonDataType.Boolean, value ? TrueValue : FalseValue) { }
        public JsonData(long value) : this(JsonDataType.Number, new JsonNumber(value)) { }
        public JsonData(ulong value) : this(JsonDataType.Number, new JsonNumber(value)) { }
        public JsonData(int value) : this((long)value) { }
        public JsonData(uint value) : this((ulong)value) { }
        public JsonData(double value) : this(JsonDataType.Number, new JsonNumber(value)) { }
        public JsonData(float value) : this((double)value) { }
        public JsonData(decimal value) : this(JsonDataType.Number, new JsonNumber(value)) { }

        private JsonData(JsonDataType type, object? value)
        {
            Type = type;
            _value = value;
        }

        public static JsonData NewObject() => new(JsonDataType.Object, new ObjectStorage());
        public static JsonData NewArray() => new(JsonDataType.Array, new List<JsonData>());
        public static JsonData Number(ReadOnlySpan<char> rawNumber)
        {
            if (!JsonNumber.IsValid(rawNumber))
                throw new JsonException($"'{rawNumber.ToString()}' is not a valid JSON number.");
            return NumberValidated(rawNumber);
        }

        internal static JsonData NumberValidated(ReadOnlySpan<char> rawNumber) =>
            new(JsonDataType.Number, rawNumber.ToString());

        public static JsonData Parse(string json, JsonOptions? options = null) => YuzeJson.Parse(json, options);

        public JsonData Add(JsonData? value)
        {
            if (Type != JsonDataType.Array) throw new InvalidOperationException("JSON node is not an array.");
            Array.Add(value ?? Null);
            return this;
        }

        public JsonData Add(string key, JsonData? value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            Object[key] = value ?? Null;
            return this;
        }

        public bool ContainsKey(string key) => Object.Contains(key);
        public bool TryGetValue(string key, out JsonData value) => Object.TryGetValue(key, out value);
        public bool Remove(string key) => Object.Remove(key);
        public void RemoveAt(int index)
        {
            if (Type != JsonDataType.Array) throw new InvalidOperationException("JSON node is not an array.");
            Array.RemoveAt(index);
        }

        public string AsString() => Type == JsonDataType.String
            ? (string)_value!
            : throw TypeError(JsonDataType.String);
        public bool AsBoolean() => Type == JsonDataType.Boolean
            ? (bool)_value!
            : throw TypeError(JsonDataType.Boolean);
        public JsonNumber AsNumber()
        {
            if (Type != JsonDataType.Number) throw TypeError(JsonDataType.Number);
            return _value is string raw ? JsonNumber.ParseValidated(raw.AsSpan()) : (JsonNumber)_value!;
        }
        public long AsInt64() => AsNumber().AsInt64();
        public ulong AsUInt64() => AsNumber().AsUInt64();
        public decimal AsDecimal() => AsNumber().AsDecimal();
        public double AsDouble() => AsNumber().AsDouble();

        public JsonData Clone()
        {
            switch (Type)
            {
                case JsonDataType.Null: return Null;
                case JsonDataType.String: return new JsonData(AsString());
                case JsonDataType.Boolean: return new JsonData(AsBoolean());
                case JsonDataType.Number: return new JsonData(JsonDataType.Number, _value);
                case JsonDataType.Array:
                    var array = NewArray();
                    foreach (var child in Array) array.Add(child.Clone());
                    return array;
                case JsonDataType.Object:
                    var obj = NewObject();
                    foreach (var property in Object.Properties) obj.Add(property.Key, property.Value.Clone());
                    return obj;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public string ToJson(JsonOptions? options = null) => YuzeJson.Serialize(this, JsonTypeInfo<JsonData>.From(JsonDataConverter.Instance), options);

        public bool Equals(JsonData? other)
        {
            if (other == null || Type != other.Type) return false;
            switch (Type)
            {
                case JsonDataType.Null: return true;
                case JsonDataType.String: return AsString() == other.AsString();
                case JsonDataType.Boolean: return AsBoolean() == other.AsBoolean();
                case JsonDataType.Number: return AsNumber().Equals(other.AsNumber());
                case JsonDataType.Array:
                    if (Count != other.Count) return false;
                    for (var index = 0; index < Count; index++)
                        if (!this[index].Equals(other[index])) return false;
                    return true;
                case JsonDataType.Object:
                    if (Count != other.Count) return false;
                    foreach (var property in Properties)
                        if (!other.TryGetValue(property.Key, out var value) || !property.Value.Equals(value)) return false;
                    return true;
                default: return false;
            }
        }

        public override bool Equals(object? obj) => obj is JsonData other && Equals(other);
        public override int GetHashCode() => Type switch
        {
            JsonDataType.Null => 0,
            JsonDataType.String or JsonDataType.Boolean => HashCode.Combine(Type, _value),
            JsonDataType.Number => HashCode.Combine(Type, AsNumber().GetHashCode()),
            _ => HashCode.Combine(Type, Count)
        };
        public override string ToString() => ToJson();

        internal void WriteNumberTo(JsonWriter writer)
        {
            if (Type != JsonDataType.Number) throw TypeError(JsonDataType.Number);
            if (_value is string raw) writer.WriteValidatedRawNumberValue(raw.AsSpan());
            else ((JsonNumber)_value!).WriteTo(writer);
        }

        public IEnumerator<JsonData> GetEnumerator()
        {
            if (Type == JsonDataType.Array) return Array.GetEnumerator();
            if (Type == JsonDataType.Object) return EnumerateObjectValues().GetEnumerator();
            throw new InvalidOperationException("Only JSON objects and arrays can be enumerated.");
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static implicit operator JsonData?(string? value) => value == null ? Null : new JsonData(value);
        public static implicit operator JsonData(bool value) => new(value);
        public static implicit operator JsonData(int value) => new(value);
        public static implicit operator JsonData(long value) => new(value);
        public static implicit operator JsonData(uint value) => new(value);
        public static implicit operator JsonData(ulong value) => new(value);
        public static implicit operator JsonData(float value) => new(value);
        public static implicit operator JsonData(double value) => new(value);
        public static implicit operator JsonData(decimal value) => new(value);

        private ObjectStorage Object => Type == JsonDataType.Object
            ? (ObjectStorage)_value!
            : throw new InvalidOperationException("JSON node is not an object.");
        private List<JsonData> Array => Type == JsonDataType.Array
            ? (List<JsonData>)_value!
            : throw new InvalidOperationException("JSON node is not an array.");
        private InvalidCastException TypeError(JsonDataType expected) =>
            new($"Expected JSON {expected}, found {Type}.");
        private IEnumerable<JsonData> EnumerateObjectValues()
        {
            foreach (var property in Object.Properties) yield return property.Value;
        }
    }
}
