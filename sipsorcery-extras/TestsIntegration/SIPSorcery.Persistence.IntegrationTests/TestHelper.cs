using MySql.Data.MySqlClient;

namespace SIPSorcery.Persistence.IntegrationTests
{
    class TestHelper
    {
        public static void ExecuteNonQuery(string dbConnStr, string query)
        {
            using (var conn = new MySqlConnection(dbConnStr))
            {
                conn.Open();
                var cmd = new MySqlCommand(query, conn);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
