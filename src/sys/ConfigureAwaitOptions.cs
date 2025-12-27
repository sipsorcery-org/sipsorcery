#if !NET8_0_OR_GREATER
namespace System.Threading.Tasks;

internal static class ConfigureAwaitOptions
{
    public const bool None = false;
}
#endif
