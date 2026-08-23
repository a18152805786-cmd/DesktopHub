# Build environment and verification

## Local environment

| Item | Result |
|---|---|
| OS | Windows 10/11-compatible Windows build host; `dotnet --info` reported RID `win-x64` |
| .NET SDKs | `9.0.315`, `9.0.316`, `10.0.301` |
| Installed Windows Desktop runtimes | .NET 8.0.14/8.0.29, .NET 9.x, .NET 10.x |
| Target framework | `net8.0-windows7.0` |
| Visual Studio tool | Visual Studio 2022 Build Tools, installation version `17.14.37516.0` |
| Full-framework MSBuild | `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe` |
| MSBuild version used | `17.14.51+25f168cee` |
| Windows SDK | No `Windows Kits\10` directory or Windows 10/11 SDK component was detected; .NET WindowsDesktop reference packs are installed and the build did not require an SDK install |
| Missing dependency | none blocking; no installation performed |

The machine has `NETFXSDK` 4.7.2 and 4.8 directories, but not a Windows 10/11 SDK tree. The project targets the .NET Windows Desktop pack and `windows7.0`, which was sufficient for this build.

## Build attempts

### Attempt 1: .NET SDK MSBuild

```text
dotnet build "Code\Desktop Frames.sln" --configuration Release --verbosity minimal
```

Result: failed before compilation with `MSB4803`; the .NET Core MSBuild implementation does not support the project's `ResolveComReference` task. This was a tool selection issue, not a source or dependency failure.

### Attempt 2: full-framework Visual Studio MSBuild

```text
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" `
  "Code\Desktop Frames.sln" /t:Build /p:Configuration=Release /p:Platform="Any CPU" /v:minimal
```

| Field | Result |
|---|---|
| Configuration | `Release` |
| Platform | `Any CPU` |
| BuildSucceeded | `true` |
| Errors | `0` |
| Warnings | The final verification emitted two MSBuild COM-reference warnings (`MSB3284`, `MSB3290`); the initial full compile also emitted existing nullable/unused-code compiler warnings. No warning cleanup or unrelated refactor was performed. |
| Output assembly | `Code/Desktop Frames/bin/Release/net8.0-windows7.0/Desktop Frames.dll` |
| Output executable | `C:\Users\71966\Desktop\DesktopHub\Code\Desktop Frames\bin\Release\net8.0-windows7.0\Desktop Frames.exe` |
| Executable SHA-256 | `6FDA761AA4559284729E2137EE6F6D46F943815FB286050EB8E6628DE081C7CA` |

The output directory contains the executable, .NET runtime configuration, NAudio assemblies, and Newtonsoft.Json. `bin/` and `obj/` are ignored by the upstream `.gitignore`; they were not committed.

## Dependencies resolved without changes

Top-level packages:

```text
NAudio 2.2.1
Newtonsoft.Json 13.0.1
```

Resolved transitive packages included `NAudio.Asio/Core/Midi/Wasapi/WinForms/WinMM 2.2.1`, `Microsoft.NETCore.Platforms 3.1.0`, `Microsoft.Win32.Registry 4.7.0`, `System.Security.AccessControl 4.7.0`, and `System.Security.Principal.Windows 4.7.0`. No package was upgraded or added.

## Runtime gate

`ExecutableLocated=true` and `BuildSucceeded=true`. `LaunchValidated=false`: actual launch was intentionally skipped because the startup path is not compatible with this phase's no-registry/no-desktop-side-effect rule. See the system-integration audit for the call chain.
