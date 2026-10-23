#nullable enable
using System;
using System.Buffers;
using System.Globalization;

namespace YuzeToolkit.Json
{
    /// <summary>Allocation-conscious, forward-only reader over UTF-16 JSON text.</summary>
    public ref struct JsonReader
    {
        private const int FrameKindObject = 0x10;
        private const int FrameKindArray = 0x20;
        private const int FrameStateMask = 0x0F;

        private const int ObjectPropertyOrEnd = 0;
        private const int ObjectProperty = 1;
        private const int ObjectValue = 2;
        private const int ObjectCommaOrEnd = 3;
        private const int ArrayValueOrEnd = 0;
        private const int ArrayValue = 1;
        private const int ArrayCommaOrEnd = 2;

        private readonly ReadOnlySpan<char> _source;
        private readonly JsonOptions _options;
        private int[]? _frames;
        private int _frameCount;
        private int _index;
        private int _tokenStart;
        private int _tokenLength;
        private string? _decodedString;
        private bool _rootStarted;
        private bool _rootCompleted;
        private bool _disposed;

        public JsonTokenType TokenType { get; private set; }
        public int Offset => _tokenStart;
        public int Depth => _frameCount;
        public ReadOnlySpan<char> RawValueSpan => _tokenLength == 0
            ? ReadOnlySpan<char>.Empty
            : _source.Slice(_tokenStart, _tokenLength);

        public JsonReader(string json, JsonOptions? options = null)
            : this((json ?? throw new ArgumentNullException(nameof(json))).AsSpan(), options)
        {
        }

        public JsonReader(ReadOnlySpan<char> json, JsonOptions? options = null)
        {
            _source = json;
            _options = options ?? JsonOptions.Default;
            _options.Validate();
            _frames = ArrayPool<int>.Shared.Rent(Math.Min(16, _options.MaxDepth));
            _frameCount = 0;
            _index = 0;
            _tokenStart = 0;
            _tokenLength = 0;
            _decodedString = null;
            _rootStarted = false;
            _rootCompleted = false;
            _disposed = false;
            TokenType = JsonTokenType.None;
        }

        public bool Read()
        {
            ThrowIfDisposed();
            _decodedString = null;
            _tokenLength = 0;

            while (true)
            {
                SkipTrivia();

                if (_frameCount == 0)
                {
                    if (_rootCompleted)
                    {
                        if (_index != _source.Length)
                            Throw("Unexpected content after the root JSON value.");
                        TokenType = JsonTokenType.None;
                        return false;
                    }

                    if (_rootStarted)
                        Throw("The root JSON container was not completed.");
                    if (_index == _source.Length)
                        return false;

                    _rootStarted = true;
                    ReadValueToken(isRoot: true);
                    return true;
                }

                var frame = _frames![_frameCount - 1];
                var kind = frame & 0xF0;
                var state = frame & FrameStateMask;

                if (kind == FrameKindObject)
                {
                    switch (state)
                    {
                        case ObjectPropertyOrEnd:
                            if (TryReadContainerEnd('}', JsonTokenType.EndObject)) return true;
                            ReadPropertyName();
                            SetTopState(ObjectValue);
                            return true;
                        case ObjectProperty:
                            if (_options.AllowTrailingCommas && TryReadContainerEnd('}', JsonTokenType.EndObject))
                                return true;
                            ReadPropertyName();
                            SetTopState(ObjectValue);
                            return true;
                        case ObjectValue:
                            SetTopState(ObjectCommaOrEnd);
                            ReadValueToken(isRoot: false);
                            return true;
                        case ObjectCommaOrEnd:
                            if (TryReadContainerEnd('}', JsonTokenType.EndObject)) return true;
                            Require(',');
                            SetTopState(ObjectProperty);
                            continue;
                        default:
                            Throw("Invalid object parser state.");
                            return false;
                    }
                }

                switch (state)
                {
                    case ArrayValueOrEnd:
                        if (TryReadContainerEnd(']', JsonTokenType.EndArray)) return true;
                        SetTopState(ArrayCommaOrEnd);
                        ReadValueToken(isRoot: false);
                        return true;
                    case ArrayValue:
                        if (_options.AllowTrailingCommas && TryReadContainerEnd(']', JsonTokenType.EndArray))
                            return true;
                        SetTopState(ArrayCommaOrEnd);
                        ReadValueToken(isRoot: false);
                        return true;
                    case ArrayCommaOrEnd:
                        if (TryReadContainerEnd(']', JsonTokenType.EndArray)) return true;
                        Require(',');
                        SetTopState(ArrayValue);
                        continue;
                    default:
                        Throw("Invalid array parser state.");
                        return false;
                }
            }
        }

        public string GetString()
        {
            if (TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
                throw new InvalidOperationException($"Current token is {TokenType}, not a string.");
            return _decodedString ?? RawValueSpan.ToString();
        }

        /// <summary>Compares a string token without allocating when the source token contains no escapes.</summary>
        public bool ValueTextEquals(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
                throw new InvalidOperationException($"Current token is {TokenType}, not a string.");
            return _decodedString != null
                ? string.Equals(_decodedString, value, StringComparison.Ordinal)
                : RawValueSpan.SequenceEqual(value.AsSpan());
        }

        public string GetRawNumber()
        {
            EnsureNumber();
            return RawValueSpan.ToString();
        }

        public bool TryGetInt64(out long value)
        {
            EnsureNumber();
            return long.TryParse(RawValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public long GetInt64() => TryGetInt64(out var value)
            ? value
            : throw NumberConversionError(nameof(Int64));

        public bool TryGetUInt64(out ulong value)
        {
            EnsureNumber();
            return ulong.TryParse(RawValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public ulong GetUInt64() => TryGetUInt64(out var value)
            ? value
            : throw NumberConversionError(nameof(UInt64));

        public bool TryGetDecimal(out decimal value)
        {
            EnsureNumber();
            return decimal.TryParse(RawValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public decimal GetDecimal() => TryGetDecimal(out var value)
            ? value
            : throw NumberConversionError(nameof(Decimal));

        public bool TryGetDouble(out double value)
        {
            EnsureNumber();
            if (!double.TryParse(RawValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public double GetDouble() => TryGetDouble(out var value)
            ? value
            : throw NumberConversionError(nameof(Double));

        public bool GetBoolean() => TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            _ => throw new InvalidOperationException($"Current token is {TokenType}, not a boolean.")
        };

        public void SkipCurrentValue()
        {
            if (TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray)) return;
            var targetDepth = _frameCount - 1;
            while (Read())
            {
                if (_frameCount == targetDepth && TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                    return;
            }
            Throw("Unexpected end of input while skipping a JSON value.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_frames != null)
            {
                ArrayPool<int>.Shared.Return(_frames, clearArray: false);
                _frames = null;
            }
            _disposed = true;
        }

        private void ReadPropertyName()
        {
            SkipTrivia();
            if (_index >= _source.Length || _source[_index] != '"')
                Throw("Expected a JSON object property name.");
            ReadStringToken(JsonTokenType.PropertyName);
            SkipTrivia();
            Require(':');
        }

        private void ReadValueToken(bool isRoot)
        {
            SkipTrivia();
            if (_index >= _source.Length) Throw("Expected a JSON value.");

            var current = _source[_index];
            switch (current)
            {
                case '{':
                    BeginContainer(FrameKindObject | ObjectPropertyOrEnd, JsonTokenType.StartObject);
                    return;
                case '[':
                    BeginContainer(FrameKindArray | ArrayValueOrEnd, JsonTokenType.StartArray);
                    return;
                case '"':
                    ReadStringToken(JsonTokenType.String);
                    CompleteScalarRoot(isRoot);
                    return;
                case 't':
                    ReadLiteral("true", JsonTokenType.True);
                    CompleteScalarRoot(isRoot);
                    return;
                case 'f':
                    ReadLiteral("false", JsonTokenType.False);
                    CompleteScalarRoot(isRoot);
                    return;
                case 'n':
                    ReadLiteral("null", JsonTokenType.Null);
                    CompleteScalarRoot(isRoot);
                    return;
                default:
                    if (current == '-' || current is >= '0' and <= '9')
                    {
                        ReadNumberToken();
                        CompleteScalarRoot(isRoot);
                        return;
                    }
                    Throw($"Unexpected character '{current}' while reading a JSON value.");
                    return;
            }
        }

        private void ReadStringToken(JsonTokenType tokenType)
        {
            _index++;
            var contentStart = _index;
            var segmentStart = contentStart;
            char[]? buffer = null;
            var written = 0;

            try
            {
                while (_index < _source.Length)
                {
                    var current = _source[_index++];
                    if (current == '"')
                    {
                        _tokenStart = contentStart;
                        _tokenLength = _index - contentStart - 1;
                        if (buffer != null)
                        {
                            CopySegment(buffer, ref written, segmentStart, _index - 1);
                            _decodedString = new string(buffer, 0, written);
                        }
                        TokenType = tokenType;
                        return;
                    }

                    if (current < 0x20)
                        Throw("Unescaped control character in JSON string.");
                    if (current != '\\')
                    {
                        if (char.IsHighSurrogate(current))
                        {
                            if (_index >= _source.Length || !char.IsLowSurrogate(_source[_index]))
                                Throw("String contains an unpaired high surrogate.");
                            _index++;
                        }
                        else if (char.IsLowSurrogate(current))
                            Throw("String contains an unpaired low surrogate.");
                        continue;
                    }

                    buffer ??= ArrayPool<char>.Shared.Rent(Math.Max(32, _source.Length - contentStart));
                    CopySegment(buffer, ref written, segmentStart, _index - 1);
                    if (_index >= _source.Length) Throw("Unterminated JSON escape sequence.");
                    var escape = _source[_index++];
                    switch (escape)
                    {
                        case '"': buffer[written++] = '"'; break;
                        case '\\': buffer[written++] = '\\'; break;
                        case '/': buffer[written++] = '/'; break;
                        case 'b': buffer[written++] = '\b'; break;
                        case 'f': buffer[written++] = '\f'; break;
                        case 'n': buffer[written++] = '\n'; break;
                        case 'r': buffer[written++] = '\r'; break;
                        case 't': buffer[written++] = '\t'; break;
                        case 'u':
                            var first = ReadHexCodeUnit();
                            if (char.IsHighSurrogate(first))
                            {
                                if (_index + 1 >= _source.Length || _source[_index] != '\\' ||
                                    _source[_index + 1] != 'u')
                                    Throw("High surrogate must be followed by a low surrogate escape.");
                                _index += 2;
                                var second = ReadHexCodeUnit();
                                if (!char.IsLowSurrogate(second))
                                    Throw("High surrogate must be followed by a low surrogate escape.");
                                buffer[written++] = first;
                                buffer[written++] = second;
                            }
                            else
                            {
                                if (char.IsLowSurrogate(first))
                                    Throw("Unexpected low surrogate in JSON string.");
                                buffer[written++] = first;
                            }
                            break;
                        default:
                            Throw($"Invalid JSON escape character '{escape}'.");
                            break;
                    }
                    segmentStart = _index;
                }
                Throw("Unterminated JSON string.");
            }
            finally
            {
                if (buffer != null) ArrayPool<char>.Shared.Return(buffer, clearArray: false);
            }
        }

        private void CopySegment(char[] destination, ref int written, int start, int end)
        {
            var length = end - start;
            if (length <= 0) return;
            _source.Slice(start, length).CopyTo(destination.AsSpan(written));
            written += length;
        }

        private char ReadHexCodeUnit()
        {
            if (_index + 4 > _source.Length) Throw("Incomplete Unicode escape sequence.");
            var value = 0;
            for (var count = 0; count < 4; count++)
            {
                var current = _source[_index++];
                value <<= 4;
                value += current switch
                {
                    >= '0' and <= '9' => current - '0',
                    >= 'a' and <= 'f' => current - 'a' + 10,
                    >= 'A' and <= 'F' => current - 'A' + 10,
                    _ => InvalidHex(current)
                };
            }
            return (char)value;
        }

        private int InvalidHex(char current)
        {
            Throw($"Invalid hexadecimal character '{current}' in Unicode escape.");
            return 0;
        }

        private void ReadNumberToken()
        {
            var start = _index;
            if (_source[_index] == '-')
            {
                _index++;
                if (_index == _source.Length) Throw("Incomplete JSON number.");
            }

            if (_source[_index] == '0')
            {
                _index++;
                if (_index < _source.Length && IsDigit(_source[_index]))
                    Throw("Leading zeroes are not allowed in JSON numbers.");
            }
            else
            {
                if (!IsNonZeroDigit(_source[_index])) Throw("Invalid JSON number.");
                while (_index < _source.Length && IsDigit(_source[_index])) _index++;
            }

            if (_index < _source.Length && _source[_index] == '.')
            {
                _index++;
                if (_index == _source.Length || !IsDigit(_source[_index]))
                    Throw("A JSON fraction requires at least one digit.");
                while (_index < _source.Length && IsDigit(_source[_index])) _index++;
            }

            if (_index < _source.Length && _source[_index] is 'e' or 'E')
            {
                _index++;
                if (_index < _source.Length && _source[_index] is '+' or '-') _index++;
                if (_index == _source.Length || !IsDigit(_source[_index]))
                    Throw("A JSON exponent requires at least one digit.");
                while (_index < _source.Length && IsDigit(_source[_index])) _index++;
            }

            _tokenStart = start;
            _tokenLength = _index - start;
            TokenType = JsonTokenType.Number;
        }

        private void ReadLiteral(string literal, JsonTokenType tokenType)
        {
            var start = _index;
            if (_source.Length - _index < literal.Length ||
                !_source.Slice(_index, literal.Length).SequenceEqual(literal.AsSpan()))
                Throw($"Invalid JSON literal; expected '{literal}'.");
            _index += literal.Length;
            _tokenStart = start;
            _tokenLength = literal.Length;
            TokenType = tokenType;
        }

        private void BeginContainer(int frame, JsonTokenType tokenType)
        {
            _tokenStart = _index;
            _tokenLength = 1;
            _index++;
            PushFrame(frame);
            TokenType = tokenType;
        }

        private bool TryReadContainerEnd(char expected, JsonTokenType tokenType)
        {
            SkipTrivia();
            if (_index >= _source.Length || _source[_index] != expected) return false;
            _tokenStart = _index;
            _tokenLength = 1;
            _index++;
            _frameCount--;
            if (_frameCount == 0) _rootCompleted = true;
            TokenType = tokenType;
            return true;
        }

        private void PushFrame(int frame)
        {
            if (_frameCount >= _options.MaxDepth) Throw("Maximum JSON depth exceeded.");
            if (_frames == null) throw new ObjectDisposedException(nameof(JsonReader));
            if (_frameCount == _frames.Length)
            {
                var replacement = ArrayPool<int>.Shared.Rent(Math.Min(_options.MaxDepth, _frames.Length * 2));
                Array.Copy(_frames, replacement, _frames.Length);
                ArrayPool<int>.Shared.Return(_frames, clearArray: false);
                _frames = replacement;
            }
            _frames[_frameCount++] = frame;
        }

        private void SetTopState(int state)
        {
            var frame = _frames![_frameCount - 1];
            _frames[_frameCount - 1] = (frame & 0xF0) | state;
        }

        private void CompleteScalarRoot(bool isRoot)
        {
            if (isRoot) _rootCompleted = true;
        }

        private void SkipTrivia()
        {
            while (_index < _source.Length)
            {
                var current = _source[_index];
                if (current is ' ' or '\t' or '\r' or '\n')
                {
                    _index++;
                    continue;
                }

                if (!_options.AllowComments || current != '/' || _index + 1 >= _source.Length) return;
                var next = _source[_index + 1];
                if (next == '/')
                {
                    _index += 2;
                    while (_index < _source.Length && _source[_index] is not ('\r' or '\n')) _index++;
                    continue;
                }
                if (next == '*')
                {
                    _index += 2;
                    while (_index + 1 < _source.Length &&
                           !(_source[_index] == '*' && _source[_index + 1] == '/')) _index++;
                    if (_index + 1 >= _source.Length) Throw("Unterminated JSON block comment.");
                    _index += 2;
                    continue;
                }
                return;
            }
        }

        private void Require(char expected)
        {
            SkipTrivia();
            if (_index >= _source.Length || _source[_index] != expected)
                Throw($"Expected '{expected}'.");
            _index++;
        }

        private void EnsureNumber()
        {
            if (TokenType != JsonTokenType.Number)
                throw new InvalidOperationException($"Current token is {TokenType}, not a number.");
        }

        private JsonException NumberConversionError(string target) =>
            CreateException($"JSON number '{RawValueSpan.ToString()}' cannot be represented as {target}.");

        private JsonException CreateException(string message)
        {
            var line = 1;
            var column = 1;
            var limit = Math.Min(_index, _source.Length);
            for (var i = 0; i < limit; i++)
            {
                if (_source[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else column++;
            }
            return new JsonException(message, _index, line, column);
        }

        private void Throw(string message) => throw CreateException(message);

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JsonReader));
        }

        private static bool IsDigit(char value) => value is >= '0' and <= '9';
        private static bool IsNonZeroDigit(char value) => value is >= '1' and <= '9';
    }
}
