# AQUARIUS Time-Series Field Data Framework

[![Build status](https://ci.appveyor.com/api/projects/status/eyoi121elrhtynw3/branch/master?svg=true)](https://ci.appveyor.com/project/SystemsAdministrator/aquarius-field-data-framework/branch/master)

The Field Data Framework for AQUARIUS Time-Series provides an SDK which can be used to develop custom field visit file parsers to work with AQUARIUS Time-Series 2017.4-and-higher.

## Installing the framework SDK

This framework is available as a NuGet package.

```Powershell
PM> Install-Package Aquarius.FieldDataFramework
```

Adding the `Aquarius.FieldDataFramework` as a dependency to your .NEt project will install:
- The `FieldDataPluginFramework` assembly, which contains the `IFieldDataPlugin` interface every plugin must implement.
- The `PluginTester.exe` tool, useful for testing your plugin from within Visual Studio.
- The `PluginPackager.exe` tool, used during a build process to package your plugin into a single `*.plugin` file for easy deployment.
- The `FieldDataPluginTool.exe` tool, used by customers to install and manage field data plugins on their AQTS app servers.

## Documentation

A developer guide is available [here.](docs/AQUARIUSDeveloperGuideFieldDataPluginFramework.pdf)

## Getting Help

See the [Wiki](https://github.com/AquaticInformatics/aquarius-field-data-framework/wiki) for answers to common questions.

## Contributing

Contributions are always welcome, no matter how large or small. Before contributing, please read the [code of conduct](CODE_OF_CONDUCT.md).

See [Contributing](CONTRIBUTING.md).