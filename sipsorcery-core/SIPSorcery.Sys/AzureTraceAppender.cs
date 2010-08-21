using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;
using log4net;
using log4net.Appender;
using log4net.Core;

namespace SIPSorcery.Sys
{
    public class AzureTraceAppender : AppenderSkeleton
    {
        protected override void Append(LoggingEvent loggingEvent)
        {
            Trace.WriteLine(loggingEvent.RenderedMessage, loggingEvent.Level.ToString());
        }
    }
}