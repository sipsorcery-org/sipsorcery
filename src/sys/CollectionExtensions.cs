#nullable enable

using System.Collections.Generic;

using Small.Collections;

using TypeNum;

namespace SIPSorcery.Sys;

static class CollectionExtensions
{
    public static void AddRange<TSize, T, Enumerator>(this SmallList<TSize, T> list, Enumerator enumerator)
        where TSize : unmanaged, INumeral<T>
        where T : unmanaged
        where Enumerator : IEnumerator<T>
    {
        while (enumerator.MoveNext())
        {
            list.Add(enumerator.Current);
        }
    }
}
