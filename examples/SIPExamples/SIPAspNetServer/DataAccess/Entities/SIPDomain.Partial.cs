// ============================================================================
// FileName: SIPDomain.Partial.cs
//
// Description:
// Represents the SIPDomain entity. This partial class is used to apply 
// additional properties or metadata to the audo generated SIPDomain class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 31 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System.Collections.Generic;

#nullable disable

namespace demo.DataAccess
{
    public partial class SIPDomain
    {
        private const char ALIAS_SEPERATOR_CHAR = ';';

        private List<string> _aliases;
        public List<string> Aliases
        {
            get
            {
                if (_aliases == null)
                {
                    _aliases = ParseAliases(AliasList);
                }

                return _aliases;
            }
        }

        private List<string> ParseAliases(string aliasString)
        {
            List<string> aliasList = new List<string>();

            if (!string.IsNullOrEmpty(aliasString))
            {
                string[] aliases = aliasString.Split(ALIAS_SEPERATOR_CHAR);
                if (aliases != null && aliases.Length > 0)
                {
                    foreach (string alias in aliases)
                    {
                        if (!string.IsNullOrEmpty(alias) && !aliasList.Contains(alias.ToLower()))
                        {
                            aliasList.Add(alias.ToLower());
                        }
                    }
                }
            }

            return aliasList;
        }
    }
}
