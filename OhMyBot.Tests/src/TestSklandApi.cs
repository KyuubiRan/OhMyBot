using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OhMyLib.Requests.Skland;

namespace OhMyBot.Tests;

[TestClass]
public class TestSklandApi
{
    private static IConfiguration _configuration = null!;
    private static SklandHttpClient _client = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _configuration = new ConfigurationBuilder()
                         .AddJsonFile("appsettings.Test.json", optional: true)
                         .AddEnvironmentVariables()
                         .Build();

        var cfg = _configuration.GetSection("Skland");
        _client = new SklandHttpClient(cfg["Token"] ?? "");
    }

    [TestMethod]
    public async Task TestGrantOAuth()
    {
        var oauth = await _client.GrantUserOAuth();
        Console.WriteLine(JsonSerializer.SerializeToNode(oauth)!.ToString());
    }
}