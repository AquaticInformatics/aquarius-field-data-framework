## Aquarius.FieldDataFramework Release Notes

This page highlights some changes in the field data framework.

Not all changes will be listed, but you can always [compare by version tags](https://github.com/AquaticInformatics/aquarius-field-data-framework/compare/v17.4.1...v17.4.0) to see the full source code difference.

### 20.2.1
- FieldVisitHotFolderService now proxies any plugin configuration from the AQTS app server by default.

### 20.2.0
- Added support for AQTS 2020.2 plugins.
- See [2020.2 release notes](docs#aqts-20202---framework-version-29) for details of new features.

### 19.4.12
- MultiFile plugin: Fixed a bug, so that inspection and calibration activity data is now merged from multiple plugins.

### 19.4.5
- Each visit file uploaded by FieldVisitHotFolderService now contains the location identifier and start date in the filename

### 19.4.4
- Fixed ZIPs-with-attachments for the FieldVisitHotFolderService

### 19.4.3
- Added support for AQTS 2019.4 Update 1 plugins
- See [2019.4 Update 1 release notes](docs#aqts-20194-update-1---framework-version-27) for details of new features.

### 19.4.2
- Added support for ZIPs-with-attachments to PluginTester and FieldVisitHotFolderService

### 19.4.1
- No external changes, just some internal build plumbing fixes.

### 19.4.0
- Added support for AQTS 2019.4 plugins
- See [2019.4 release notes](docs#aqts-20194---framework-version-26) for details of new features.

### 19.3.3
- FieldVisitHotFolderService now enforces the `/MaximumVisitDuration=` setting, which defaults to 1 day 6 hours. This setting helps catch poorly composed FV data before it ends up in your AQTS system.
- FieldVisitHotFolderService now supports a `/DryRun=true` option for easier debugging of configurations. No data changes will be made to your AQTS system.

### 19.3.2
- FieldVisitHotFolderService now exits with "success" when visits rejected as duplicates succeed after retrying.

### 19.3.1
- FieldVisitHotFolderService now supports location aliases

### 19.3.0
- Added support for AQTS 2019.3 plugins
- See [2019.3 release notes](docs#aqts-20193---framework-version-25) for details of new features.
 
### 19.2.2
- FieldVisitHotFolderService: Improved the automatic deployment of the JsonFieldData plugin

### 19.2.1
- JsonFieldData: Fixed a JSON serialization bug for the DischargeActivity.MeasurementGrade property

### 19.2.0
- Added support for AQTS 2019.2 plugins
- See [2019.2 release notes](docs#aqts-20192---framework-version-23) for details of new features.

### 19.1.0
- Added support for AQTS 2019.1 plugins
- See [2019.1 release notes](docs#aqts-20191---framework-version-21) for details of new features.

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
- See [2018.4 release notes](docs#aqts-20184---framework-version-20) for details of new features.

### 18.3.1
- Added "missing plugin" detection logic to the Field Data Plugin Tool.

### 18.3.0
- Added support for AQTS 2018.3 plugins
- See [2018.3 release notes](docs#aqts-20183---framework-version-13) for details of new features.

### 18.2.0
- Added support for AQTS 2018.2 plugins
- See [2018.2 release notes](docs#aqts-20182---framework-version-12) for details of new features.

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
