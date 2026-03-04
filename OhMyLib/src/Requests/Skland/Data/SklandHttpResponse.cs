namespace OhMyLib.Requests.Skland.Data;

public class SklandBaseHttpResponse
{
    public int Status { get; set; } = 0;
    public string Msg { get; set; } = string.Empty;
    public string? Type { get; set; } = null;
}

public class SklandHttpResponse<T> : SklandBaseHttpResponse
{
    public T? Data { get; set; } = default;
}