namespace EdgeTtsSharp.Exception
{
    public class EdgeTTSException : System.Exception
    {
        public EdgeTTSException(string message) : base(message) { }
        public EdgeTTSException(string message, System.Exception innerException) : base(message, innerException) { }
    }
}
