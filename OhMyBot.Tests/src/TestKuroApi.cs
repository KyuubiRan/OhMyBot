// See https://aka.ms/new-console-template for more information

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OhMyLib.Requests.Kuro;

namespace OhMyBot.Tests;

[TestClass]
public class TestKuroApi
{
    private static IConfiguration _configuration = null!;
    private static KuroHttpClient _client = null!;
    
    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _configuration = new ConfigurationBuilder()
                         .AddJsonFile("appsettings.Test.json", optional: true)
                         .AddEnvironmentVariables()
                         .Build();

        var kuro = _configuration.GetSection("Kuro");

        _client = new KuroHttpClient(kuro["Token"] ?? "", kuro["DevCode"], kuro["DistinctId"]);
    }

    [TestMethod]
    public async Task TestKuroBbsGetMineProgressAsync()
    {
        var kuro = _configuration.GetSection("Kuro");

        var result = await _client.BbsGetMineAsync(long.Parse(kuro["Uid"]!));
        if (!result.Success)
        {
            throw new Exception("获取用户信息失败：" + result.Msg);
        }

        Console.WriteLine(JsonSerializer.SerializeToNode(result.Data!.Mine!)!.ToString());
    }

    [TestMethod]
    public async Task TestKuroBbsGetPostsAsync()
    {
        var posts = await _client.BbsGetPostsAsync();
        if (!posts.Success)
        {
            throw new Exception("获取帖子列表失败：" + posts.Msg);
        }
        
        Console.WriteLine(JsonSerializer.SerializeToNode(posts.Data!)!.ToString());
    }
}