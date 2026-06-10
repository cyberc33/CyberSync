namespace CyberSync.Services;

public static class DriveResolver
{
    public static DestinationSelection AnalyzeDestinationSelection(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) {
            return new DestinationSelection { ErrorMessage = "Select a destination folder first." };
        }

        string absolutePath;
        try {
            absolutePath = Path.GetFullPath(selectedPath);
        } catch {
            return new DestinationSelection { ErrorMessage = "The selected destination folder is invalid." };
        }

        if (!Directory.Exists(absolutePath)) {
            return new DestinationSelection { ErrorMessage = "The selected destination folder does not exist." };
        }

        string? rootPath = Path.GetPathRoot(absolutePath);
        if (string.IsNullOrWhiteSpace(rootPath)) {
            return new DestinationSelection { ErrorMessage = "Could not resolve the destination drive." };
        }

        try {
            var drive = new DriveInfo(rootPath);
            string volumeLabel = drive.VolumeLabel?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(volumeLabel)) {
                return new DestinationSelection { ErrorMessage = "The destination drive must have a non-empty volume label." };
            }

            string normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
            string relativePath = Path.GetRelativePath(normalizedRoot, absolutePath);
            if (relativePath == ".") {
                relativePath = string.Empty;
            }

            return new DestinationSelection {
                Success = true,
                SelectedPath = string.IsNullOrWhiteSpace(relativePath) ? normalizedRoot : absolutePath,
                RootPath = normalizedRoot,
                VolumeLabel = volumeLabel,
                RelativePath = string.IsNullOrWhiteSpace(relativePath) ? string.Empty : relativePath
            };
        } catch (Exception ex) {
            return new DestinationSelection { ErrorMessage = $"Could not read drive information: {ex.Message}" };
        }
    }

    public static DriveResolution ResolveConfiguredDestination(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.DestinationVolumeLabel)) {
            return new DriveResolution { ErrorMessage = "No destination drive label is configured." };
        }

        foreach (DriveInfo drive in DriveInfo.GetDrives()) {
            try {
                if (!drive.IsReady) {
                    continue;
                }

                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) {
                    continue;
                }

                if (!string.Equals(drive.VolumeLabel, config.DestinationVolumeLabel, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                string rootPath = EnsureTrailingSeparator(drive.RootDirectory.FullName);
                string resolvedPath = string.IsNullOrWhiteSpace(config.DestinationRelativePath)
                    ? rootPath
                    : Path.GetFullPath(Path.Combine(rootPath, config.DestinationRelativePath));

                return new DriveResolution {
                    Success = true,
                    RootPath = rootPath,
                    VolumeLabel = drive.VolumeLabel,
                    ResolvedPath = resolvedPath
                };
            } catch {
                continue;
            }
        }

        return new DriveResolution {
            ErrorMessage = $"The destination drive '{config.DestinationVolumeLabel}' is not connected."
        };
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
