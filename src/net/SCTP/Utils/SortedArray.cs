using System;
using System.Collections.Generic;

namespace SCTP4CS.Utils
{
    public class SortedArray<T>
    {
        readonly List<T> list;

        public SortedArray()
        {
            list = new List<T>();
        }

        public void Add(T item)
        {
            list.Add(item);
            list.Sort();
        }

        public void AddToList(List<T> array)
        {
            array.AddRange(list);
        }

        public void Remove(T item)
        {
            list.Remove(item);
        }

        public void RemoveWhere(Func<T, bool> f)
        {
            for (int i = 0; i < list.Count;)
            {
                if (f(list[i])) list.RemoveAt(i);
                else i++;
            }
        }

        public void Clear()
        {
            list.Clear();
        }

        public T this[int index]
        {
            get { return list[index]; }
            set { list[index] = value; list.Sort(); }
        }

        public T First
        {
            get { return list[0]; }
        }
        public T Last
        {
            get { return list[list.Count - 1]; }
        }

        public int Count
        {
            get { return list.Count; }
        }

        public SortedArrayEnumerator GetEnumerator()
        {
            return new SortedArrayEnumerator(list);
        }

        public struct SortedArrayEnumerator
        {
            private List<T> list;
            private int index;

            public SortedArrayEnumerator(List<T> list)
            {
                this.list = list;
                this.index = -1;
            }

            public T Current
            {
                get { return list[index]; }
            }

            public void Dispose() { }

            public bool MoveNext()
            {
                index++;
                return index < list.Count;
            }

            public void Reset()
            {
                index = -1;
            }
        }
    }
}
