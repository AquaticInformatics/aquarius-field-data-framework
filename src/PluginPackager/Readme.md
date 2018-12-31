# PluginPackager.exe

PluginPackager.exe is tool that can be used during a build process to create a `*.plugin` file, which can be easily deployed using the [FieldDataPluginTool](../FieldDataPluginTool).

- By default, the packaging tool will bundle up everything in a folder using reasonable defaults.
- The tool is included in the `Aquarius.FieldDataFramework` NuGet package.

## Invoking the packaging step as a post-build event in Visual Studio

This post-build event will create a plugin bundle with the name of the current target. This is usually all you need.

```
$(SolutionDir)packages\Aquarius.FieldDataFramework.17.4.1\tools\PluginPackager.exe $(TargetPath) /OutputPath=$(ProjectDir)deploy\$(Configuration)\$(TargetName).plugin
```

## How does my build know if the packaging step has failed?

The `PluginPackager.exe` tool follows standard exit code conventions. Zero means success, and any positive exit codes means an error occurred.

Visual Studio post-build events will detect any non-zero exit codes and indicate a failed packaging step.

## Packaging by folder

If you set the `/AssemblyFolder=someDir` option or just specify a folder on the command line, the tool will:
- Inspect all the `*.dll` assemblies in the folder
- Fail unless exactly one assembly contains exactly one implementation the `IFieldDataPlugin` interface.
- Automatically set the `AssemblyTypeQualifiedName` from the discovered assembly.

## Packaging by assembly path

If you set the `/AssemblyPath=path` option or just specify a path to an assembly on the command line, the tool will:
- Ensure that the assembly contains  exactly one implementation the `IFieldDataPlugin` interface.
- Automatically set the `AssemblyTypeQualifiedName` from the discovered assembly.

## Other defaults

- The `/DeployedFolder=value` will default to the simplified plugin name unless explicitly set.
- The `/Description=value` will default to simplified plugin name unless explicitly set.

## The `*.plugin` file format

A `*.plugin` file is a ZIP archive containing:
- A `manifest.json` file, which includes the **PluginFolderName**, **Description**, and **AssemblyQualifiedTypeName** string properties. These values are set by the plugin developer and should not need to be changed when a plugin is installed on an AQTS system.
- All other files in the ZIP archive, including any nested folders and files, will be copied to the named folder when the plugin is installed.

A bare-minimum `*.plugin` file contains 2 files:
- The plugin assembly implementing the `IFieldDataPlugin` interface
- The `manifest.json` file
