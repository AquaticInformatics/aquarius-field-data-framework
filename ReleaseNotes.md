## Aquarius.FieldDataFramework Release Notes

This page highlights some changes in the field data framework.

Not all changes will be listed, but you can always [compare by version tags](https://github.com/AquaticInformatics/aquarius-field-data-framework/compare/v17.4.1...v17.4.0) to see the full source code difference.
### 25.2.5
- Added support for AQTS 2025.2 plugins
- See [2025.2  release notes](docs#aqts-20252---framework-version-215) for details of new features.

### 23.4.2
- Updated ServiceStack framework libraries to v6 to support AQTS 2023.4

### 21.4.10
- MultiFile - Bugfix - Allow delegate plugin settings to be configured.

### 21.4.8
- JsonFieldData - Bugfix - Added support for GageAtZeroFlow activity (missing since the 20.3 release)
- MultiFile - Bugfix - Added support for GageAtZeroFlow activity (missing since the 20.3 release)

### 21.4.7
- Improved support for internal MigrationProject format.

### 21.4.3
- Added support for AQTS 2021.4 Update 1 plugins.
- See [2021.4 Update 1 release notes](docs#aqts-20214-update-1---framework-version-214) for details of new features.

### 21.4.2
- MultiFile - Merge the CompletedActivities boolean properties of overlapped visits

### 21.4.0
- Added support for AQTS 2021.4 plugins.
- See [2021.4 release notes](docs#aqts-20214---framework-version-213) for details of new features.

### 21.3.2
- FieldVisitHotFolderService - Improved export of new 2021.3 field visit properties

### 21.3.1
- FieldVisitHotFolderService - Improved visit export logic when visit attachments aren't found in S3 storage buckets
- JsonFieldData - Improved plugin bundling

### 21.3.0
- Added support for AQTS 2021.3 Update 1 plugins.
- See [2021.3 Update 1 release notes](docs#aqts-20213-update-1---framework-version-212) for details of new features.

### 20.3.11
- PluginTester & FieldVisitHotFolderService - Improved loading of plugins from *.plugin packages. -Verbose=true enables detailed assembly resolution tracing logic.

### 20.3.9
- FieldVisitHotFolderService - Added [visit export](tree/master/src/FieldVisitHotFolderService#exporting-existing-field-visits) functionality

### 20.3.8
- FieldVisitHotFolderService - Added -PluginPriority configuration options
- FieldVisitHotFolderService - Bugfix: Ensure JSON serialization is initialized before uploading any visit data
- JsonFieldData - Updated to latest ServiceStack.Text JSON parser
- MultiFile - Updated to latest ServiceStack.Text JSON parser.

### 20.3.7
- Addressed [CVE-2018-1285](https://github.com/advisories/GHSA-2cwj-8chv-9pp9) by updating the log4net dependency to 2.0.12. None of the previously-released tools were affected by the vulnerability, but we have updated the dependency to the newer version anyways.
- Removed the obsolete FieldDataPluginTool

### 20.3.6
- No external changes, just some internal build plumbing fixes.

### 20.3.5
- FieldVisitHotFolderService - Improved error logging and ensure that AQTS 2020.3-or-newer is running.

### 20.3.1
- Fixed a serialization bug for Engineered structures

### 20.3.0
- Added support for AQTS 2020.3 plugins.
- See [2020.3 release notes](docs#aqts-20203---framework-version-210) for details of new features.

### 20.2.5
- FieldVisitHotFolderService - Support /MergeMode=AllowSamDayVisits for AQTS 2020.1-and-newer
- Added complete support for inspections and calibrations to: JsonFieldData plugin, MultiFile plugin, and FieldVisitHotFolderService.

### 20.2.1
- FieldVisitHotFolderService now proxies any plugin configuration from the AQTS app server by default.

### 20.2.0
- Added support for AQTS 2020.2 plugins.
- See [2020.2 release notes](docs#aqts-20202---framework-version-29) for details of new features.

### 19.4.14
- FieldVisitHotFolderService - Support /MergeMode=AllowSamDayVisits for AQTS 2020.1-and-newer
- Added complete support for inspections and calibrations to: JsonFieldData plugin, MultiFile plugin, and FieldVisitHotFolderService.

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
