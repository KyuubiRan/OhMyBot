using OhMyOneBot.V11.Lib.Messages.CQ;

namespace OhMyBot.Tests;

[TestClass]
public class TestCQSerialize
{
    [TestMethod]
    public void SerializeTest()
    {
        var cq = new CQCode { Type = "at", Parameters = { { "qq", "123456" } } };
        Assert.AreEqual("[CQ:at,qq=123456]", cq.ToString());

        var cq2 = new CQCode { Type = "anonymous" };
        Assert.AreEqual("[CQ:anonymous]", cq2.ToString());

        var cq3 = new CQCode { Type = "anonymous", Parameters = { { "ignore", "" } } };
        Assert.ThrowsExactly<FormatException>(cq3.ToString);

        var cq4 = new CQCode { Type = "share", Parameters = { { "url", "https://google.com" }, { "title", "咕噜咕噜" } } };
        Assert.AreEqual("[CQ:share,url=https://google.com,title=咕噜咕噜]", cq4.ToString());
    }

    [TestMethod]
    public void DeserializeTest()
    {
        var cq = CQCodeSerializer.Deserialize("[CQ:at,qq=123456]");
        Assert.AreEqual("at", cq.Type);
        Assert.HasCount(1, cq.Parameters);
        Assert.IsTrue(cq.Parameters.ContainsKey("qq"));
        Assert.AreEqual("123456", cq.Parameters["qq"]);

        var cq2 = CQCodeSerializer.Deserialize("[CQ:anonymous]");
        Assert.AreEqual("anonymous", cq2.Type);
        Assert.HasCount(0, cq2.Parameters);

        Assert.ThrowsExactly<FormatException>(() => CQCodeSerializer.Deserialize("[CQ:anonymous,ignore]"));

        var cq4 = CQCodeSerializer.Deserialize("[CQ:share,url=https://google.com,title=咕噜咕噜]");
        Assert.AreEqual("share", cq4.Type);
        Assert.HasCount(2, cq4.Parameters);
        Assert.IsTrue(cq4.Parameters.ContainsKey("url"));
        Assert.AreEqual("https://google.com", cq4.Parameters["url"]);
        Assert.IsTrue(cq4.Parameters.ContainsKey("title"));
        Assert.AreEqual("咕噜咕噜", cq4.Parameters["title"]);
    }
}