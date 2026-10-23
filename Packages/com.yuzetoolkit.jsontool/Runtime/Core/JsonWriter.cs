#nullable enable
using System;
using System.Buffers;
using System.Globalization;

namespace YuzeToolkit.Json
{
    /// <summary>Pooled JSON text writer shared by reflection, generated converters and DOM output.</summary>
    public sealed class JsonWriter : IDisposable
    {
        private enum ContainerKind : byte
        {
            Object,
            Array
        }

        private struct Frame
        {
            public ContainerKind Kind;
            public int Count;
            public bool ExpectingValue;
        }

        private readonly JsonOptions _options;
        private char[]? _buffer;
        private Frame[] _frames;
        private int _length;
        private int _frameCount;
        private bool _rootWritten;
        private bool _disposed;

        public int Length => _length;

        public JsonWriter(JsonOptions? options = null, int initialCapacity = 256)
        {
            if (initialCapacity < 16) initialCapacity = 16;
            _options = options ?? JsonOptions.Default;
            _options.Validate();
            _buffer = ArrayPool<char>.Shared.Rent(initialCapacity);
            _frames = new Frame[Math.Min(8, _options.MaxDepth)];
        }

        public void WriteStartObject()
        {
            BeforeValue();
            Append('{');
            Push(ContainerKind.Object);
        }

        public void WriteEndObject()
        {
            EnsureContainer(ContainerKind.Object);
            ref var frame = ref _frames[_frameCount - 1];
            if (frame.ExpectingValue) throw new InvalidOperationException("A JSON property is missing its value.");
            if (_options.WriteIndented && frame.Count > 0)
            {
                AppendNewLine();
                AppendIndent(_frameCount - 1);
            }
            _frameCount--;
            Append('}');
            AfterValue();
        }

        public void WriteStartArray()
        {
            BeforeValue();
            Append('[');
            Push(ContainerKind.Array);
        }

        public void WriteEndArray()
        {
            EnsureContainer(ContainerKind.Array);
            ref var frame = ref _frames[_frameCount - 1];
            if (_options.WriteIndented && frame.Count > 0)
            {
                AppendNewLine();
                AppendIndent(_frameCount - 1);
            }
            _frameCount--;
            Append(']');
            AfterValue();
        }

        public void WritePropertyName(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            EnsureContainer(ContainerKind.Object);
            ref var frame = ref _frames[_frameCount - 1];
            if (frame.ExpectingValue) throw new InvalidOperationException("The previous property is missing its value.");
            if (frame.Count > 0) Append(',');
            WriteLayoutBeforeItem();
            WriteEscapedString(name);
            Append(':');
            if (_options.WriteIndented) Append(' ');
            frame.ExpectingValue = true;
        }

        public void WriteNullValue()
        {
            BeforeValue();
            Append("null".AsSpan());
            AfterValue();
        }

        public void WriteBooleanValue(bool value)
        {
            BeforeValue();
            Append(value ? "true".AsSpan() : "false".AsSpan());
            AfterValue();
        }

        public void WriteStringValue(string? value)
        {
            if (value == null)
            {
                WriteNullValue();
                return;
            }
            BeforeValue();
            WriteEscapedString(value);
            AfterValue();
        }

        public void WriteNumberValue(long value)
        {
            Span<char> buffer = stackalloc char[32];
            if (!value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Failed to format an Int64 JSON number.");
            WriteFormattedNumber(buffer.Slice(0, written));
        }

        public void WriteNumberValue(ulong value)
        {
            Span<char> buffer = stackalloc char[32];
            if (!value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Failed to format a UInt64 JSON number.");
            WriteFormattedNumber(buffer.Slice(0, written));
        }

        public void WriteNumberValue(int value)
        {
            Span<char> buffer = stackalloc char[16];
            if (!value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Failed to format an Int32 JSON number.");
            WriteFormattedNumber(buffer.Slice(0, written));
        }

        public void WriteNumberValue(uint value)
        {
            Span<char> buffer = stackalloc char[16];
            if (!value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Failed to format a UInt32 JSON number.");
            WriteFormattedNumber(buffer.Slice(0, written));
        }

        public void WriteNumberValue(short value) => WriteNumberValue((int)value);
        public void WriteNumberValue(ushort value) => WriteNumberValue((uint)value);
        public void WriteNumberValue(byte value) => WriteNumberValue((uint)value);
        public void WriteNumberValue(sbyte value) => WriteNumberValue((int)value);

        public void WriteNumberValue(decimal value)
        {
            Span<char> buffer = stackalloc char[32];
            if (!value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Failed to format a Decimal JSON number.");
            WriteFormattedNumber(buffer.Slice(0, written));
        }

        public void WriteNumberValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new JsonException("NaN and Infinity are not valid JSON numbers.");
            Span<char> buffer = stackalloc char[32];
            if (!value.TryFormat(buffer, out var written, "R".AsSpan(), CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Failed to format a Double JSON number.");
            WriteFormattedNumber(buffer.Slice(0, written));
        }

        public void WriteNumberValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new JsonException("NaN and Infinity are not valid JSON numbers.");
            Span<char> buffer = stackalloc char[24];
            if (!value.TryFormat(buffer, out var written, "R".AsSpan(), CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Failed to format a Single JSON number.");
            WriteFormattedNumber(buffer.Slice(0, written));
        }

        public void WriteRawNumberValue(ReadOnlySpan<char> value)
        {
            if (!JsonNumber.IsValid(value)) throw new JsonException($"'{value.ToString()}' is not a valid JSON number.");
            WriteValidatedRawNumberValue(value);
        }

        internal void WriteValidatedRawNumberValue(ReadOnlySpan<char> value)
        {
            BeforeValue();
            Append(value);
            AfterValue();
        }

        public override string ToString()
        {
            ThrowIfDisposed();
            if (!_rootWritten || _frameCount != 0)
                throw new InvalidOperationException("JSON writing is incomplete.");
            return new string(_buffer!, 0, _length);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_buffer != null)
            {
                ArrayPool<char>.Shared.Return(_buffer, clearArray: false);
                _buffer = null;
            }
            _disposed = true;
        }

        private void WriteFormattedNumber(ReadOnlySpan<char> value)
        {
            BeforeValue();
            Append(value);
            AfterValue();
        }

        private void WriteEscapedString(string value)
        {
            Append('"');
            var segmentStart = 0;
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                string? escape = current switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ when current < 0x20 => null,
                    _ => string.Empty
                };

                if (escape == string.Empty)
                {
                    if (char.IsHighSurrogate(current))
                    {
                        if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                            throw new JsonException("String contains an unpaired high surrogate.");
                        index++;
                    }
                    else if (char.IsLowSurrogate(current))
                        throw new JsonException("String contains an unpaired low surrogate.");
                    continue;
                }

                if (index > segmentStart) Append(value.AsSpan(segmentStart, index - segmentStart));
                if (current < 0x20 && escape == null)
                    AppendUnicodeEscape(current);
                else Append(escape!.AsSpan());
                segmentStart = index + 1;
            }
            if (segmentStart < value.Length) Append(value.AsSpan(segmentStart));
            Append('"');
        }

        private void AppendUnicodeEscape(char value)
        {
            Span<char> unicode = stackalloc char[6];
            unicode[0] = '\\';
            unicode[1] = 'u';
            var number = (int)value;
            for (var digit = 5; digit >= 2; digit--)
            {
                var nibble = number & 0xF;
                unicode[digit] = (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
                number >>= 4;
            }
            Append(unicode);
        }

        private void BeforeValue()
        {
            ThrowIfDisposed();
            if (_frameCount == 0)
            {
                if (_rootWritten) throw new InvalidOperationException("Only one root JSON value can be written.");
                return;
            }

            ref var frame = ref _frames[_frameCount - 1];
            if (frame.Kind == ContainerKind.Object)
            {
                if (!frame.ExpectingValue) throw new InvalidOperationException("A property name must be written first.");
                return;
            }

            if (frame.Count > 0) Append(',');
            WriteLayoutBeforeItem();
        }

        private void AfterValue()
        {
            if (_frameCount == 0)
            {
                _rootWritten = true;
                return;
            }
            ref var frame = ref _frames[_frameCount - 1];
            frame.Count++;
            if (frame.Kind == ContainerKind.Object) frame.ExpectingValue = false;
        }

        private void Push(ContainerKind kind)
        {
            if (_frameCount >= _options.MaxDepth) throw new JsonException("Maximum JSON depth exceeded.");
            if (_frameCount == _frames.Length)
                Array.Resize(ref _frames, Math.Min(_options.MaxDepth, _frames.Length * 2));
            _frames[_frameCount++] = new Frame { Kind = kind };
        }

        private void EnsureContainer(ContainerKind expected)
        {
            ThrowIfDisposed();
            if (_frameCount == 0 || _frames[_frameCount - 1].Kind != expected)
                throw new InvalidOperationException($"Current JSON container is not a {expected}.");
        }

        private void WriteLayoutBeforeItem()
        {
            if (!_options.WriteIndented) return;
            AppendNewLine();
            AppendIndent(_frameCount);
        }

        private void AppendIndent(int depth)
        {
            var count = depth * _options.IndentSize;
            EnsureCapacity(count);
            for (var index = 0; index < count; index++) _buffer![_length++] = ' ';
        }

        private void AppendNewLine() => Append('\n');

        private void Append(char value)
        {
            EnsureCapacity(1);
            _buffer![_length++] = value;
        }

        private void Append(ReadOnlySpan<char> value)
        {
            EnsureCapacity(value.Length);
            value.CopyTo(_buffer!.AsSpan(_length));
            _length += value.Length;
        }

        private void EnsureCapacity(int additional)
        {
            ThrowIfDisposed();
            if (_length + additional <= _buffer!.Length) return;
            var replacement = ArrayPool<char>.Shared.Rent(Math.Max(_length + additional, _buffer.Length * 2));
            _buffer.AsSpan(0, _length).CopyTo(replacement);
            ArrayPool<char>.Shared.Return(_buffer, clearArray: false);
            _buffer = replacement;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JsonWriter));
        }
    }
}
