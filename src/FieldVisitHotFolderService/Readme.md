# Field Visit Hot Folder Service

Download the latest version of the Field Visit Hot Folder Service [from the releases page](https://github.com/AquaticInformatics/aquarius-field-data-framework/releases/latest).

The `FieldVisitHotFolderService.exe` tool is a .NET console tool which can:
- Run from the command line and Ctrl-C to exit,  or run as a Windows service.
- Monitor a hot folder path for new files
- Try to parse the files using a set of plugins running locally on the client
- Upload any created visits to AQTS using the [JSON plugin](../JsonFieldData/Readme.md)

The service can be run on any Windows system with the .NET 4.7.2 runtime. This includes every Windows 10 desktop and Windows 2016 Server, and any Windows 7 desktop and Windows 2008 R2 Server with the most recent Windows Update patches.

![Field Visit Hot Folder Service](images/FieldVisitHotFolderService.svg "Field Visit Hot Folder Service")

# Installing the service

- Download the `FieldVisitHotFolderService.zip` archive from the [releases page](https://github.com/AquaticInformatics/aquarius-field-data-framework/releases/latest) and unzip the contents into a folder.
- Run the `InstallService.cmd` batch file with elevated privileges to install the service.
- Run the `UninstallService.cmd` batch file to uninstall the service.
- The service expects an `Options.txt` file to exist in the same folder as the EXE.
- The `Options.txt` file uses the [@options.txt syntax](https://github.com/AquaticInformatics/examples/wiki/Common-command-line-options) to store its configuration options.

# Configuring the service

Create an `Options.txt` file in the same folder as the EXE, to contain all the configuration options.

The following 9 lines are a good start for the `Options.txt` file:
```
# Enter your AQTS credentials here.
# The account should have "CanAddData" permission in all locations in order to create field visits.
/Server=myAppServer
/Username=hotfolderuser
/Password=pass123

# Configure the hot folder path to monitor
/HotFolderPath=D:\Some Folder\Drop Field Visit Files Here
```

You can test your configuration without installing the service just by running the EXE directly from the command line or by double-cliking it from Windows Explorer.

When not run as Windows Service, the program will run until you type Ctrl-C or Ctrl-Break to exit.

## Having the service monitor a network share

Windows services like `FieldVisitHotFolderService` are normally installed to run using the built-in "Local System" account, which is a lower-permissions account which cannot read files from network shares located on other computers.

If your configured `/HotFolderPath=` setting is located on another system, like the UNC path `/HotFolderPath=//OfficeFiles/FieldVisits`, then you will need to change the service's "Log On" property to a network account with permissions to access and modify files in that folder.

![Picture](images/ChangeServiceLogOn.png)

## Adding the local plugins

The service requires local plugins, so that it can inspect the field data locally, and determine if a conflicting visit already exists at the location in AQTS.

It expects to file `*.plugin` files in the `LocalPlugins` folder where the service runs.

Just download the plugins you need from http://source.aquaticinformatics.com and copy the `*.plugin` files to this folder before starting the service.

## Be sure to install the JSON plugin on the AQTS server

In addition to using the plugins from the `LocalPlugins` folder to parse incoming files,
the service also requires the [JSON plugin](../JsonFieldData/Readme.md) to be installed on the AQTS app server.
The service will upload all of its locally parsed field visit activities to AQTS in JSON format,
according to the configured `/MergeMode` setting.

## Folder configuration

There are six configurable folders which are used to process field visit files.

- The `/HotFolderPath` option is the folder that will be watched for new files.
- When a new file is detected, it is moved to the `/ProcessingFolder` while it is being processed.
- After processing, the file will be moved to the `/UploadedFolder` if it can successful upload all of its results to AQTS.
- The file will be moved to the `/PartialFolder` when at least one visit was skipped with a `WARN` to avoid duplicates in `/MergeMode=Skip`.
- The `/ArchiveFolder` will receive copies of visits being replaced when `/MergeMode=ArchiveAndReplace` is enabled.
- Otherwise processed file will be moved to the `/FailedFolder` if it fails to upload anything to AQTS.

The `/ProcessingFolder`, `/UploadedFolder`, `/PartialFolder`, `/ArchivedFolder` and `/FailedFolder` can be absolute paths, or can be paths relative to the base `/HotFolderPath` folder. The folders will be created if they don't already exist.

- These folders can be local to the computer running `FieldVisitHotFolderService.exe`, or can be a UNC network path.
- The program will need file system rights to read, write, delete, and move files in all of these folder locations.

## Controlling the `/MergeMode` behaviour

AQTS does not currently allow a plugin to add any new field visit activities to a location when field visit activities already exist on the same day.
Attempts to upload such files will result in the dreaded `Saving parsed data would result in duplicates` error message.

The `/MergeMode` option controls how the service behaves when a file contains activities which occur on the same day as a visit already in the AQTS system.

| `/MergeMode=<option>` | Description |
| --- | --- |
| `/MergeMode=Skip` | `Skip` mode is the default behaviour.<br/><br/>Any new activities which conflict with an existing AQTS visit will be skipped, and the remaining non-conflicting activities will be be uploaded as new visits to AQTS.<br/><br/>Once processed, the input file will be moved to the `/PartialFolder`, along with its activity log. |
| `/MergeMode=Fail` | If any of the new activities conflict with any existing visits, then **none of the new activities** will be uploaded to AQTS. The file will be considered as a failure to upload.<br/><br/>Once processed, the input file will be moved to the `/FailedFolder`, along with its activity log. |
| `/MergeMode=Replace` | If a new activity conflicts with an existing AQTS visit, **the existing AQTS visit will be deleted without confirmation**, and a new activities will be uploaded to AQTS.<br/><br/>Please use caution with this option, as the delete is a destructive operation which cannot be undone.<br/><br/>Once processed, the input file will be moved to the `/UploadedFolder`, along with its activity log. |
| `/MergeMode=ArchiveAndReplace` | Same as `/MergeMode=Replace`, but existing visits are archived to the `/ArchiveFolder` before being deleted. |

# Operation

- The service will monitor the `HotFolderPath` for files matching the `FileMask` pattern.
- When a new file is detected, the service will wait for the `FileQuietDelay`, before attempting to process the file.
- The file content will be loaded into memory, and passed to each `/Plugin` option, until a plugin returns a successful parse result.
- For every successfully parsed visit, the service will check if a visit already exists on that day at the requested location. These "conflicting visits" will be skipped with a `WARN` log line, but will not cause a failure.
- If no plugins can parse the file successfully, or if an upload error occurs, the file will be considered to have failed, and will be moved to the `/FailedFolder`.

## How can I tell if the hot folder service is running?

When the service is running, a special file named `_ServiceIsRunning_.txt` will be created in the `/HotFolderPath` folder.

When the service has been stopped, this special file will be automatically deleted.

This will help network users know if the service is actually monitoring the folder for incoming files.

## Conditions which can cause a file to be "failed"

Any of these conditions will cause the file to be considered failed, and will be moved to the `/FailedFolder`:
- The file was not recognized by one of the local `/Plugin` parsers.
- The location identifiers referenced in the file do not exist on the AQTS system.
- A validation error occurred when the visit was uploaded to AQTS.

## "Partial uploads" - Conflicting visits on the same day cannot be overwritten

AQTS does not allow 2 visits on the same day in a location to have any overlap between the start and end times, and does not allow visit data from a plugin to be merged with a visit already existing in AQTS.

So before the service uploads a locally-parsed visit, it checks if the location already has a visit on the same day.
If an existing visit is detected, the service will not attempt to upload the conflicting visit.
The upload will be skipped and logged with a `WARN` line, but will not cause the file processing to fail.
The file will be moved to the `/PartialFolder`.

The `/OverlapIncludesWholeDay` option controls how strictly the overlap is enforced by the  hot folder service.
- `/OverlapIncludesWholeDay=false` is the default option, and only considers strict overlaps based on the visit start and end times. This setting can result in multiple, non-overlapping visits on the same day.
    - Eg. When a visit already exists on Tuesday from 2 PM to 5 PM, a new Tuesday visit from 8AM to 9AM will be uploaded successfully, without triggering any partial upload logic.
- `/OverlapIncludesWholeDay=true` will consider the entire day of visit when checking for merge conflicts. This mode will ensure at most one visit per day per location.
    - Eg. When a visit already exists on Tuesday from 2 PM to 5 PM, a new Tuesday visit from 8AM to 9AM will be considered a conflict, and the `/MergeMode` logic will be triggered.
    
This feature allows the hot folder to consume files which are repeatedly generated via an append operation, containing only new data appended to the end of the file.

## Detecting a valid AQTS app server connection

When the service is started, and immediately before processing any detected files, the following logic will be applied:

- If no local plugins are configured with the `/Plugin=` option, the service will exit.
- If the AQTS app server is not running, the service will wait for the `ConnectionRetryDelay`, for up to `MaximumConnectionAttempts`.
- If the maximum connection attempts is reached without a successful connection, the service will stop.
- If `MaximumConnectionAttempts` is less than 1, the service will wait repeatedly until the app server is running, or the service is manually stopped.
- Once a valid AQTS connection is established, a few more configuration inspections are made.
- If the AQTS server is not running AQTS 2018.4-or-newer, the service will exit.
- If the AQTS server does not have the [JSON field data plugin](../JsonFieldData/Readme.md) installed, the service will exit.

# Log files

The service creates a `FieldVisitHotFolderService.log` file in the same directory as the EXE.

In addition, each processed file will have a `{filename}.log` file in the appropriate `/UploadedFolder`, `/PartialFolder`, or `/FailedFolder`, for debugging purposes.

# /Help screen

```
Purpose: Monitors a folder for field visit files and appends them to an AQTS app server

Usage: FieldVisitHotFolderService [-option=value] [@optionsFile] ...

Supported -option=value settings (/option=value works too):

  =========================== AQTS connection settings
  -Server                     The AQTS app server.
  -Username                   AQTS username. [default: admin]
  -Password                   AQTS credentials. [default: admin]
  -MaximumConnectionAttempts  The maximum number of connection attempts before exiting. [default: 3]
  -ConnectionRetryDelay       The TimeSpan to wait in between AQTS connection attempts. [default: 00:01:00]
  -MaximumConcurrentRequests  Maximum concurrent requests during field visit import. [default: 16]

  =========================== Visit merge settings
  -MergeMode                  One of Skip, Fail, Replace, ArchiveAndReplace. [default: Skip]
  -OverlapIncludesWholeDay    True if a conflict includes any visit on same day. False can generate multiple visits on the same day. [default: False]

  =========================== File monitoring settings
  -HotFolderPath              The root path to monitor for field visit files.
  -FileMask                   A comma-separated list of file patterns to monitor. [defaults to '*.*' if omitted]
  -FileQuietDelay             Timespan of no file activity before processing begins. [default: 00:00:05]
  -ProcessingFolder           Move files to this folder during processing. [default: Processing]
  -UploadedFolder             Move files to this folder after successful uploads. [default: Uploaded]
  -PartialFolder              Move files to this folder if when partial uploads are performed to avoid duplicates. [default: PartialUploads]
  -ArchivedFolder             Any visits replaced via /MergeMode=ArchiveAndReplace will be archived here before being replace with new visits. [default: Archived]
  -FailedFolder               Move files to this folder if an upload error occurs. [default: Failed]

Use the @optionsFile syntax to read more options from a file.

  Each line in the file is treated as a command line option.
  Blank lines and leading/trailing whitespace is ignored.
  Comment lines begin with a # or // marker.
```
