using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIPSorcery.Entities;

namespace SIPSorcery.Entities.IntegrationTests
{
    class TestHelper
    {
        public static void ExecuteQuery(string query)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                sipSorceryEntities.ExecuteStoreCommand(query, null);
            }
        }
    }
}
