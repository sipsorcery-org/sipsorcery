using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BlueFace.VoIP.Net.SIP
{
    public class StringDictionary : IEnumerable
    {
        private Dictionary<string, string> m_dictionary = new Dictionary<string, string>();

        public int Count
        {
            get { return m_dictionary.Count; }
        }

        public string this[string key]
        {
            get{ return m_dictionary[key]; }
            set { m_dictionary[key] = value; }
        }

        public StringDictionary()
        { }

        public void Add(string key, string value)
        {
            m_dictionary.Add(key, value);
        }

        public bool ContainsKey(string key)
        {
            return m_dictionary.ContainsKey(key);
        }

        public void Remove(string key)
        {
            m_dictionary.Remove(key);
        }

        public IEnumerator GetEnumerator()
        {
            return m_dictionary.GetEnumerator();
        }
    }
}
