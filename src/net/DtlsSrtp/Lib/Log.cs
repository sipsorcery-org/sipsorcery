// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net.SharpSRTP
{
    public static class Log
    {
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        public static bool WarnEnabled { get; set; } = true;
        public static void Warn(string message, Exception ex = null)
        {
            SinkWarn(message, ex);
        }

        public static bool ErrorEnabled { get; set; } = true;
        public static void Error(string message, Exception ex = null)
        {
            SinkError(message, ex);
        }

        public static bool TraceEnabled { get; set; } = true;
        public static void Trace(string message, Exception ex = null)
        {
            SinkTrace(message, ex);
        }

        public static bool DebugEnabled { get; set; }
#if DEBUG
            = true;
#endif

        public static void Debug(string message, Exception ex = null)
        {
            SinkDebug(message, ex);
        }

        public static bool InfoEnabled { get; set; }
#if DEBUG
            = true;
#endif
        public static void Info(string message, Exception ex = null)
        {
            SinkInfo(message, ex);
        }

        public static Action<string, Exception> SinkWarn = new Action<string, Exception>((m, ex) => { logger.LogWarning(m); });
        public static Action<string, Exception> SinkError = new Action<string, Exception>((m, ex) => { logger.LogError(m); });
        public static Action<string, Exception> SinkTrace = new Action<string, Exception>((m, ex) => { logger.LogTrace(m); });
        public static Action<string, Exception> SinkDebug = new Action<string, Exception>((m, ex) => { logger.LogDebug(m); });
        public static Action<string, Exception> SinkInfo = new Action<string, Exception>((m, ex) => { logger.LogInformation(m); });
    }
}
