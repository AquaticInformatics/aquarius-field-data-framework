# JSON Field Data Plugin

The JSON Field Data Plugin is a plugin for AQTS 2018.4-or-newer which reads JSON files and created field visit activities from its contents.

Download the latest version of the JSON Field Data plugin [from the releases page](https://github.com/AquaticInformatics/aquarius-field-data-framework/releases/latest).

The JSON content supported by this plugin is the [AppendedResults](../FieldDataPluginFramework.Serialization/AppendedResults.cs) document DTO.

This is the same JSON format produced by the [PluginTester.exe /Json=outputPath](../PluginTester/Readme.md#saving-json-results) option.

## Outer document

The outer JSON document contains 3 properties:

| Property | Description |
| --- | --- |
| FrameworkAssemblyQualifiedName | The .NET version info of the framework used to create the JSON document. This value will be read to enable any version-specific parsing, if a breaking change is introduced into the framework. If omitted, the current framework version will be assumed. |
| PluginAssemblyQualifiedTypeName | The .NET version of the plugin which originally parsed the file. The value is never read by the JSON plugin. |
| AppendedVisits | A list of `FieldVisitInfo` objects to append into the AQTS server. |

```json
{
    "FrameworkAssemblyQualifiedName": "FieldDataPluginFramework.IFieldDataPlugin, FieldDataPluginFramework, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null",
    "PluginAssemblyQualifiedTypeName": "FlowTracker2Plugin.Plugin, FlowTracker2Plugin, Version=17.4.44.0, Culture=neutral, PublicKeyToken=null",
    "AppendedVisits": [
        ... visit 1 ...,

        ... visit N ...
    ]
}
```

## `FieldDataPluginFramework.dll` objects

This readme won't document all the properties of every object.

Many DTO classes in the `FieldDataPluginFramework.DataModel` namespace have simple getter and setter methods, and those won't be documented here.

But some of the JSON fields written to the document are ignored by the JSON plugin, so those exceptions will be documented here.

## ISO 8601 datetime format

All JSON timestamps are an ISO 8601 timestamp, with 7 fractional seconds digits (100 nanosecond resolution), with a UTC offset.

- `yyyy-mm-ddTHH:MM:SS.FFFFFFF+HH:MM`
- `yyyy-mm-ddTHH:MM:SS.FFFFFFF-HH:MM`
- Note the `T` separating the date and time components.
- All fields are padded leading zeros as needed, and all fields are mandatory, so all timestamps are exactly 33 characters in length.

## Ignored FieldVisitInfo properties

- LocationId
- FieldVisitId
- StartDate (using the FieldVisitInfo.FieldVisitDetails.FieldVisitPeriod.Start value instead)
- EndDate (using the FieldVisitInfo.FieldVisitDetails.FieldVisitPeriod.End value instead)
- Party (using the FieldVisitInfo.FieldVisitDetails.Party value instead)
- FieldVisitIdentifier
- 
## Ignored LocationInfo properties

Only the `LocationInfo.LocationIdentifier` string property is read by the JSON plugin.

The other properties are all ignored, as they are replaced with the true AQTS location properties during import:
- LocationId
- LocationName
- UniqueId
- UtcOffset

## Ignored FieldVisitDetails properties

- StartDate (using the FieldVisitDetails.FieldVisitPeriod.Start value instead)
- EndDate (using the FieldVisitDetails.FieldVisitPeriod.End value instead)

## Ignored DischargeActivity properties

- MeasurementStartTime (using the DischargeActivity.MeasurementPeriod.Start value instead)
- MeasurementEndTime (using the DischargeActivity.MeasurementPeriod.End value instead)

## Ignored ChannelMeasurement properties

- MeasurementStartTime (using the ChannelMeasurementBase.MeasurementPeriod.Start value instead)
- MeasurementEndTime (using the ChannelMeasurementBase.MeasurementPeriod.End value instead)
