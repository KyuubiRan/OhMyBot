namespace OhMyLib.Requests.Kuro.Data;

public class KuroBaseHttpResponse
{
    public int Code { get; set; } = 200;
    public string Msg { get; set; } = string.Empty;
    public bool Success { get; set; }
    public Guid? TraceId { get; set; } = null;
}

public class KuroHttpResponse<T> : KuroBaseHttpResponse
{
    public T? Data { get; set; } = default;
}