namespace EdgeTtsSharp.Exception
{
    public class WebSocketException : EdgeTTSException
    {
        public WebSocketException(string message) : base(message) { }
        public WebSocketException(string message, System.Exception innerException) : base(message, innerException) { }
    }
}
