#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace YuzeToolkit.Json.Tests
{
    public sealed class JsonTestSuiteTests
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private static string CorpusDirectory => Path.GetFullPath(
            "Packages/com.yuzetoolkit.jsontool/Tests/External/JSONTestSuite/test_parsing");

        private static string[] Cases(char prefix) => Directory.GetFiles(CorpusDirectory, prefix + "_*.json")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();

        [Test]
        public void RequiredValidCorpus_IsAccepted()
        {
            foreach (var path in Cases('y'))
            {
                var json = StrictUtf8.GetString(File.ReadAllBytes(path));
                Assert.DoesNotThrow(() => YuzeJson.Parse(json), Path.GetFileName(path));
            }
        }

        [Test]
        public void RequiredInvalidCorpus_IsRejected()
        {
            foreach (var path in Cases('n'))
            {
                var rejected = false;
                try
                {
                    var json = StrictUtf8.GetString(File.ReadAllBytes(path));
                    YuzeJson.Parse(json);
                }
                catch (DecoderFallbackException) { rejected = true; }
                catch (JsonException) { rejected = true; }
                Assert.That(rejected, Is.True, Path.GetFileName(path));
            }
        }
    }
}
