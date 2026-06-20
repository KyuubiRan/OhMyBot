using OhMyBot.OneBotV11.Messages.Entity;

namespace OhMyBot.Tests;

[TestClass]
public class V2OneBotTests
{
    [TestMethod]
    public void GetMessageByTypeReturnsRequestedIndexedEntity()
    {
        var messages = new List<MessageEntity>
        {
            MessageEntity.Text("hello"),
            MessageEntity.At("10001"),
            MessageEntity.At("10002")
        };

        var secondAt = messages.GetMessageByType(MessageType.At, n: 1);

        Assert.IsNotNull(secondAt);
        Assert.AreEqual("10002", secondAt.Parameters["qq"]);
    }

    [TestMethod]
    public void GetMessageByTypeReturnsNullWhenMissing()
    {
        var messages = new List<MessageEntity> { MessageEntity.Text("hello") };

        var image = messages.GetMessageByType(MessageType.Image);

        Assert.IsNull(image);
    }
}
