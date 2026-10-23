#nullable enable
using System;
using NUnit.Framework;

namespace YuzeToolkit.Json.Tests
{
    public sealed class JsonReaderWriterTests
    {
        [Test]
        public void Reader_EmitsStrictStructuredTokens()
        {
            var reader = new JsonReader("{\"items\":[1,true,null,\"a\\nb\"]}");
            try
            {
                Assert.That(reader.Read(), Is.True); Assert.That(reader.TokenType, Is.EqualTo(JsonTokenType.StartObject));
                Assert.That(reader.Read(), Is.True); Assert.That(reader.TokenType, Is.EqualTo(JsonTokenType.PropertyName)); Assert.That(reader.GetString(), Is.EqualTo("items"));
                Assert.That(reader.Read(), Is.True); Assert.That(reader.TokenType, Is.EqualTo(JsonTokenType.StartArray));
                Assert.That(reader.Read(), Is.True); Assert.That(reader.GetInt64(), Is.EqualTo(1));
                Assert.That(reader.Read(), Is.True); Assert.That(reader.GetBoolean(), Is.True);
                Assert.That(reader.Read(), Is.True); Assert.That(reader.TokenType, Is.EqualTo(JsonTokenType.Null));
                Assert.That(reader.Read(), Is.True); Assert.That(reader.GetString(), Is.EqualTo("a\nb"));
                Assert.That(reader.Read(), Is.True); Assert.That(reader.TokenType, Is.EqualTo(JsonTokenType.EndArray));
                Assert.That(reader.Read(), Is.True); Assert.That(reader.TokenType, Is.EqualTo(JsonTokenType.EndObject));
                Assert.That(reader.Read(), Is.False);
            }
            finally { reader.Dispose(); }
        }

        [TestCase("{\"a\":1,}")]
        [TestCase("[1 2]")]
        [TestCase("[01]")]
        [TestCase("[1e]")]
        [TestCase("{a:1}")]
        [TestCase("true false")]
        public void StrictParser_RejectsInvalidJson(string json) => Assert.Throws<JsonException>(() => YuzeJson.Parse(json));

        [Test]
        public void RelaxedOptions_AllowCommentsAndTrailingCommas()
        {
            var options = new JsonOptions { AllowComments = true, AllowTrailingCommas = true };
            var value = YuzeJson.Parse("{/* note */\"a\":1,}", options);
            Assert.That(value["a"].AsInt64(), Is.EqualTo(1));
        }

        [Test]
        public void SpanEntryPoint_ParsesWithoutCreatingAnInputString()
        {
            ReadOnlySpan<char> json = "{\"value\":42}".AsSpan();
            Assert.That(YuzeJson.Parse(json)["value"].AsInt64(), Is.EqualTo(42));
        }

        [Test]
        public void Reader_RejectsRawUnpairedSurrogates()
        {
            var high = new string((char)0xD800, 1);
            var low = new string((char)0xDC00, 1);
            Assert.Throws<JsonException>(() => YuzeJson.Parse("\"" + high + "\""));
            Assert.Throws<JsonException>(() => YuzeJson.Parse("\"" + low + "\""));
        }

        [Test]
        public void Writer_ProducesValidIndentedJson()
        {
            var options = new JsonOptions { WriteIndented = true };
            using var writer = new JsonWriter(options);
            writer.WriteStartObject();
            writer.WritePropertyName("text"); writer.WriteStringValue("a\nb");
            writer.WritePropertyName("value"); writer.WriteNumberValue(2.5);
            writer.WriteEndObject();
            var json = writer.ToString();
            Assert.That(json, Does.Contain("\n"));
            Assert.That(YuzeJson.Parse(json)["text"].AsString(), Is.EqualTo("a\nb"));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Writer_RejectsNonJsonFloatingPointValues(double value)
        {
            using var writer = new JsonWriter();
            Assert.Throws<JsonException>(() => writer.WriteNumberValue(value));
        }
    }
}
