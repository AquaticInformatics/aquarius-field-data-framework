# MultiFile.Configurator.exe

The `MultiFile.Configurator.exe` is a .NET console tool which can be used to quickly generate configuration settings for the [MultiFile](../MultiFile) plugin.

- Can be run from CMD.EXE, PowerShell, or a bash shell.
- Runs on any up-to-date Windows system.
- Generates a configuration which exposes the settings from other plugins into the MultiFile context, so those same settings can be used.

Download the latest version of the MultiFile.Configurator.exe [from the releases page](https://github.com/AquaticInformatics/aquarius-field-data-framework/releases/latest).

See [the wiki page](https://github.com/AquaticInformatics/aquarius-field-data-framework/wiki/MultiFile-plugin) for more details on the types of configuration that the MultiFile plugin may need.

# Why does this tool exist?

We don't really want to create yet-another-tool, but since the [MultiFile](../MultiFile) plugin needs to exist to solve the same-day-same-location-different-plugins problem,
there needs to be some way to easily configure the MultiFile plugin so that it works as customers expect.

If you run this tool and give it credentials to your app server, the tool will create a valid configuration setting which allows MultiFile to:
- invoke the other enabled plugins.
- using the same configured priority order.
- with the same customer-specific settings.

Some plugins, like the popular [TabularCsv plugin](https://github.com/AquaticInformatics/tabular-field-data-plugin#tabular-csv-field-data-plugin), require a customer-specific setting to be of any use at all.

So if you want to use MultiFile to import data using the TabularCsv plugin, you will need to duplicate all of your TOML settings so that the MultiFile plugin can pass them along when it launches TabularCsv as a delegate plugin.

# Use case 1 - MultiFile on the app server

This is main expected use case, where a customer creates a ZIP of two or more field visit files with data for the same day and location.

The customer is using MultiFile to ensure that all the same-day-same-location data from all the files just create a single visit, instead of one visit per file.

Just launch the tool with credentials for your app server, and the tool will create a setting with **Group**=`FieldDataPluginConfig-MultiFile` **Key**=`Config` and a Value property generated from all the enabled and configured plugins on the app server.

```sh
MultiFile.Configurator.exe -server=doug-vm2019

11:58:07.532 INFO  - Connecting to doug-vm2019 ...
11:58:16.161 INFO  - Connected to doug-vm2019 (2022.1.85.0) as admin
11:58:16.306 INFO  - Saving generated configuration to the app server ...
11:58:16.308 INFO  - Creating new MultiFile Config setting ...
11:58:16.471 INFO  - Saved generated configuration to the app server.
```

# Use case 2 - MultiFile running externally

This use case is typical for the [FieldVisitHotFolderService](../FieldVisitHotFolderService), where the FVHFS is running some plugins locally, outside of the app server.

The `-GenerateForExternalUse=true` and `-JsonPath=`*somePath* options are used to generate a JSON file that can be fed into the FVHFS for its local copy of MultiFile. The JSON will contain the plugin priorities and settings from all enabled plugins on the AQTS app server.

```
MultiFile.Configurator.exe -server=doug-vm2019 -GenerateForExternalUse=true -JsonPath=MySettings.json

14:42:31.813 WARN  - Disabling /SaveOnServer=true since /GenerateForExternalUse=true
14:42:31.825 INFO  - Connecting to doug-vm2019 ...
14:42:32.423 INFO  - Connected to doug-vm2019 (2022.1.85.0) as admin
14:42:32.502 INFO  - Saved generated configuration to 'MySettings.json'.
```

To use this JSON file in the `/PluginSettings=MultiFile=Config=@somepath\MySettings.json` option to given that configuration to the MultiFile plugin running within the FVHFS.

See the [Configuring your plugin settings](./FieldVisitHotFolderService#configuring-your-plugin-settings) topic for more details.

## Usage

```
Create a MultiFile configuration setting from an AQTS app server's current configuration.

Usage: MultiFile.Configurator [-option=value] [@optionsFile] ...

Supported -option=value settings (/option=value works too):

  =============================== AQTS app server credentials
  -Server                         AQTS server
  -Username                       AQTS username
  -Password                       AQTS password

  =============================== Generator settings
  -IncludeDisabledPluginSettings  If true, include the settings for currently disabled plugins in the generated configuration. [default: False]
  -SaveOnServer                   If true, save the generated configuration as a MultiFile plugin setting on the server. [default: True]
  -GenerateForExternalUse         If true, generate a config for external use, in the PluginTester or FieldVisitHotFolderService. [default: False]
  -JsonPath                       If set, save the generated configuration to this file.

Use the @optionsFile syntax to read more options from a file.

  Each line in the file is treated as a command line option.
  Blank lines and leading/trailing whitespace is ignored.
  Comment lines begin with a # or // marker.
```