using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Provisioning;
using Common;
using log4net;
using ServiceStack;
using ServiceStack.Text;

namespace MultiFile.Configurator
{
    public class Generator
    {
        // ReSharper disable once PossibleNullReferenceException
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Context Context { get; set; }

        private IAquariusClient Client { get; set; }

        private const string PluginSettingPrefix = "FieldDataPluginConfig-";
        private const string MultiFilePluginName = "MultiFile";
        private const string MultiFilePluginSettingGroup = PluginSettingPrefix + MultiFilePluginName;
        private const string MultiFilePluginSettingKey = nameof(Config);

        public void Run()
        {
            Validate();

            using (Client = CreateConnectedClient())
            {
                var (config, existingSetting) = GenerateConfig();

                var configJsonText = CreateConfigJson(config);

                if (Context.SaveOnServer)
                {
                    SaveServerSetting(configJsonText, existingSetting);
                }

                var saveToDisk = !string.IsNullOrWhiteSpace(Context.JsonPath);

                if (saveToDisk)
                {
                    File.WriteAllText(Context.JsonPath, configJsonText);

                    Log.Info($"Saved generated configuration to '{Context.JsonPath}'.");
                }

                if (!Context.SaveOnServer && !saveToDisk)
                {
                    Log.Warn("The generated configuration is not saved to the app server or to disk.");
                    Log.Warn(configJsonText);
                }
            }
        }

        private void Validate()
        {
            ThrowIfEmpty(nameof(Context.Server), Context.Server);
            ThrowIfEmpty(nameof(Context.Username), Context.Username);
            ThrowIfEmpty(nameof(Context.Password), Context.Password);

            if (Context.GenerateForExternalUse && Context.SaveOnServer)
            {
                Log.Warn($"Disabling /{nameof(Context.SaveOnServer)}=true since /{nameof(Context.GenerateForExternalUse)}=true");
                Context.SaveOnServer = false;
            }
        }

        private void ThrowIfEmpty(string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
                return;

            throw new ExpectedException($"The /{name}= option must be set.");
        }

        private IAquariusClient CreateConnectedClient()
        {
            Log.Info($"Connecting to {Context.Server} ...");
            var client = AquariusClient.CreateConnectedClient(Context.Server, Context.Username, Context.Password);

            Log.Info($"Connected to {Context.Server} ({client.ServerVersion}) as {Context.Username}");

            return client;
        }

        private static string CreateConfigJson(Config config)
        {
            using (JsConfig.With(new ServiceStack.Text.Config{ IncludeNullValues = false}))
                return config.ToJson().IndentJson();
        }

        private (Config Config, Setting ExistingSetting) GenerateConfig()
        {
            var allPluginSettings = Client.Provisioning.Get(new GetSettings())
                .Results
                .Where(s => s.Group.StartsWith(PluginSettingPrefix) && s.Group.Length > PluginSettingPrefix.Length)
                .ToList();

            var multiFileSetting = allPluginSettings
                .FirstOrDefault(s => s.Group == MultiFilePluginSettingGroup && s.Key == MultiFilePluginSettingKey);

            var delegatePlugins = Client.Provisioning.Get(new GetFieldDataPlugins())
                .Results
                .Where(p => !p.PluginFolderName.Equals(MultiFilePluginName, StringComparison.InvariantCultureIgnoreCase) && (Context.IncludeDisabledPluginSettings || p.IsEnabled))
                .ToList();

            var config = new Config();

            var delegatePluginSettings = allPluginSettings
                .Where(s => s.Group != MultiFilePluginSettingGroup)
                .GroupBy(s => s.Group)
                .ToDictionary(
                    g => g.Key.Substring(PluginSettingPrefix.Length),
                    g => g.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    StringComparer.InvariantCultureIgnoreCase);

            PluginConfig CreateOnlyPluginSettings(string pluginFolderName)
            {
                if (!delegatePluginSettings.TryGetValue(pluginFolderName, out var settings) || !settings.Any())
                    return null;

                return new PluginConfig
                {
                    Settings = settings
                        .ToDictionary(
                            kvp => $"{pluginFolderName}-{kvp.Key}",
                            kvp => kvp.Value),
                };
            }

            PluginConfig CreateExplicitPluginConfig(FieldDataPlugin plugin)
            {
                delegatePluginSettings.TryGetValue(plugin.PluginFolderName, out var settings);

                if (plugin.IsEnabled)
                    return new PluginConfig
                    {
                        Path = $"{plugin.PluginFolderName}.plugin",
                        PluginPriority = plugin.PluginPriority,
                        Settings = settings
                    };

                return CreateOnlyPluginSettings(plugin.PluginFolderName);
            }

            if (Context.GenerateForExternalUse)
            {
                // Generate a more full set of explicit plugin configurations
                config.Plugins = delegatePlugins
                    .Select(CreateExplicitPluginConfig)
                    .Where(pc => pc != null)
                    .ToList();
            }
            else
            {
                // Otherwise just generate all the known plugin settings, in case other plugins get added/enabled externally at a later date
                config.Plugins = delegatePluginSettings
                    .Select(dps => CreateOnlyPluginSettings(dps.Key))
                    .Where(pc => pc != null)
                    .ToList();
            }

            return (config, multiFileSetting);
        }

        private void SaveServerSetting(string configJsonText, Setting existingSetting)
        {
            Log.Info($"Saving generated configuration to the app server ...");

            if (existingSetting == null)
            {
                Log.Info($"Creating new {MultiFilePluginName} {MultiFilePluginSettingKey} setting ...");

                Client.Provisioning.Post(new PostSetting
                {
                    Group = MultiFilePluginSettingGroup,
                    Key = MultiFilePluginSettingKey,
                    Value = configJsonText,
                    Description = $"Generated by {FileHelper.ExeNameAndVersion}"
                });
            }
            else
            {
                Log.Info($"Updating existing {MultiFilePluginName} {MultiFilePluginSettingKey} setting ...");

                Client.Provisioning.Put(new PutSetting
                {
                    Group = existingSetting.Group,
                    Key = existingSetting.Key,
                    Value = configJsonText,
                    Description = $"Updated by {FileHelper.ExeNameAndVersion}, replacing previous version from {existingSetting.LastModifiedTime}"
                });
            }

            Log.Info($"Saved generated configuration to the app server.");
        }
    }
}
