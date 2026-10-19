using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace BuildKit.Tests
{
    public class ConfigTests
    {
        const string Json = @"{
  ""productName"": ""Game"",
  ""version"": ""1.2.0"",
  ""defines"": [""A"", ""B""],
  ""android"": { ""packageName"": ""com.x.game"", ""format"": ""aab"", ""playTrack"": ""internal"" },
  ""defaultProfile"": ""release"",
  ""profiles"": {
    ""dev"": { ""development"": true, ""defines"": [""DEV""], ""android"": { ""format"": ""apk"" } },
    ""release"": { ""git"": { ""requireClean"": true } }
  }
}";

        [Test]
        public void UsesDefaultProfile()
        {
            var c = ConfigLoader.Resolve(Json, null);
            Assert.AreEqual("release", c.Profile);
            Assert.IsTrue(c.git.requireClean);
            Assert.IsFalse(c.development);
        }

        [Test]
        public void ProfileDeepMergesObjectsAndReplacesArrays()
        {
            var c = ConfigLoader.Resolve(Json, "dev");
            Assert.AreEqual("dev", c.Profile);
            Assert.IsTrue(c.development);
            Assert.AreEqual("apk", c.android.format);                 // 被 profile 覆盖
            Assert.AreEqual("com.x.game", c.android.packageName);     // 没写的字段保留顶层的
            Assert.AreEqual("internal", c.android.playTrack);
            CollectionAssert.AreEqual(new[] { "DEV" }, c.defines);    // 数组整体替换
            Assert.IsFalse(c.git.requireClean);
        }

        [Test]
        public void MissingFieldsKeepCodeDefaults()
        {
            var c = ConfigLoader.Resolve("{\"productName\":\"G\"}", null);
            Assert.AreEqual("", c.Profile);
            Assert.AreEqual("aab", c.android.format);
            Assert.AreEqual(36, c.android.playMinTargetSdk);
            Assert.IsTrue(c.ios.uploadToTestFlight);
            Assert.AreEqual("app-store", c.ios.exportMethod);
            Assert.IsTrue(c.embedBuildInfo);
        }

        [Test]
        public void UnknownProfileIsAnError()
        {
            var e = Assert.Throws<BuildKitException>(() => ConfigLoader.Resolve(Json, "staging"));
            StringAssert.Contains("staging", e.Message);
            StringAssert.Contains("dev / release", e.Message);
        }

        [Test]
        public void TypoProducesWarningWithSuggestion()
        {
            var c = ConfigLoader.Resolve("{\"android\":{\"pakageName\":\"com.a.b\"}}", null);
            var w = c.Warnings.Single();
            StringAssert.Contains("android.pakageName", w);
            StringAssert.Contains("android.packageName", w);
        }

        [Test]
        public void TypoInsideProfileIsReported()
        {
            var c = ConfigLoader.Resolve("{\"profiles\":{\"dev\":{\"developement\":true}}}", null);
            StringAssert.Contains("profiles.dev.developement", c.Warnings.Single());
        }

        [TestCase("{\"development\":\"true\"}", "development")]
        [TestCase("{\"android\":{\"minSdk\":\"24\"}}", "android.minSdk")]
        [TestCase("{\"defines\":\"A\"}", "defines")]
        [TestCase("{\"ios\":{\"privacy\":[{\"key\":1}]}}", "ios.privacy[0].key")]
        public void WrongTypeIsAnError(string json, string path)
        {
            var e = Assert.Throws<BuildKitException>(() => ConfigLoader.Resolve(json, null));
            StringAssert.Contains(path, e.Message);
        }

        [Test]
        public void SyntaxErrorMentionsLine()
        {
            var e = Assert.Throws<BuildKitException>(() => ConfigLoader.Resolve("{\n\"a\": 1,\n}", null));
            StringAssert.Contains("第 3 行", e.Message);
        }

        [Test]
        public void ArtifactNameIncludesProfile()
        {
            var c = ConfigLoader.Resolve(Json, "dev");
            Assert.AreEqual("Game_1.2.0_123_dev", Builder.ArtifactName(c, 123, true));
            Assert.AreEqual("1.2.0_123_dev", Builder.ArtifactName(c, 123, false));
        }

        [Test]
        public void ParsesArchitecturesCaseInsensitively()
        {
            Assert.AreEqual(AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7, Builder.ParseArch("arm64, ARMV7"));
            Assert.AreEqual(AndroidArchitecture.ARM64, Builder.ParseArch(""));
        }

        [Test]
        public void BuildNumberIsMonotonicAndFitsVersionCode()
        {
            var n = BuildNumbers.Timestamp();
            Assert.Greater(n, 1_000_000);          // 2026 年时约 140 万
            Assert.Less(n, 2_100_000_000);         // Google Play versionCode 上限
        }
    }
}
