using System.Net;
using OhMyBot.Core.Integrations.Happytuk;
using OhMyBot.Core.Commanding.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OhMyBot.Core.Infrastructure.ScheduledTasks;

namespace OhMyBot.Tests;

[TestClass]
public class V2HappytukTests
{

    [TestMethod]
    public void ScheduledTaskArgsBindAsNestedJsonObject()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Task:Enabled"] = "false",
            ["Task:Cron"] = "0 18 * * 4",
            ["Task:Args:CouponCode"] = "yyMMdd維護",
            ["Task:Args:Retry:Count"] = "3",
            ["Task:Args:Games:0"] = "latale",
            ["Task:Args:Games:1"] = "closers"
        }).Build();
        var options = new ScheduledTaskOptions();

        ScheduledTaskOptions.Bind(options, configuration.GetSection("Task"));

        Assert.IsFalse(options.Enabled);
        Assert.AreEqual("0 18 * * 4", options.Cron);
        Assert.AreEqual("yyMMdd維護", options.Args["CouponCode"]?.GetValue<string>());
        Assert.AreEqual("3", options.Args["Retry"]?["Count"]?.GetValue<string>());
        Assert.AreEqual("closers", options.Args["Games"]?[1]?.GetValue<string>());
    }

    [TestMethod]
    public void HappytukProxyBindsAsObject()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Happytuk:Proxy:Server"] = "socks5://127.0.0.1:7890",
            ["Happytuk:Proxy:Username"] = "user",
            ["Happytuk:Proxy:Password"] = "pass"
        }).Build();
        var options = new HappytukOptions();

        configuration.GetSection("Happytuk").Bind(options);

        Assert.AreEqual("socks5://127.0.0.1:7890", options.Proxy.Server);
        Assert.AreEqual("user", options.Proxy.Username);
        Assert.AreEqual("pass", options.Proxy.Password);
    }

    [TestMethod]
    public void HappytukBrowserExpandsUserHomePath()
    {
        var options = new HappytukBrowserFallbackOptions
        {
            UserDataDir = "~/.local/share/OhMyBot/happytuk-browser"
        };

        Assert.AreEqual(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "OhMyBot", "happytuk-browser"),
            options.UserDataDir);
    }

    [TestMethod]
    public void HappytukRedeemSupportsGroupsButBindRemainsPrivate()
    {
        var root = new HappytukCommandDslProvider(null!).GetNodes().Single();

        Assert.AreEqual(SupportedChatTypes.All, root.SupportChatTypes);
        Assert.AreEqual(SupportedChatTypes.Private, root.Children.Single(node => node.Name == "bind").SupportChatTypes);
        Assert.AreEqual(SupportedChatTypes.All, root.Children.Single(node => node.Name == "redeem").SupportChatTypes);
        Assert.AreEqual(SupportedChatTypes.Private, root.Children.Single(node => node.Name == "delete").SupportChatTypes);
    }

    [TestMethod]
    public async Task HappytukLoginUsesDynamicPageFieldsAndReturnsRotatedSession()
    {
        var page = Html("<input value=\"dynamic-r\" name=\"r\"><input value=\"10.0.0.1\" name=\"clientIP\">");
        page.Headers.TryAddWithoutValidation("Set-Cookie", "JSESSIONID=initial; Domain=.mangot5.com; Path=/; HttpOnly");
        var login = Json("{\"result\":{\"ok\":true}}");
        login.Headers.TryAddWithoutValidation("Set-Cookie", "JSESSIONID=authenticated; Domain=.mangot5.com; Path=/; HttpOnly");
        var handler = new QueueHandler(page, login);
        var client = new HappytukHttpClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.mangot5.com")
        });

        var session = await client.LoginAsync("account", "password");

        var parsed = HappytukSession.Parse(session, "fallback-ua");
        Assert.AreEqual("JSESSIONID=authenticated", parsed.Cookies);
        Assert.Contains("Chrome/", parsed.UserAgent);
        Assert.Contains("JSESSIONID=initial", handler.Requests[1].Cookie ?? string.Empty);
        Assert.Contains("r=dynamic-r", handler.Requests[1].Body ?? string.Empty);
        Assert.Contains("clientIP=10.0.0.1", handler.Requests[1].Body ?? string.Empty);
        Assert.Contains("userID=account", handler.Requests[1].Body ?? string.Empty);
        Assert.Contains("password=password", handler.Requests[1].Body ?? string.Empty);
        Assert.IsFalse(handler.Requests[0].IsXmlHttpRequest);
        Assert.IsTrue(handler.Requests[1].IsXmlHttpRequest);
    }

    [TestMethod]
    public async Task HappytukLoginReportsCloudflareChallenge()
    {
        var challenge = Html("<title>Just a moment...</title><script src=\"/cdn-cgi/challenge-platform/test\"></script>");
        challenge.StatusCode = HttpStatusCode.Forbidden;
        challenge.Headers.TryAddWithoutValidation("cf-ray", "test-ray");
        var client = new HappytukHttpClient(new HttpClient(new QueueHandler(challenge))
        {
            BaseAddress = new Uri("https://www.mangot5.com")
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.LoginAsync("account", "password"));

        Assert.Contains("Cloudflare", exception.Message);
        Assert.Contains("test-ray", exception.Message);
    }

    [TestMethod]
    public async Task HappytukBindNotifiesBeforeBrowserFallback()
    {
        var challenge = Html("<title>Just a moment...</title>");
        challenge.StatusCode = HttpStatusCode.Forbidden;
        var options = Options.Create(new HappytukOptions
        {
            BrowserFallback = new HappytukBrowserFallbackOptions
            {
                Enabled = true,
                ExecutablePath = "/definitely-missing/chromium"
            }
        });
        var client = new HappytukHttpClient(new HttpClient(new QueueHandler(challenge))
        {
            BaseAddress = new Uri("https://www.mangot5.com")
        });
        var service = new HappytukAccountService(
            null!,
            client,
            new HappytukBrowserClient(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<HappytukBrowserClient>.Instance),
            null!,
            null!,
            options,
            TimeProvider.System);
        var notified = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BindAsync(
            1,
            "account",
            "password",
            _ =>
            {
                notified = true;
                return Task.CompletedTask;
            }));

        Assert.IsTrue(notified);
    }

    [TestMethod]
    public async Task HappytukRedeemConvertsStringCouponAndUsesUpdatedChecksum()
    {
        var handler = new QueueHandler(
            Html("<input name=\"checksum\" type=\"hidden\" value=\"old-checksum\">"),
            Json("{\"returnCode\":\"HP111-INTERNAL\"}"),
            Json("{\"returnCode\":1,\"checksum\":\"new-checksum\",\"serverData\":{\"required\":false},\"characterData\":{\"required\":false}}"),
            Json("{\"returnCode\":1}"));
        var client = new HappytukHttpClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.mangot5.com")
        });

        var result = await client.RedeemAsync("JSESSIONID=session", "260709維護");

        Assert.AreEqual(HappytukRedeemResultType.Success, result);
        Assert.HasCount(4, handler.Requests);
        Assert.AreEqual("serial=260709%E7%B6%AD%E8%AD%B7", handler.Requests[1].Body);
        Assert.Contains("serial=HP111-INTERNAL", handler.Requests[2].Uri.Query);
        Assert.Contains("checksum=old-checksum", handler.Requests[2].Uri.Query);
        Assert.Contains("checksum=new-checksum", handler.Requests[3].Uri.Query);
        Assert.AreEqual("JSESSIONID=session", handler.Requests[3].Cookie);
    }

    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("Cookie", out var cookies) ? cookies.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Contains("X-Requested-With")));
            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Cookie, string? Body, bool IsXmlHttpRequest);
}
