namespace OhMyLib.Requests;

public abstract class HttpClientBase(Uri baseAddress)
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = baseAddress
    };

    public static implicit operator HttpClient(HttpClientBase client) => client._client;
}