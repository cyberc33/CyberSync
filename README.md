# CyberSync
Small sync program to create backups on external disks.

## Requirements for local builds
To compile the app locally, install the following on a Windows machine:

- Windows 10 or Windows 11
- CMake 3.21 or newer
- A C++17-compatible compiler, such as Visual Studio 2022 with the Desktop development with C++ workload
- Qt Widgets development files for either Qt 5 or Qt 6

Optional tools:

- Ninja, if you want to use the Ninja generator with CMake
- Inno Setup, if you want to build the installer from `installer/CyberSync.iss`

## Build locally
If Qt is not already discoverable by CMake, point `CMAKE_PREFIX_PATH` to your Qt installation.

```powershell
cmake -S . -B build -DCMAKE_PREFIX_PATH="C:\Qt\6.8.0\msvc2022_64"
cmake --build build --config Release
```

If you use Visual Studio, you can also open the generated solution from the `build` directory after configuration.
