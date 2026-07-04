using System.Text.Json;
using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Messages.Entity;

// NapCat 的消息段 data 里混有非字符串标量（如 image.file_size / sub_type 是数字、file.isDir 是布尔），
// 而消息段模型用 Dictionary<string,string> 承接。System.Text.Json 默认无法把 JSON number/bool
// 读进 string 值，会导致整条消息（含图片/文件等）反序列化失败。此转换器在读取时把任意标量强制转为字符串。
internal sealed class MessageParameterMapConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("消息段 data 应为 JSON 对象。");
        }

        var result = new Dictionary<string, string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("消息段 data 中出现意外的 JSON 结构。");
            }

            var key = reader.GetString()!;
            reader.Read();
            result[key] = ReadValueAsString(ref reader);
        }

        throw new JsonException("读取消息段 data 时 JSON 意外结束。");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }

    // reader 位于值的起始 token。标量原样取用（单 token，外层循环会推进）；
    // 对象/数组用 JsonDocument 完整读取并保留原始 JSON 文本。
    private static string ReadValueAsString(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString() ?? string.Empty;
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Null:
                return string.Empty;
            case JsonTokenType.Number:
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                // 数字保留原始文本（避免 int/double 强转丢精度）；对象/数组保留原始 JSON。
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    return document.RootElement.GetRawText();
                }
            default:
                throw new JsonException($"消息段 data 中出现不支持的 JSON 值：{reader.TokenType}。");
        }
    }
}
