using System;
using System.Collections.Generic;

namespace SIPSorceryMedia.Abstractions
{
    public class MediaFormatManager<T>
    {
        public readonly List<T> SupportedFormats = new List<T>();

        public T? SelectedFormat { get; private set; }
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

        /// <summary>
        /// Requests that the audio sink and source only advertise support for the supplied list of codecs.
        /// Only codecs that are already supported and in the <see cref="SupportedFormats" /> list can be 
        /// used.
        /// </summary>
        /// <param name="filter">Function to determine which formats the source formats should be restricted to.</param>
        public void RestrictFormats(Func<T, bool> filter)
        {
            if (filter is null)
            {
                _filteredFormats = new List<T>(SupportedFormats);
            }
            else
            {
                var filtered = new List<T>(_filteredFormats.Count);
                foreach (var item in _filteredFormats)
                {
                    if (filter(item))
                    {
                        filtered.Add(item);
                    }
                }
                _filteredFormats = filtered;
            }
        }

        public void SetSelectedFormat(T format)
        {
            SelectedFormat = format;
        }
    }
}
