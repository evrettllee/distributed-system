namespace Shared
{
    public class Response
    {
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? Data { get; set; }

        public static Response Ok(string? data = null, string? message = null) => new()
        {
            Status = "ok",
            Data = data,
            Message = message
        };

        public static Response Error(string message) => new()
        {
            Status = "error",
            Message = message
        };
    }
}