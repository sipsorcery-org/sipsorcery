using System;
using System.Collections.Generic;
using System.Linq;

namespace SIPSorceryMedia.Abstractions
{
    public class MediaFormatManager<T>
    {
        public readonly List<T> SupportedFormats = new List<T>();

        public T SelectedFormat { get; private set; }
        private List<T> _filteredFormats = new List<T>();

        public MediaFormatManager(List<T> supportedFormats)
        {
            SupportedFormats = supportedFormats;
            _filteredFormats = new List<T>(SupportedFormats);
        }

        public List<T> GetSourceFormats()
        {
            return _filteredFormats;
        }

        public void RestrictFormats(Func<T, bool> filter)
        {
            if (filter == null)
            {
                _filteredFormats = new List<T>(SupportedFormats);
            }
            else
            {
                _filteredFormats = _filteredFormats.Where(x => filter(x)).ToList();
            }
        }

        public void SetSelectedFormat(T format)
        {
            SelectedFormat = format;
        }
    }
}
