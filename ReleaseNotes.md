## Aquarius.FieldDataFramework Release Notes

This page highlights some changes in the field data framework.

Not all changes will be listed, but you can always [compare by version tags](https://github.com/AquaticInformatics/aquarius-field-data-framework/compare/v17.4.1...v17.4.0) to see the full source code difference.

### 18.4.18
- Added [Issue #86](https://github.com/AquaticInformatics/aquarius-field-data-framework/issues/86) options `/MaximumFileCount` and `/MaximumFileWaitInterval` to help migration workflows.

### 18.4.16
- The FieldVisitHotFolderService has a few bug fixes:
    - [Issue #84](https://github.com/AquaticInformatics/aquarius-field-data-framework/issues/84) - Added periodic file scan at `/FileScanInterval`
    - [Issue #82](https://github.com/AquaticInformatics/aquarius-field-data-framework/issues/82) - Skip visits for unknown locations
    - [Issue #81](https://github.com/AquaticInformatics/aquarius-field-data-framework/issues/81) - Show when the service is running
    - [Issue #80](https://github.com/AquaticInformatics/aquarius-field-data-framework/issues/80) - Capture all logged messages

### 18.4.14
- There have been a few rapid 18.4.x point releases, as the new JSON plugin, MultiFile plugin, and FieldVisitHotFolderService have stabilized.
- The 2018.4 framework itself posted to NuGet has not changed and functionality since the 18.4.0 release.

### 18.4.7
- Added [JSON plugin](./src/JsonFieldData/Readme.md), [Multi-file plugin](./src/MultiFile/Readme.md), and the [Field visit hot folder service](./src/FieldVisitHotFolderService/Readme.md)

### 18.4.0
- Added support for 2018.4 plugins
- Updated the target .NET framework to .NET 4.7.2 to match the AQTS 2018.4 requirements
- See [2018.4 release notes](docs#aqts-20184) for details of new features.

### 18.3.1
- Added "missing plugin" detection logic to the Field Data Plugin Tool.

### 18.3.0
- Added support for AQTS 2018.3 plugins
- See [2018.3 release notes](docs#aqts-20183) for details of new features.

### 18.2.0
- Added support for AQTS 2018.2 plugins
- See [2018.2 release notes](docs#aqts-20182) for details of new features.

### 18.1.1
- Improved the `FieldDataPluginTool` to always copy the server's framework assembly into the installed plugin folder. This allows plugins written for 2017.4 to work on 2018.1 systems, just by re-installing the plugin. There is no need to recompile the plugin from source code.

### 18.1.0
- Added support for AQTS 2018.1 plugins
- Added support for importing LevelSurvey activities

### 17.4.2
- Added `/ExpectedStatus` and `/ExpectedError` options to `PluginTester.exe`, so that failure conditions of a plugin can also be tested. This can be useful to confirm that your plugin quickly returns `CannotParse` when asked to parse `Kitten.jpg`.

### 17.4.1
- Exit the `PluginTester.exe` with an error code if the plugin detects an error. This enables easy integration testing.

### 17.4.0
- For simplicity, changed the package version scheme to match the compatible AQTS version.
- Fixed a bug in the `PluginPackager.exe` so that a plugin can resolve its .NET dependencies correctly during the packaging step.

### 1.0.1
- Initial public release, compatible with AQTS 2017.4+.
