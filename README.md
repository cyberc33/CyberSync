# CyberSync
Small sync program to backup source folders onto an external drive.

## Stack
CyberSync is a Windows desktop application built with:

- WinUI 3
- .NET 8
- Windows App SDK

The app stores its configuration under `%APPDATA%\CyberSync`, runs file mirroring with `robocopy`, and creates a daily scheduled task with `schtasks`.

## Requirements for local builds
Install the following on a Windows 10 or Windows 11 machine:

- Visual Studio 2022 or newer
- .NET 8 SDK
- The Visual Studio `WinUI application development` workload
- Developer Mode enabled in Windows

Optional:

- Inno Setup, if you want to build the installer from `installer/CyberSync.iss`

## Install the dependencies
### 1. Install Visual Studio and WinUI tools
Open the Visual Studio Installer and add the `WinUI application development` workload.
This is the Microsoft-recommended workload for C# WinUI 3 development.

### 2. Install the .NET 8 SDK
Verify the SDK is available:

```powershell
dotnet --info
```

### 3. Enable Developer Mode
Turn on Developer Mode in Windows before building and running the app locally.
This is required by Microsoft's WinUI setup guidance for local development.

## Compile locally
### 1. Open a shell in the repository root

```powershell
cd C:\path\to\CyberSync
```

### 2. Restore dependencies

```powershell
dotnet restore .\src\CyberSync.csproj
```

This downloads the Windows App SDK NuGet package referenced by the project.

### 3. Build the app

```powershell
dotnet build .\src\CyberSync.csproj -c Debug -p:Platform=x64
```

For a Release build:

```powershell
dotnet build .\src\CyberSync.csproj -c Release -p:Platform=x64
```

### 4. Run the app
After a successful Debug build, the executable is typically created at:

```text
src\bin\x64\Debug\net8.0-windows10.0.19041.0\CyberSync.exe
```

You can launch it with:

```powershell
.\src\bin\x64\Debug\net8.0-windows10.0.19041.0\CyberSync.exe
```

## Open in Visual Studio
You can also open the project file directly in Visual Studio:

```text
src\CyberSync.csproj
```

Then build and run it from the IDE.

## Create a publish output for the installer
The installer script expects a published x64 build, not just a raw `dotnet build` output.
Create that publish folder with:

```powershell
dotnet publish .\src\CyberSync.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true
```

The publish output should be created at:

```text
src\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish
```

## Build the installer
Once the publish output exists, open `installer/CyberSync.iss` in Inno Setup and build the installer.
By default, the script packages files from:

```text
src\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish
```

## Scheduled mode
The scheduled task launches the app with:

```text
--run-scheduled
```

That mode runs the sync silently using the saved configuration and exits with:

- `0` for success
- `2` for success with warnings
- `1` for failure

## Troubleshooting
### NuGet restore fails
Make sure you have network access and that `dotnet restore` can reach NuGet.
The project depends on the `Microsoft.WindowsAppSDK` package.

### WinUI templates or build support are missing
Re-open the Visual Studio Installer and confirm the `WinUI application development` workload is installed.

### The installer cannot find the build output
Run the publish command, not just `dotnet build`, before compiling `installer/CyberSync.iss`.
