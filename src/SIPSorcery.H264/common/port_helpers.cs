//-----------------------------------------------------------------------------
// Filename: port_helpers.cs
//
// Description: Small building blocks the C++ -> C# port needs that have no
// direct upstream file:
//  - Ptr<T>: a pointer as a struct, so arrays of pointers (uint8_t* pData[4])
//    can live in inline arrays, which can't hold raw pointer elements.
//  - PtrArrayN<T> / InlineArrayN<T>: fixed-length arrays of pointers / structs
//    embedded in unmanaged structs, the way C embeds T x[N] in a struct.
//    (Arrays of primitives use C# fixed buffers instead.) Nested InlineArrayN
//    types give multi-dimensional tables: uint8_t t[6][8][8] is reached
//    through an InlineArray8<InlineArray8<uint8_t>>* and indexed t[a][b][c].
//  - StaticTable: process-lifetime native copies of upstream's const tables, so
//    the ported code can index them and take row pointers as C does.
//  - CRuntime: the libc memory functions (memcpy, memset, ...) the C code calls.
//
// Function pointers can't be inline-array elements, so a C function-pointer
// typedef becomes a `global using unsafe` alias, and an array of them in a
// struct is InlineArrayN<nint> storage behind a property that returns the
// alias pointer type (see SExpandPicFunc), which keeps C's p->pf[i](...) call
// syntax.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SIPSorcery.H264;

/// <summary>
/// A T* wrapped in a struct. Converts implicitly to and from T*, and supports the
/// pointer arithmetic the ported code does on array-of-pointer elements.
/// </summary>
internal unsafe struct Ptr<T> where T : unmanaged
{
    public T* p;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Ptr(T* p) => this.p = p;

    public static implicit operator T*(Ptr<T> x) => x.p;
    public static implicit operator Ptr<T>(T* x) => new Ptr<T>(x);

    public static T* operator +(Ptr<T> x, int i) => x.p + i;
    public static T* operator +(Ptr<T> x, nint i) => x.p + i;
    public static T* operator -(Ptr<T> x, int i) => x.p - i;
    public static T* operator -(Ptr<T> x, nint i) => x.p - i;

    public readonly bool IsNull => p == null;
}

[InlineArray(1)]
internal struct PtrArray1<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(2)]
internal struct PtrArray2<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(3)]
internal struct PtrArray3<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(4)]
internal struct PtrArray4<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(6)]
internal struct PtrArray6<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(7)]
internal struct PtrArray7<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(8)]
internal struct PtrArray8<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(15)]
internal struct PtrArray15<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(16)]
internal struct PtrArray16<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(17)]
internal struct PtrArray17<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(32)]
internal struct PtrArray32<T> where T : unmanaged
{
    private Ptr<T> _e0;
}

[InlineArray(1)]
internal struct InlineArray1<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(2)]
internal struct InlineArray2<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(3)]
internal struct InlineArray3<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(4)]
internal struct InlineArray4<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(6)]
internal struct InlineArray6<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(7)]
internal struct InlineArray7<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(8)]
internal struct InlineArray8<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(14)]
internal struct InlineArray14<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(15)]
internal struct InlineArray15<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(16)]
internal struct InlineArray16<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(17)]
internal struct InlineArray17<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(24)]
internal struct InlineArray24<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(30)]
internal struct InlineArray30<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(32)]
internal struct InlineArray32<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(33)]
internal struct InlineArray33<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(52)]
internal struct InlineArray52<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(64)]
internal struct InlineArray64<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(66)]
internal struct InlineArray66<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(128)]
internal struct InlineArray128<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(256)]
internal struct InlineArray256<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(257)]
internal struct InlineArray257<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(384)]
internal struct InlineArray384<T> where T : unmanaged
{
    private T _e0;
}

[InlineArray(460)]
internal struct InlineArray460<T> where T : unmanaged
{
    private T _e0;
}

/// <summary>
/// Native copies of upstream's const tables. C gives a table its declared
/// dimension and zero-fills any elements the initializer leaves out; Of does
/// the same. The memory is never freed, like C static data.
/// </summary>
internal static unsafe class StaticTable
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<nint, int> s_byteSizes = new();

    /// <summary>A one-dimensional table: <c>const T name[count] = { init }</c>.</summary>
    public static T* Of<T>(int count, ReadOnlySpan<T> init) where T : unmanaged => Of<T, T>(count, init);

    /// <summary>
    /// A table of <paramref name="count"/> rows of type <typeparamref name="TRow"/>
    /// (an InlineArrayN of T, or a struct), initialised from the flattened elements:
    /// <c>const T name[count][N]... = { init }</c>.
    /// </summary>
    public static TRow* Of<TRow, T>(int count, ReadOnlySpan<T> init) where TRow : unmanaged where T : unmanaged
    {
        int bytes = checked(count * sizeof(TRow));
        if (bytes % sizeof(T) != 0 || init.Length > bytes / sizeof(T))
        {
            throw new ArgumentException($"{init.Length} initializers of {sizeof(T)} bytes do not fit {count} x {sizeof(TRow)} bytes.");
        }
        void* p = NativeMemory.AlignedAlloc((nuint)Math.Max(bytes, 1), 16);
        NativeMemory.Clear(p, (nuint)bytes);
        init.CopyTo(new Span<T>(p, init.Length));
        lock (s_lock)
        {
            s_byteSizes[(nint)p] = bytes;
        }
        return (TRow*)p;
    }

    /// <summary>The size in bytes of a table made by Of, for tests.</summary>
    public static int ByteSize(void* table)
    {
        lock (s_lock)
        {
            return s_byteSizes.TryGetValue((nint)table, out int bytes) ? bytes : -1;
        }
    }
}

/// <summary>The C runtime memory functions, so ported calls read as in C.</summary>
internal static unsafe class CRuntime
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void* memcpy(void* dst, void* src, nint count)
    {
        Unsafe.CopyBlockUnaligned(dst, src, (uint)count);
        return dst;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void* memmove(void* dst, void* src, nint count)
    {
        Buffer.MemoryCopy(src, dst, count, count);
        return dst;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void* memset(void* dst, int value, nint count)
    {
        Unsafe.InitBlockUnaligned(dst, (byte)value, (uint)count);
        return dst;
    }

    public static int memcmp(void* a, void* b, nint count) =>
        new ReadOnlySpan<byte>(a, checked((int)count)).SequenceCompareTo(new ReadOnlySpan<byte>(b, checked((int)count)));
}