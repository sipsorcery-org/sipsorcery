using System;

namespace SIPSorcery.Sys;

public delegate void ReadOnlySpanAction<T>(ReadOnlySpan<T> span);

public delegate void ReadOnlyMemoryAction<T>(ReadOnlyMemory<T> span);
