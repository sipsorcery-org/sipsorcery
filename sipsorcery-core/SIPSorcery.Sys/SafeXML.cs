//-----------------------------------------------------------------------------
// Filename: SafeXML.cs
//
// Description: Converts special charatcers in XML to their safe equivalent.
//
// History:
// 22 jun 2005	Aaron Clauson	Created.
// 
//-----------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;

namespace SIPSorcery.Sys
{
	public class SafeXML
	{
		public static string MakeSafeXML(string xmlString)
		{
			if(xmlString != null && xmlString.Trim().Length > 0)
			{
				xmlString = Regex.Replace(xmlString, "&", "&amp;");
				xmlString = Regex.Replace(xmlString, "<", "&lt;");
				xmlString = Regex.Replace(xmlString, ">", "&gt;");

				char[] xmlChars = xmlString.ToCharArray();

				for(int index=0; index<xmlChars.Length; index++)
				{
					int xmlCharVal = Convert.ToInt32(xmlChars[index]);

					if(xmlCharVal < 32 && xmlCharVal != 10 && xmlCharVal != 13)
					{
						xmlChars[index] = ' ';
					}
				}

				return new string(xmlChars);
			}
			else
			{
				return null;
			}
		}
	}
}
