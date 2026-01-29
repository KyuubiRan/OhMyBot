namespace OhMyLib.Requests.Kuro.Data;

public class KuroHttpResponse<T>
{
    public int Code { get; set; } = 200;
    public string Msg { get; set; } = string.Empty;
    public bool Success { get; set; }
    public T? Data { get; set; } = default;
    public Guid? TraceId { get; set; } = null;
}