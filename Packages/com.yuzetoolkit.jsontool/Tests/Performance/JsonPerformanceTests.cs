#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace YuzeToolkit.Json.PerformanceTests
{
    public sealed class JsonPerformanceTests
    {
        private static object? _result;

        private sealed class Model
        {
            public int Id;
            public string Name { get; set; } = string.Empty;
            public List<int> Values { get; set; } = new();
        }

        private sealed class ModelConverter : JsonConverter<Model>
        {
            public static ModelConverter Instance { get; } = new();

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
                        case "Id":
                            value.Id = checked((int)reader.GetInt64());
                            break;
                        case "Name":
                            value.Name = reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString();
                            break;
                        case "Values":
                            if (reader.TokenType == JsonTokenType.Null) { value.Values = new List<int>(); break; }
                            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected Values array.");
                            var values = new List<int>();
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                values.Add(checked((int)reader.GetInt64()));
                            value.Values = values;
                            break;
                        default: reader.SkipCurrentValue(); break;
                    }
                }
                return value;
            }

            public override void Write(JsonWriter writer, Model? value, JsonSerializationContext context)
            {
                if (value == null) { writer.WriteNullValue(); return; }
                writer.WriteStartObject();
                writer.WritePropertyName("Id"); writer.WriteNumberValue(value.Id);
                writer.WritePropertyName("Name"); writer.WriteStringValue(value.Name);
                writer.WritePropertyName("Values");
                writer.WriteStartArray();
                foreach (var item in value.Values) writer.WriteNumberValue(item);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }

        private static readonly Model Value = new()
        {
            Id = 42,
            Name = "performance",
            Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 }
        };

        private static readonly JsonTypeInfo<Model> GeneratedTypeInfo =
            JsonTypeInfo<Model>.From(ModelConverter.Instance);

        private static readonly string Json = YuzeJson.Serialize(Value, GeneratedTypeInfo);

        [Test, Performance]
        public void Serialize_ReflectionBaseline()
        {
            Measure.Method(() => _result = YuzeJson.Serialize(Value))
                .WarmupCount(10).MeasurementCount(20).IterationsPerMeasurement(100).Run();
        }

        [Test, Performance]
        public void Serialize_GeneratedStyleConverter()
        {
            Measure.Method(() => _result = YuzeJson.Serialize(Value, GeneratedTypeInfo))
                .WarmupCount(10).MeasurementCount(20).IterationsPerMeasurement(100).Run();
        }

        [Test, Performance]
        public void Deserialize_ReflectionBaseline()
        {
            Measure.Method(() => _result = YuzeJson.Deserialize<Model>(Json))
                .WarmupCount(10).MeasurementCount(20).IterationsPerMeasurement(100).Run();
        }

        [Test, Performance]
        public void Deserialize_GeneratedStyleConverter()
        {
            Measure.Method(() => _result = YuzeJson.Deserialize(Json, GeneratedTypeInfo))
                .WarmupCount(10).MeasurementCount(20).IterationsPerMeasurement(100).Run();
        }
    }
}
