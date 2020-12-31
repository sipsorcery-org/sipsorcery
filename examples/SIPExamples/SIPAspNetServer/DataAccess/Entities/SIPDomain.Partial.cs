using System;
using System.Collections.Generic;

#nullable disable

namespace SIPAspNetServer.DataAccess
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
