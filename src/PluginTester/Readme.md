# PluginTester - No AQTS Server needed!

The `PluginTester.exe` console app allows you to run your field data plugin outside of the AQUARIUS Time-Series server environment, for easier debugging and validation.

- Can be run from CMD.EXE, PowerShell, or a bash shell.
- Can be run from within Visual Studio, to allow step-by=step debugging of you plugin.
- An exit code of 0 means the file was successfully parsed by the plugin.
- An exit code of 1 means "something went wrong".
- Everything gets logged to `PluginTester.log`
- Any appended results from the plugin can be saved to a JSON file.

## Usage

```
Parse a file using a field data plugin, logging the results.

usage: PluginTester [-option=value] ...

Supported -option=value settings (/option=value works too):

  -Plugin               Path to the plugin assembly to debug
  -Data                 Path to the data file to be parsed. Can be set more than once.
  -Location             Optional location identifier context
  -UtcOffset            UTC offset in .NET TimeSpan format. [default: -08:00:00]
  -Json                 Optional path to write the appended results as JSON
  -ExpectedError        Expected error message
  -ExpectedStatus       Expected parse status. One of SuccessfullyParsedButDataInvalid, SuccessfullyParsedAndDataValid, CannotParse [default: SuccessfullyParsedAndDataValid]
```

## Saving JSON results

The `/Json=outputPath` option can be specified to save the resulting framework DTOs to a JSON document.

This JSON document can be very useful to debug complex logic within a plugin, since it will represent the exact data being sent to the framework when your plugin runs on your AQUARIUS Time Series app server.

This JSON document will contain the [AppendedResults](../FieldDataPluginFramework.Serialization/AppendedResults.cs) content:
- The version information of the framework used to launch the plugin.
- The version information of the plugin used to parse the data file.
- A list of `FieldVisitInfo` objects parsed by the plugin.

This JSON document can also be parsed by the [JSON field data plugin](../JsonFieldData/Readme.md) and it can be sent to the AQUARIUS Support team for troubleshooting.

### Logging

The tester uses `log4net` to log to both the console and to the `PluginTester.log` file.

Log statements from the tester itself are easily distinguished from log statements from the plugin being tested.

## Invoking the tester as a post-build event in Visual Studio

This post-build event will test that your plugin can successfully parse the `data\somefile.ext` file. If a parsing error is detected, the build will be marked as failed.

```
$(SolutionDir)packages\Aquarius.FieldDataFramework.18.4.0\tools\PluginTester.exe /Plugin=$(TargetPath) /Data=$(SolutionDir)..\data\somefile.ext
```

### How does my build know if the tester has failed?

The `PluginTester.exe` tool follows standard exit code conventions. Zero means success, and any positive exit codes means an error occurred.

Visual Studio post-build events will detect any non-zero exit codes and indicate a failed parse attempt by your plugin.

## Using PluginTester for integration tests

You can leverage two features of `PluginTester` to build an automated test suite for your plugin.

You can even test for error conditions using the `/ExpectedStatus` and `/ExpectedError` options.

1. An exit code of 0 means "The plugin parsed the file as expected".

Any other exit code means something went wrong. Use the exit code to determine if the file was parsed.

```sh
$ PluginTester.exe -Plugin=MyPlugin.dll -Data=data.csv -Json=results.json || echo "Did not parser data.csv"
```

2. Saving the appended results to JSON should always yield the identical output.

```sh
#!/bin/bash

# Helper function
exit_abort () {
    [ ! -z "$1" ] && echo ERROR: "$1"
    echo
    echo 'ABORTED!'
    echo
    exit $ERRCODE
}

PluginTester=../some/path/PluginTester.exe
PluginPath=some/other/path/MyPlugin.dll
DataPath=data.csv
JsonPath=results.json
ExpectedResultsPath=some/path/expected.json

$PluginTester -Plugin=$pluginPath -Data=$DataPath -Json=$JsonPath || exit_abort "Can't parse $DataPath"
cmp $JsonPath $ExpectedResultsPath || exit_abort "Expected output did not match."
```

### Debugging from Visual Studio

Use the `PluginTest.exe` to debug your plugin from within Visual Studio.

1. Open your plugin's **Properties** page
2. Select the **Debug** tab
3. Select **Start external program:** as the start action and browse to `packages\Aquarius.FieldDataFramework.18.4.0\tools\PluginTester.exe`
4. Enter the **Command line arguments:** to launch your plugin

```
/Plugin=<yourPluginAssembly>.dll /Data=a\path\to\sometestfile.ext
```

The `/Plugin=` argument can be the filename of your plugin assembly, without any folder. The default working directory for a start action is the bin folder containing your plugin.

5. Set a breakpoint in your plugin's `ParseFile()` methods.
6. Select your plugin project in Solution Explorer and select **"Debug | Start new instance"**
7. Now you're debugging your plugin!

### Limitations

The tester doesn't fully emulate the plugin framework. It simply exercises the `IFieldDataPlugin` interface and collects the data your plugin tries to append.

- The AQTS framework will perform extensive validation on the data being appended. But the tester doesn't (and can't) perform any of that validation.

#### My plugin seems to run fine in the tester. Why won't my plugin work on AQTS?

When `PluginTester` says "Yup" but AQTS says "Nope" to a file, usually that means a data validation error. Check the `FieldDataPluginFramework.log` on your AQTS server for details.

If the log file doesn't contain an explaination why the data won't upload:
- Use `PluginTester /Json=path.json` option to save the appended data in JSON format.
- Send the JSON file to the SupportTeam @ AquaticInformatics and we'll take a deeper look.

