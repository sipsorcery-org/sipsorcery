using System;
using System.Runtime.InteropServices;

using unsigned_long_t = System.UInt64;

namespace PortAudio.Net
{
    internal class StreamFinishedCallbackContainer
    {
        private PaStreamFinishedCallback callbackProvider;
        private object userData;

        public StreamFinishedCallbackContainer(PaStreamFinishedCallback callbackProvider, object userData)
        {
            this.callbackProvider = callbackProvider;
            this.userData = userData;
        }

        public unsafe void Callback(IntPtr garbage)
        {
            callbackProvider(userData);
        }
    }
}