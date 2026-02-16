using System.Runtime.CompilerServices;

namespace SIPSorceryMedia.Abstractions.UnitTest
{
    public static class TestHelper
    {
        public static string GetCurrentMethodName([CallerMemberName] string methodName = default) => methodName;
    }
}
