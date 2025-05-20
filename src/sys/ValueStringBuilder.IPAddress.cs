using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace SIPSorcery.Sys;

internal ref partial struct ValueStringBuilder
{

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET8_0_OR_GREATER
    public void Append(IPAddress value) => AppendSpanFormattable(value, null, null);
#else
    public void Append(IPAddress value) => Append(value.ToString());
#endif
}
