# Multi-file field data plugin

The Multi-file field data plugin is a plugin for AQTS 2018.4-or-newer which can create same-day field visits from data parsed by separate plugins.

Download the latest version of the MultiFile plugin [from the releases page](https://github.com/AquaticInformatics/aquarius-field-data-framework/releases/latest).

If all the data files are combined into a single ZIP archive, this plugin will try parsing the files using all the other plugins installed on the AQTS server.

If each the files within the ZIP archive can be parsed by one the other plugins, then the import will succeed.

This plugin allows you to:
- Go into the field on Wednesday
- Take a discharge measurement using a [FlowTracker2](https://github.com/AquaticInformatics/flowtracker2-field-data-plugin) device, saved as `WednesdayDischarge.ft`
- Take other environmental reasings (like Air temperature, and battery voltage), saved in the [StageDischargeReadings CSV format](https://github.com/AquaticInformatics/stage-discharge-readings-field-data-plugin) as 'WednesdayReadings.csv`
- Combine both files into `Wednesday.zip`
- Upload `Wednesday.zip` to AQTS, and see the Wednesday visit with both the discharge measurement and the on-site readings.
