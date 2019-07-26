using System.Configuration;

namespace SIPSorcery.Sys
{
    public class AppSettingsConfiguration : IConfiguration
    {
        public string GetSetting(string key)
        {
            return ConfigurationManager.AppSettings[key];
        }

        public object GetSection(string sectionName)
        {
            return ConfigurationManager.GetSection(sectionName);
        }
    }
}
