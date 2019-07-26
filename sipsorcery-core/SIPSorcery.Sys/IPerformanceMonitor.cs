namespace SIPSorcery.Sys
{
    public interface IPerformanceMonitor
    {
        void IncrementCounter(string counterName, int incrementBy);
    }
}
