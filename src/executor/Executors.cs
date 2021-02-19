using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Executor
{
    public static class Executors
    {
        public static ExecutorService NewSingleThreadExecutor()
        {
            return new ExecutorService();
        }
    }
}
