namespace LandmarkLocal;

/// <summary>Pinned portable MariaDB Windows zip (matches Docker's MariaDB 11.4 line).</summary>
public static class MariaDbPin
{
    public const string Version = "11.4.13";
    public const string FileName = "mariadb-11.4.13-winx64.zip";
    public const string DownloadUrl =
        "https://archive.mariadb.org/mariadb-11.4.13/winx64-packages/mariadb-11.4.13-winx64.zip";
    /// <summary>SHA-256 of mariadb-11.4.13-winx64.zip from archive.mariadb.org sha256sums.txt.</summary>
    public const string Sha256 = "d62986d433eeebfde218560b276103831604a61e929e87f1a17f5aebd80257e2";
    public const string UnpackFolderName = "mariadb-11.4.13-winx64";
}
