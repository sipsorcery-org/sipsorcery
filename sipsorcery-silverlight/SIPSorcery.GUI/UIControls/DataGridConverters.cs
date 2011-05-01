using System;
using System.Windows.Data;
using System.Globalization;
using SIPSorcery.SIP;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public class SIPURIConverter : IValueConverter
    {
        public object Convert(object value,
                           Type targetType,
                           object parameter,
                           CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }
            else
            {
                return ((SIPURI)value).ToParameterlessString();
            }
        }

        public object ConvertBack(object value,
                                  Type targetType,
                                  object parameter,
                                  CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }
            else
            {
                return SIPURI.ParseSIPURI(value.ToString());
            }
        }
    }

    public class SIPParameterlessURIConverter : IValueConverter
    {
        public object Convert(object value,
                           Type targetType,
                           object parameter,
                           CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }
            else
            {
                return ((SIPParameterlessURI)value).ToString();
            }
        }

        public object ConvertBack(object value,
                                  Type targetType,
                                  object parameter,
                                  CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }
            else
            {
                return SIPParameterlessURI.ParseSIPParamterlessURI(value.ToString());
            }
        }
    }

    public class PasswordConverter : IValueConverter
    {
        public object Convert(object value,
                           Type targetType,
                           object parameter,
                           CultureInfo culture)
        {
            if (value != null)
            {
                return "******";
            }
            else
            {
                return String.Empty;
            }
        }

        public object ConvertBack(object value,
                                  Type targetType,
                                  object parameter,
                                  CultureInfo culture)
        {
            return value as string;
        }
    }

    public class DateTimeConverter : IValueConverter
    {
        public object Convert(object value,
                           Type targetType,
                           object parameter,
                           CultureInfo culture)
        {
            if (value != null && ((DateTime)value) != DateTime.MinValue)
            {
                return ((DateTime)value).ToString("dd MMM yyyy HH:mm:ss");
            }
            else
            {
                return String.Empty;
            }
        }

        public object ConvertBack(object value,
                                  Type targetType,
                                  object parameter,
                                  CultureInfo culture)
        {
            return DateTime.Parse(value.ToString());
        }
    }

    public class DateTimeOffsetConverter : IValueConverter
    {
        public object Convert(object value,
                           Type targetType,
                           object parameter,
                           CultureInfo culture)
        {
            if (value != null)
            {
                return ((DateTimeOffset)value).ToString("dd MMM yyyy HH:mm:ss");
            }
            else
            {
                return String.Empty;
            }
        }

        public object ConvertBack(object value,
                                  Type targetType,
                                  object parameter,
                                  CultureInfo culture)
        {
            return DateTimeOffset.Parse(value.ToString());
        }
    }

    public class FromHeaderConverter : IValueConverter
    {
        public object Convert(object value,
                            Type targetType,
                            object parameter,
                            CultureInfo culture)
        {
            SIPFromHeader fromheader = SIPFromHeader.ParseFromHeader(value as string);
            string fromName = (fromheader.FromName.IsNullOrBlank()) ? null : fromheader.FromName + " ";
            return fromName + "<" + fromheader.FromURI.ToParameterlessString() + ">";
        }

        public object ConvertBack(object value,
                                  Type targetType,
                                  object parameter,
                                  CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
