using System.Collections.Generic;
using NUnit.Framework;

namespace BuildKit.Tests
{
    public class MiniJsonTests
    {
        [Test]
        public void ParsesAllValueTypes()
        {
            var d = (Dictionary<string, object>)MiniJson.Parse("{\"s\":\"a\\u4e2d\\n\",\"i\":36,\"f\":1.5,\"b\":true,\"n\":null,\"a\":[1,\"x\"],\"o\":{}}");
            Assert.AreEqual("a中\n", d["s"]);
            Assert.AreEqual(36L, d["i"]);
            Assert.AreEqual(1.5, d["f"]);
            Assert.AreEqual(true, d["b"]);
            Assert.IsNull(d["n"]);
            CollectionAssert.AreEqual(new object[] { 1L, "x" }, (List<object>)d["a"]);
            Assert.IsInstanceOf<Dictionary<string, object>>(d["o"]);
        }

        [Test]
        public void IgnoresBom()
        {
            var d = (Dictionary<string, object>)MiniJson.Parse("\uFEFF{\"a\":1}");
            Assert.AreEqual(1L, d["a"]);
        }

        [Test]
        public void KeepsKeyOrder()
        {
            var d = (Dictionary<string, object>)MiniJson.Parse("{\"z\":1,\"a\":2,\"m\":3}");
            CollectionAssert.AreEqual(new[] { "z", "a", "m" }, d.Keys);
        }

        [TestCase("{\n\"a\": 1,\n}", 3, "最后一项后面不能有逗号")]
        [TestCase("{\n\"a\": 1\n\"b\": 2\n}", 3, "漏了逗号")]
        [TestCase("{\n// note\n\"a\": 1\n}", 2, "注释")]
        [TestCase("{\"p\": \"C:\\Users\"}", 1, "无效的转义")]
        [TestCase("{\"a\":1,\"a\":2}", 1, "重复")]
        public void ReportsErrorsWithLineNumber(string json, int line, string fragment)
        {
            var e = Assert.Throws<MiniJson.ParseException>(() => MiniJson.Parse(json));
            Assert.AreEqual(line, e.Line, e.Message);
            StringAssert.Contains(fragment, e.Message);
        }

        [Test]
        public void RoundTrips()
        {
            const string json = "{\"s\":\"q\\\"\\\\\\t\",\"n\":[1,2.5,true,null],\"o\":{\"k\":\"中文\"}}";
            Assert.AreEqual(json, MiniJson.Serialize(MiniJson.Parse(json)));
            var pretty = MiniJson.Serialize(MiniJson.Parse(json), true);
            Assert.AreEqual(json, MiniJson.Serialize(MiniJson.Parse(pretty)));
        }
    }
}
