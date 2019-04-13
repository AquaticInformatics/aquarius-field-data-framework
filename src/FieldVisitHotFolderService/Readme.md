# Field Visit Hot Folder Service

The `FieldVisitHotFolderService.exe` tool is a .NET console tool which can:
- Run from the command line and Ctrl-C to exit,  or run as a Windows service.
- Monitor a hot folder path for new files
- Try to parse the files using a set of plugins
- Upload any created visits to AQTS using the [JSON plugin](https://github.com/AquaticInformatics/json-field-data-plugin)

# Installing the service

- Run the `InstallService.cmd` batch file with elevated privileges to install the service.
- Run the `UninstallService.cmd` batch file to uninstall the service.
- The service expects an `Options.txt` file to exist in the same folder as the EXE.
- The `Options.txt` file uses the [@options.txt syntax](https://github.com/AquaticInformatics/examples/wiki/Common-command-line-options) to store its configuration options.

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

  =========================== Local plugin settings
  -Plugin                     A plugin assembly to use for parsing field visits locally. Can be set multiple times.

  =========================== File monitoring settings
  -HotFolderPath              The root path to monitor for field visit files.
  -FileMask                   A comma-separated list of file patterns to monitor. [defaults to '*.*' if omitted]
  -FileQuietDelay             Timespan of no file activity before processing begins. [default: 00:00:05]
  -ProcessingFolder           Move files to this folder during processing. [default: Processing]
  -UploadedFolder             Move files to this folder after successful uploads. [default: Uploaded]
  -FailedFolder               Move files to this folder if an upload error occurs. [default: Failed]

Use the @optionsFile syntax to read more options from a file.

  Each line in the file is treated as a command line option.
  Blank lines and leading/trailing whitespace is ignored.
```
