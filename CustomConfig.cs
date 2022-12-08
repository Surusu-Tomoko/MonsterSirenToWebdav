using System.Configuration;
using ConfigurationManager = System.Configuration.ConfigurationManager;

namespace MonsterSiren
{
    public class CustomConfig
    {
        public static bool SetValue(string key, string value)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (settings[key] == null)
                {
                    settings.Add(key, value);
                }
                else
                {
                    settings[key].Value = value;
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
                Console.WriteLine("Save Change!");
                return true;
            }
            catch (ConfigurationErrorsException)
            {
                Console.WriteLine("Error writing app settings");
                return false;
            }
        }

        public static string GetValue(string key)
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            return settings[key].Value;
            if (settings[key].Value == null)
            {
                
            } else
            {
                return "";
            }
            
        }

        /*public static string GetList()
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;

            return settings[key].Value;
        }*/
    }
}
