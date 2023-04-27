namespace TagBites.IO.GoogleDrive
{
    public static class GoogleDriveFileSystem
    {
        public static FileSystem Create(string apiKey, string applicationName)
        {
            return new FileSystem(new GoogleDriveFileSystemOperations(apiKey, applicationName));
        }
    }
}
