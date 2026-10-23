#nullable enable
using NUnit.Framework;

namespace YuzeToolkit.Json.Tests
{
    public sealed class JsonDataTests
    {
        [Test]
        public void ParseMutateCloneAndRoundTrip_PreserveJsonMeaning()
        {
            var root = JsonData.Parse("{\"nullValue\":null,\"array\":[1,2],\"large\":18446744073709551615}");
            Assert.That(root["nullValue"].IsNull, Is.True);
            Assert.That(root["large"].AsUInt64(), Is.EqualTo(ulong.MaxValue));
            root["array"].Add("three");
            root["enabled"] = true;

            var clone = root.Clone();
            Assert.That(clone, Is.EqualTo(root));
            Assert.That(JsonData.Parse(root.ToJson()), Is.EqualTo(root));
        }

        [Test]
        public void Object_PreservesInsertionOrderAndSupportsReplacement()
        {
            var value = JsonData.NewObject();
            value["b"] = 1;
            value["a"] = 2;
            value["b"] = 3;
            Assert.That(value.ToJson(), Is.EqualTo("{\"b\":3,\"a\":2}"));
        }

        [Test]
        public void DuplicateProperties_FollowConfiguredPolicy()
        {
            Assert.That(YuzeJson.Parse("{\"a\":1,\"a\":2}")["a"].AsInt64(), Is.EqualTo(2));
            var options = new JsonOptions { DuplicatePropertyHandling = JsonDuplicatePropertyHandling.Error };
            Assert.Throws<JsonException>(() => YuzeJson.Parse("{\"a\":1,\"a\":2}", options));
        }

        [Test]
        public void NumericallyEqualValues_HaveEqualHashCodes()
        {
            var integer = JsonData.Parse("1");
            var decimalValue = JsonData.Parse("1.0");
            Assert.That(integer, Is.EqualTo(decimalValue));
            Assert.That(integer.GetHashCode(), Is.EqualTo(decimalValue.GetHashCode()));
        }
    }
}
