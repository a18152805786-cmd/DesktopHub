# Desktop Hub baseline build notes

```text
BuildCommand=& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "Code\Desktop Frames.sln" /t:Build /p:Configuration=Release /p:Platform="Any CPU" /v:minimal
Configuration=Release
Platform=Any CPU
BuildSucceeded=true
Errors=0
OutputBinary=C:\Users\71966\Desktop\DesktopHub\Code\Desktop Frames\bin\Release\net8.0-windows7.0\Desktop Frames.exe
OutputSHA256=6FDA761AA4559284729E2137EE6F6D46F943815FB286050EB8E6628DE081C7CA
```

The first `dotnet build` attempt failed with `MSB4803` because the .NET SDK MSBuild cannot run `ResolveComReference`. Re-running with the installed full-framework Visual Studio MSBuild succeeded. The project source and dependency versions were left unchanged.

The executable was not run. Runtime validation is a separate blocked item, not a build failure.
