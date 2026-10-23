#nullable enable
using System;
using System.Globalization;

namespace YuzeToolkit.Json
{
    public enum JsonNumberKind
    {
        Int64,
        UInt64,
        Decimal,
        Double,
        Raw
    }

    public readonly struct JsonNumber : IEquatable<JsonNumber>
    {
        private readonly long _int64;
        private readonly ulong _uint64;
        private readonly decimal _decimal;
        private readonly double _double;
        private readonly string? _raw;

        public JsonNumberKind Kind { get; }

        public JsonNumber(long value)
        {
            Kind = JsonNumberKind.Int64;
            _int64 = value;
            _uint64 = default;
            _decimal = default;
            _double = default;
            _raw = null;
        }

        public JsonNumber(ulong value)
        {
            Kind = JsonNumberKind.UInt64;
            _uint64 = value;
            _int64 = default;
            _decimal = default;
            _double = default;
            _raw = null;
        }

        public JsonNumber(decimal value)
        {
            Kind = JsonNumberKind.Decimal;
            _decimal = value;
            _int64 = default;
            _uint64 = default;
            _double = default;
            _raw = null;
        }

        public JsonNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "NaN and Infinity are not valid JSON numbers.");
            Kind = JsonNumberKind.Double;
            _double = value;
            _int64 = default;
            _uint64 = default;
            _decimal = default;
            _raw = null;
        }

        private JsonNumber(string raw)
        {
            Kind = JsonNumberKind.Raw;
            _raw = raw;
            _int64 = default;
            _uint64 = default;
            _decimal = default;
            _double = default;
        }

        public static JsonNumber Parse(ReadOnlySpan<char> value)
        {
            if (!IsValid(value)) throw new JsonException($"'{value.ToString()}' is not a valid JSON number.");
            return ParseValidated(value);
        }

        internal static JsonNumber ParseValidated(ReadOnlySpan<char> value)
        {
            var isFractional = value.IndexOf('.') >= 0 || value.IndexOf('e') >= 0 || value.IndexOf('E') >= 0;
            if (!isFractional)
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed))
                    return new JsonNumber(signed);
                if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned))
                    return new JsonNumber(unsigned);
            }
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
                return new JsonNumber(decimalValue);
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) &&
                !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue))
                return new JsonNumber(doubleValue);
            return new JsonNumber(value.ToString());
        }

        public bool TryGetInt64(out long value)
        {
            switch (Kind)
            {
                case JsonNumberKind.Int64: value = _int64; return true;
                case JsonNumberKind.UInt64 when _uint64 <= long.MaxValue: value = (long)_uint64; return true;
                case JsonNumberKind.Decimal when decimal.Truncate(_decimal) == _decimal &&
                                                     _decimal is >= long.MinValue and <= long.MaxValue:
                    value = (long)_decimal; return true;
                case JsonNumberKind.Raw:
                    return long.TryParse(_raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                case JsonNumberKind.Double:
                    if (Math.Truncate(_double) != _double) break;
                    try
                    {
                        value = checked((long)_double);
                        return true;
                    }
                    catch (OverflowException)
                    {
                        break;
                    }
            }
            value = default;
            return false;
        }

        public bool TryGetUInt64(out ulong value)
        {
            switch (Kind)
            {
                case JsonNumberKind.UInt64: value = _uint64; return true;
                case JsonNumberKind.Int64 when _int64 >= 0: value = (ulong)_int64; return true;
                case JsonNumberKind.Decimal when decimal.Truncate(_decimal) == _decimal &&
                                                     _decimal is >= 0 and <= ulong.MaxValue:
                    value = (ulong)_decimal; return true;
                case JsonNumberKind.Raw:
                    return ulong.TryParse(_raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                case JsonNumberKind.Double:
                    if (Math.Truncate(_double) != _double || _double < 0) break;
                    try
                    {
                        value = checked((ulong)_double);
                        return true;
                    }
                    catch (OverflowException)
                    {
                        break;
                    }
            }
            value = default;
            return false;
        }

        public bool TryGetDecimal(out decimal value)
        {
            switch (Kind)
            {
                case JsonNumberKind.Int64: value = _int64; return true;
                case JsonNumberKind.UInt64: value = _uint64; return true;
                case JsonNumberKind.Decimal: value = _decimal; return true;
                case JsonNumberKind.Double:
                    try
                    {
                        value = (decimal)_double;
                        return true;
                    }
                    catch (OverflowException)
                    {
                        value = default;
                        return false;
                    }
                case JsonNumberKind.Raw:
                    return decimal.TryParse(_raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                default:
                    value = default;
                    return false;
            }
        }

        public bool TryGetDouble(out double value)
        {
            switch (Kind)
            {
                case JsonNumberKind.Int64: value = _int64; return true;
                case JsonNumberKind.UInt64: value = _uint64; return true;
                case JsonNumberKind.Decimal: value = (double)_decimal; return true;
                case JsonNumberKind.Double: value = _double; return true;
                case JsonNumberKind.Raw:
                    return double.TryParse(_raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                           !double.IsNaN(value) && !double.IsInfinity(value);
                default:
                    value = default;
                    return false;
            }
        }

        public long AsInt64() => TryGetInt64(out var value)
            ? value
            : throw new InvalidCastException($"JSON number {this} cannot be represented as Int64.");
        public ulong AsUInt64() => TryGetUInt64(out var value)
            ? value
            : throw new InvalidCastException($"JSON number {this} cannot be represented as UInt64.");
        public decimal AsDecimal() => TryGetDecimal(out var value)
            ? value
            : throw new InvalidCastException($"JSON number {this} cannot be represented as Decimal.");
        public double AsDouble() => TryGetDouble(out var value)
            ? value
            : throw new InvalidCastException($"JSON number {this} cannot be represented as Double.");

        public override string ToString() => Kind switch
        {
            JsonNumberKind.Int64 => _int64.ToString(CultureInfo.InvariantCulture),
            JsonNumberKind.UInt64 => _uint64.ToString(CultureInfo.InvariantCulture),
            JsonNumberKind.Decimal => _decimal.ToString(CultureInfo.InvariantCulture),
            JsonNumberKind.Double => _double.ToString("R", CultureInfo.InvariantCulture),
            JsonNumberKind.Raw => _raw!,
            _ => throw new ArgumentOutOfRangeException()
        };

        internal void WriteTo(JsonWriter writer)
        {
            switch (Kind)
            {
                case JsonNumberKind.Int64: writer.WriteNumberValue(_int64); return;
                case JsonNumberKind.UInt64: writer.WriteNumberValue(_uint64); return;
                case JsonNumberKind.Decimal: writer.WriteNumberValue(_decimal); return;
                case JsonNumberKind.Double: writer.WriteNumberValue(_double); return;
                case JsonNumberKind.Raw: writer.WriteValidatedRawNumberValue(_raw.AsSpan()); return;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public bool Equals(JsonNumber other)
        {
            if (TryGetDecimal(out var leftDecimal) && other.TryGetDecimal(out var rightDecimal))
                return leftDecimal == rightDecimal;
            if (TryGetDouble(out var leftDouble) && other.TryGetDouble(out var rightDouble))
                return leftDouble.Equals(rightDouble);
            return string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is JsonNumber other && Equals(other);
        public override int GetHashCode()
        {
            if (TryGetDecimal(out var decimalValue)) return decimalValue.GetHashCode();
            if (TryGetDouble(out var doubleValue)) return doubleValue.GetHashCode();
            return ToString().GetHashCode();
        }

        internal static bool IsValid(ReadOnlySpan<char> value)
        {
            if (value.Length == 0) return false;
            var index = 0;
            if (value[index] == '-')
            {
                index++;
                if (index == value.Length) return false;
            }
            if (value[index] == '0')
            {
                index++;
                if (index < value.Length && IsDigit(value[index])) return false;
            }
            else
            {
                if (!IsNonZeroDigit(value[index])) return false;
                while (index < value.Length && IsDigit(value[index])) index++;
            }
            if (index < value.Length && value[index] == '.')
            {
                index++;
                if (index == value.Length || !IsDigit(value[index])) return false;
                while (index < value.Length && IsDigit(value[index])) index++;
            }
            if (index < value.Length && value[index] is 'e' or 'E')
            {
                index++;
                if (index < value.Length && value[index] is '+' or '-') index++;
                if (index == value.Length || !IsDigit(value[index])) return false;
                while (index < value.Length && IsDigit(value[index])) index++;
            }
            return index == value.Length;
        }

        private static bool IsDigit(char value) => value is >= '0' and <= '9';
        private static bool IsNonZeroDigit(char value) => value is >= '1' and <= '9';
    }
}
