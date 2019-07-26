namespace SIPSorcery.Sys
{
    public interface IConfiguration
    {
        string GetSetting(string key);
        object GetSection(string sectionName);
    }
}
