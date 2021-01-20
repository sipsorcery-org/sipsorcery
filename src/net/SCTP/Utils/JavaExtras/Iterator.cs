using System.Collections.Generic;

namespace SCTP4CS.Utils
{
    public class Iterator<K, V>
    {
        Dictionary<K, V> dict;
        K[] keys;
        int i;
        public Iterator(Dictionary<K, V> dict)
        {
            this.dict = dict;
            keys = new K[dict.Count];
            dict.Keys.CopyTo(keys, 0);
            i = 0;
        }

        public bool hasNext()
        {
            return i < keys.Length;
        }

        K lastKey;
        public V next()
        {
            lastKey = keys[i++];
            return dict[lastKey];
        }


        public void remove()
        {
            if (dict.ContainsKey(lastKey))
                dict.Remove(lastKey);
        }
    }

    public class Iterator<V>
    {
        List<V> list;
        int i;
        public Iterator(List<V> list)
        {
            this.list = list;
            i = 0;
        }

        public bool hasNext()
        {
            return i < list.Count;
        }

        public V next()
        {
            return list[i++];
        }


        public void remove()
        {
            list.RemoveAt(--i);
        }
    }

    public static class IteratorHelper
    {
        public static Iterator<K, V> iterator<K, V>(this Dictionary<K, V> dict)
        {
            return new Iterator<K, V>(dict);
        }
        public static Iterator<V> iterator<V>(this List<V> list)
        {
            return new Iterator<V>(list);
        }
    }
}
