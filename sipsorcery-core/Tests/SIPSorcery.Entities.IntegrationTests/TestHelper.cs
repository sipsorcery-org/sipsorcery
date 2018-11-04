namespace SIPSorcery.Entities.IntegrationTests
{
    class TestHelper
    {
        public static void ExecuteQuery(string query)
        {
            using (var sipSorceryEntities = new SIPSorceryEntities())
            {
                sipSorceryEntities.Database.ExecuteSqlCommand(query, null);
            }
        }
    }
}
