// ============================================================================
// FileName: NumberFormatting.cs
//
// Description:
// Some useful number formatting functions.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Feb 2006	Aaron Clauson	Created.
// ============================================================================

using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys
{
	public class NumberFormatter
	{
		public static string ToSIFormat(double number, int decimalPlaces)
		{
			if(number > 1000000000000)
			{
				double teraNumber = Math.Round((double)(number / (double)100000000000), decimalPlaces);
				return teraNumber.ToString() + "T";
			}
			if(number > 1000000000)
			{
				double gigaNumber = Math.Round((double)(number / (double)100000000), decimalPlaces);
				return gigaNumber.ToString() + "G";
			}
			else if(number > 1000000)
			{
				double kiloNumber = Math.Round((double)(number / (double)1000000), decimalPlaces);
				return kiloNumber.ToString() + "M";
			}
			else if(number > 1000)
			{
				double kiloNumber = Math.Round((double)(number / (double)1000), decimalPlaces);
				return kiloNumber.ToString() + "k";
			}
			else
			{
				return Math.Round((double)number, decimalPlaces).ToString();
			}
		}

		private static string ToSIByteFormat(double number, int decimalPlaces, string suffix)
		{
			if(number > 1099511627776)
			{
				double teraNumber = Math.Round((double)(number / (double)1099511627776), decimalPlaces);
				return teraNumber.ToString() + "T" + suffix;
			}
			if(number > 1073741824)
			{
				double gigaNumber = Math.Round((double)(number / (double)1073741824), decimalPlaces);
				return gigaNumber.ToString() + "G" + suffix;
			}
			else if(number > 1048576)
			{
				double kiloNumber = Math.Round((double)(number / (double)1048576), decimalPlaces);
				return kiloNumber.ToString() + "M" + suffix;
			}
			else if(number > 1024)
			{
				double kiloNumber = Math.Round((double)(number / (double)1024), decimalPlaces);
				return kiloNumber.ToString() + "K" + suffix;
			}
			else
			{
				return Math.Round((double)number, decimalPlaces).ToString() + suffix;
			}
		}

		
		public static string ToSIByteFormat(double number, int decimalPlaces)
		{
			return ToSIByteFormat(number, decimalPlaces, "B");
		}

		public static string ToSIBitFormat(double number, int decimalPlaces)
		{
			return ToSIByteFormat(number, decimalPlaces, "b");
		}
	}
}
