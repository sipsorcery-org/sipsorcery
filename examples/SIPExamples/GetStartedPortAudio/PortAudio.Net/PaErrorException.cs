using System;
using System.Runtime.InteropServices;
using static PortAudio.Net.PaBindings;

using pa_error_t = System.Int32;

namespace PortAudio.Net
{
    public class PaErrorException : Exception
    {
        public PaErrorException() { }

        public PaErrorException(string message) : base(message) { }

        public PaErrorException(string message, Exception inner) : base(message, inner) { }
        
        public PaErrorException(pa_error_t error) : base(PaLibrary.GetErrorText(error)) { }

        public static void ThrowIfError(pa_error_t error)
        {
            if (error < 0)
                throw new PaErrorException(error);
        }
    }
}