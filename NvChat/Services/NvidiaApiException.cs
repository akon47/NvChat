using System;

namespace NvChat.Services
{
    /// <summary>
    /// NVIDIA API 호출 실패 시 던져지는 예외.
    /// </summary>
    public class NvidiaApiException : Exception
    {
        public NvidiaApiException(string message) : base(message)
        {
        }

        public NvidiaApiException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
