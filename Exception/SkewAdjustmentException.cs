namespace EdgeTtsSharp.Exception
{
    public class SkewAdjustmentException : EdgeTTSException
    {
        public SkewAdjustmentException(string message) : base(message) { }
        public SkewAdjustmentException(string message, System.Exception innerException) : base(message, innerException) { }
    }
}
