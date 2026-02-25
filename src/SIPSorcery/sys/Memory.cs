using System;

namespace SIPSorcery.Sys;

public delegate void ReadOnlySpanAction<T>(ReadOnlySpan<T> span);

public delegate void ReadOnlyMemoryAction<T>(ReadOnlyMemory<T> span);

public delegate void SpanAction<T>(Span<T> span);

public delegate void MemoryAction<T>(Memory<T> span);
