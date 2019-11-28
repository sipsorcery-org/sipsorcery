//-----------------------------------------------------------------------------
// Filename: SafeXML.cs
//
// Description: Converts special characters in XML to their safe equivalent.
//
// Author(s):
// Aaron Clauson
//
// History:
// 22 Jun 2005	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;

namespace SIPSorcery.Sys
{
    public class SafeXML
    {
        public static string MakeSafeXML(string xmlString)
        {
            if (xmlString != null && xmlString.Trim().Length > 0)
            {
                xmlString = Regex.Replace(xmlString, "&", "&amp;");
                xmlString = Regex.Replace(xmlString, "<", "&lt;");
                xmlString = Regex.Replace(xmlString, ">", "&gt;");

                char[] xmlChars = xmlString.ToCharArray();

                for (int index = 0; index < xmlChars.Length; index++)
                {
                    int xmlCharVal = Convert.ToInt32(xmlChars[index]);

                    if (xmlCharVal < 32 && xmlCharVal != 10 && xmlCharVal != 13)
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
