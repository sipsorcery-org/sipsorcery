using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.executor
{
    public static class Executors
    {
        public static ExecutorService NewSingleThreadExecutor()
        {
            return new ExecutorService();
        }
    }
}
