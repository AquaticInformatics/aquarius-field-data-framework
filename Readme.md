# AQUARIUS Time-Series Field Data Framework

[![Build status](https://ci.appveyor.com/api/projects/status/eyoi121elrhtynw3/branch/master?svg=true)](https://ci.appveyor.com/project/SystemsAdministrator/aquarius-field-data-framework/branch/master)

The Field Data Framework for AQUARIUS Time-Series provides an SDK which can be used to develop custom field visit file parsers to work with AQUARIUS Time-Series 2017.4-and-higher.

## Need to install a plugin on your AQTS app server?

If you find yourself here because you need to install a plugin on your server, then you should: 
- Download the latest `FieldDataPluginTool.zip` archive from [here](../../releases/latest)
- Extract the archive on your AQTS app server
- Run the `FieldDataPluginTool.exe` as Administrator
- Click the "Add..." button and select your plugin file to install

See the [FieldDataPluginTool](./src/FieldDataPluginTool/Readme.md) page for full details.

You can skip reading the remaining sections below, since they are intended for software developers using the framework to developer their own custom field data plugins.

## Useful tools ready for download

Each [release](../../releases/latest) of the framework also includes some tools useful to AQTS customers, without requiring any programming knowledge.

- The [FieldDataPluginTool](./src/FieldDataPluginTool/Readme.md) is a GUI tool which can be used to install plugins on your AQTS app server.
- The [FieldVisitHotFolderService](./src/FieldVisitHotFolderService/Readme.md) is a Windows service which can import field visit files dropped into a monitored hot folder.
- The [JSON field data](./src/JsonFieldData/Readme.md) plugin can import field data stored in a JSON format. It is often used with the hot folder service.
- The [Multi-file](./src/MultiFile/Readme.md) plugin can import a ZIP archive of files supported by other installed plugins.

## Installing the framework SDK

This framework is available as a NuGet package.

```Powershell
PM> Install-Package Aquarius.FieldDataFramework
```

Adding the `Aquarius.FieldDataFramework` as a dependency to your .NET project will install:
- The `FieldDataPluginFramework` assembly, which contains the `IFieldDataPlugin` interface every plugin must implement.
- The [`PluginTester.exe`](src/PluginTester) tool, useful for testing your plugin from within Visual Studio.
- The [`PluginPackager.exe`](src/PluginPackager) tool, used during a build process to package your plugin into a single `*.plugin` file for easy deployment.
- The [`FieldDataPluginTool.exe`](src/FieldDataPluginTool) tool, used by customers to install and manage field data plugins on their AQTS app servers.

## Documentation

A developer guide is available [here.](docs/)

## Getting Help

See the [Wiki](https://github.com/AquaticInformatics/aquarius-field-data-framework/wiki) for answers to common questions.

## Contributing

Contributions are always welcome, no matter how large or small. Before contributing, please read the [code of conduct](CODE_OF_CONDUCT.md).

See [Contributing](CONTRIBUTING.md).
