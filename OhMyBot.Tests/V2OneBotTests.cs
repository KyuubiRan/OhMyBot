using OhMyBot.OneBotV11.Messages.Entity;
using System.Text.Json;
using OhMyBot.OneBotV11.Events.Messages.Group;

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

    [TestMethod]
    public void GroupMessageWithNonStringSegmentDataDeserializes()
    {
        // NapCat 实际会在消息段 data 里发数字/布尔（image 的 file_size 是数字、file 的 isDir 是布尔）。
        // 若模型不容忍非字符串标量，整条含图片的消息就会反序列化失败、消息被丢弃。
        const string raw = """
        {
          "post_type": "message",
          "message_type": "group",
          "message_id": 42,
          "user_id": 10001,
          "group_id": 20002,
          "raw_message": "hi[CQ:image]",
          "sender": { "user_id": 10001, "nickname": "tester", "card": "群名片", "role": "member" },
          "message": [
            { "type": "text", "data": { "text": "hi" } },
            { "type": "image", "data": { "file": "a.jpg", "file_size": 12345, "sub_type": 0 } }
          ]
        }
        """;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var message = JsonSerializer.Deserialize<GroupMessageEvent>(raw, options);

        Assert.IsNotNull(message);
        Assert.AreEqual(20002, message.GroupId);
        Assert.AreEqual("群名片", message.Sender.Card);
        Assert.AreEqual("hi", message.GetMessageByType(MessageType.Text)!.Parameters["text"]);
        var imageSegment = message.GetMessageByType(MessageType.Image)!;
        Assert.AreEqual("12345", imageSegment.Parameters["file_size"]);
        Assert.AreEqual("0", imageSegment.Parameters["sub_type"]);
    }
}
