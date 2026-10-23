#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace YuzeToolkit.Json.Tests
{
    public sealed class JsonSerializationTests
    {
        public enum Mode : ushort
        {
            None,
            Ready = 65535
        }

        public sealed class Model
        {
            [JsonName("id")] public int Id;
            public string Name { get; set; } = string.Empty;
            public List<int> Values { get; set; } = new();
            public Model? Child { get; set; }
            public Dictionary<string, decimal> Scores { get; set; } = new();
            public Mode CurrentMode { get; set; }
            [JsonIgnore] public string Ignored { get; set; } = "ignored";
        }

        private sealed class RequiredModel
        {
            [JsonRequired]
            public string Name { get; set; } = string.Empty;
        }

        private sealed class GeneratedModelConverter : JsonConverter<Model>
        {
            public static GeneratedModelConverter Instance { get; } = new();

            public override Model? Read(ref JsonReader reader, JsonSerializationContext context)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected Model object.");
                var value = new Model();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    if (!reader.Read()) throw new JsonException("Model property is missing a value.");
                    switch (name)
                    {
                        case "id": value.Id = context.ReadValue<int>(ref reader); break;
                        case "Name": value.Name = context.ReadValue<string>(ref reader) ?? string.Empty; break;
                        case "Values": value.Values = context.ReadValue<List<int>>(ref reader) ?? new List<int>(); break;
                        case "Child": value.Child = Read(ref reader, context); break;
                        case "Scores": value.Scores = context.ReadValue<Dictionary<string, decimal>>(ref reader) ?? new Dictionary<string, decimal>(); break;
                        case "CurrentMode": value.CurrentMode = context.ReadValue<Mode>(ref reader); break;
                        default: reader.SkipCurrentValue(); break;
                    }
                }
                return value;
            }

            public override void Write(JsonWriter writer, Model? value, JsonSerializationContext context)
            {
                if (value == null) { writer.WriteNullValue(); return; }
                writer.WriteStartObject();
                writer.WritePropertyName("id"); writer.WriteNumberValue(value.Id);
                writer.WritePropertyName("Name"); writer.WriteStringValue(value.Name);
                writer.WritePropertyName("Values"); context.WriteValue(writer, value.Values);
                writer.WritePropertyName("Child"); Write(writer, value.Child, context);
                writer.WritePropertyName("Scores"); context.WriteValue(writer, value.Scores);
                writer.WritePropertyName("CurrentMode"); context.WriteValue(writer, value.CurrentMode);
                writer.WriteEndObject();
            }
        }

        private sealed class ConstantModelConverter : JsonConverter<Model>
        {
            private readonly int _id;
            public ConstantModelConverter(int id) => _id = id;
            public override Model Read(ref JsonReader reader, JsonSerializationContext context) { reader.SkipCurrentValue(); return new Model { Id = _id }; }
            public override void Write(JsonWriter writer, Model? value, JsonSerializationContext context) { writer.WriteStartObject(); writer.WritePropertyName("constant"); writer.WriteNumberValue(_id); writer.WriteEndObject(); }
        }

        [Test]
        public void ReflectionSerializer_RoundTripsObjectsCollectionsAndNulls()
        {
            var source = new Model
            {
                Id = 7,
                Name = "model",
                Values = new List<int> { 1, 2, 3 },
                Child = new Model { Id = 8 },
                Scores = new Dictionary<string, decimal> { ["exact"] = 1.25m },
                CurrentMode = Mode.Ready
            };
            var json = YuzeJson.Serialize(source);
            var result = YuzeJson.Deserialize<Model>(json)!;
            Assert.That(result.Id, Is.EqualTo(7));
            Assert.That(result.Name, Is.EqualTo("model"));
            Assert.That(result.Values, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(result.Child!.Id, Is.EqualTo(8));
            Assert.That(result.Scores["exact"], Is.EqualTo(1.25m));
            Assert.That(result.CurrentMode, Is.EqualTo(Mode.Ready));
            Assert.That(json, Does.Not.Contain("Ignored"));
        }

        [Test]
        public void GeneratedStyleConverter_IsEquivalentToReflectionMeaning()
        {
            var source = new Model { Id = 2, Name = "generated", Values = new List<int> { 4, 5 } };
            var reflectionJson = YuzeJson.Serialize(source);
            var typeInfo = JsonTypeInfo<Model>.From(GeneratedModelConverter.Instance);
            var generatedJson = YuzeJson.Serialize(source, typeInfo);
            Assert.That(JsonData.Parse(generatedJson), Is.EqualTo(JsonData.Parse(reflectionJson)));
            var result = YuzeJson.Deserialize(generatedJson, typeInfo)!;
            Assert.That(result.Id, Is.EqualTo(source.Id));
            Assert.That(result.Values, Is.EqualTo(source.Values));
        }

        [Test]
        public void ConverterResolution_UsesPerCallThenOptionsThenStaticThenReflection()
        {
            JsonConverterRegistry.Register(new ConstantModelConverter(10));
            try
            {
                Assert.That(YuzeJson.Deserialize<Model>("{}")!.Id, Is.EqualTo(10));
                var options = new JsonOptions
                {
                    Resolver = new JsonTypeInfoResolver().Add(new ConstantModelConverter(20))
                };
                Assert.That(YuzeJson.Deserialize<Model>("{}", options)!.Id, Is.EqualTo(20));
                Assert.That(YuzeJson.Deserialize("{}", new ConstantModelConverter(30))!.Id, Is.EqualTo(30));
            }
            finally { JsonConverterRegistry.Unregister<Model>(); }
        }

        [Test]
        public void RequiredAndUnknownMemberPolicies_AreEnforced()
        {
            var strict = new JsonOptions { UnknownMemberHandling = JsonUnknownMemberHandling.Error };
            Assert.Throws<JsonException>(() => YuzeJson.Deserialize<Model>("{\"unknown\":1}", strict));
            Assert.Throws<JsonException>(() => YuzeJson.Deserialize<RequiredModel>("{}"));
            Assert.That(YuzeJson.Deserialize<RequiredModel>("{\"Name\":\"present\"}")!.Name,
                Is.EqualTo("present"));
        }

        [Test]
        public void FloatingPointOverflow_IsRejected()
        {
            Assert.Throws<JsonException>(() => YuzeJson.Deserialize<float>("1e39"));
            Assert.Throws<JsonException>(() => YuzeJson.Deserialize<double>("1e400"));
        }
    }
}
