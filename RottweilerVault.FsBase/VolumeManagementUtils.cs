using System;
using System.IO;

namespace RottweilerVault.FsBase;

public static class VolumeManagementUtils
{
    public static string GetAppDataDirectoryPath()
    {
        string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        appDataDir = Path.Combine(appDataDir, "rottweiler-vault");

        if (Directory.Exists(appDataDir))
        {
            return appDataDir;
        }

        Directory.CreateDirectory(appDataDir);
        return appDataDir;
    }
}