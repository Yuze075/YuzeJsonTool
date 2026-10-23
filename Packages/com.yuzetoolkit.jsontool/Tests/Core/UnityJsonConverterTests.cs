#nullable enable
using NUnit.Framework;
using UnityEngine;
using YuzeToolkit.Json.Unity;

namespace YuzeToolkit.Json.Tests
{
    public sealed class UnityJsonConverterTests
    {
        [OneTimeSetUp]
        public void RegisterConverters() => UnityJsonConverters.RegisterDefaults();

        [Test]
        public void UnityValueConverters_RoundTripWithoutReflection()
        {
            var vector = new Vector3(1.25f, -2.5f, 3.75f);
            Assert.That(YuzeJson.Deserialize<Vector3>(YuzeJson.Serialize(vector)), Is.EqualTo(vector));

            var bounds = new Bounds(vector, new Vector3(10, 20, 30));
            Assert.That(YuzeJson.Deserialize<Bounds>(YuzeJson.Serialize(bounds)), Is.EqualTo(bounds));

            var color = new Color32(1, 2, 3, 4);
            Assert.That(YuzeJson.Deserialize<Color32>(YuzeJson.Serialize(color)), Is.EqualTo(color));
        }

        [Test]
        public void UnityResolver_CanBePassedWithoutGlobalRegistration()
        {
            var options = new JsonOptions { Resolver = UnityJsonTypeResolver.Instance };
            var source = new Rect(1, 2, 3, 4);
            Assert.That(YuzeJson.Deserialize<Rect>(YuzeJson.Serialize(source, options), options), Is.EqualTo(source));
        }

        [Test]
        public void UnityConverters_RespectMemberPolicies()
        {
            var caseInsensitive = new JsonOptions
            {
                Resolver = UnityJsonTypeResolver.Instance,
                PropertyNameCaseInsensitive = true
            };
            Assert.That(YuzeJson.Deserialize<Vector2>("{\"X\":1,\"Y\":2}", caseInsensitive),
                Is.EqualTo(new Vector2(1, 2)));

            var strict = new JsonOptions
            {
                Resolver = UnityJsonTypeResolver.Instance,
                UnknownMemberHandling = JsonUnknownMemberHandling.Error,
                DuplicatePropertyHandling = JsonDuplicatePropertyHandling.Error
            };
            Assert.Throws<JsonException>(() => YuzeJson.Deserialize<Bounds>(
                "{\"center\":{\"x\":0,\"y\":0,\"z\":0},\"extra\":1}", strict));
            Assert.Throws<JsonException>(() => YuzeJson.Deserialize<BoundsInt>(
                "{\"size\":{\"x\":1,\"y\":1,\"z\":1},\"size\":{\"x\":2,\"y\":2,\"z\":2}}", strict));
        }
    }
}
