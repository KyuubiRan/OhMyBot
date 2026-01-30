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

    [TestMethod]
    public async Task TestKuroBbsSigninAsync()
    {
        var posts = await _client.BbsSignInAsync();
        if (!posts.Success)
        {
            throw new Exception("签到失败：" + posts.Msg);
        }

        Console.WriteLine(JsonSerializer.SerializeToNode(posts.Data!)!.ToString());
    }

    [TestMethod]
    public async Task TestShareAsync()
    {
        var posts = await _client.BbsSharePostAsync();
        if (!posts.Success)
        {
            throw new Exception("分享帖子失败：" + posts.Msg);
        }

        Console.WriteLine(JsonSerializer.SerializeToNode(posts.Data!)!.ToString());
    }

    [TestMethod]
    public async Task TestLikePostsAsync()
    {
        var pst = await _client.BbsGetPostsAsync();
        if (!pst.Success)
        {
            throw new Exception("获取帖子列表失败：" + pst.Msg);
        }

        var posts = pst.Data!.PostList;

        for (var i = 0; i < 5 && i < posts.Count; i++)
        {
            var post = posts[i];
            var likeResult = await _client.BbsLikePostAsync(post.GameId, post.GameForumId, post.PostType, post.PostId, post.UserId);
            if (!likeResult.Success)
            {
                throw new Exception($"点赞帖子 {post.PostId} 失败：" + likeResult.Msg);
            }

            Console.WriteLine($"点赞帖子 {post.PostId} 成功");
            await Task.Delay(2000);
        }

        Console.WriteLine("全部点赞完成");
    }

    [TestMethod]
    public async Task TestGetPostDetailAsync()
    {
        var pst = await _client.BbsGetPostsAsync();
        if (!pst.Success)
        {
            throw new Exception("获取帖子列表失败：" + pst.Msg);
        }

        var posts = pst.Data!.PostList;
        for (var i = 0; i < 3; i++)
        {
            var post = posts[i];
            var detailResult = await _client.BbsGetPostDetailAsync(post.PostId);
            if (!detailResult.Success)
            {
                throw new Exception($"获取帖子详情 {post.PostId} 失败：" + detailResult.Msg);
            }

            Console.WriteLine($"帖子 {post.PostId}: {post.PostTitle}");
            await Task.Delay(2000);
        }

        Console.WriteLine("全部获取完成");
    }
}