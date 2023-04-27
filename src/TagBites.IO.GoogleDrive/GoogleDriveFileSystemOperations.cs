using Google.Apis.Drive.v3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TagBites.IO.Operations;

namespace TagBites.IO.GoogleDrive
{
    internal class GoogleDriveFileSystemOperations : IFileSystemAsyncWriteOperations, IFileSystemMetadataSupport
    {
        private readonly DriveService _service;
        private const string QueryFields = "id, name, kind, parents, createdTime, modifiedTime, mimeType, md5Checksum, size, contentRestrictions";

        #region IFileSystemOperationsMetadataSupport

        bool IFileSystemMetadataSupport.SupportsIsHiddenMetadata => false;
        bool IFileSystemMetadataSupport.SupportsIsReadOnlyMetadata => false;
        bool IFileSystemMetadataSupport.SupportsLastWriteTimeMetadata => false;

        #endregion

        public GoogleDriveFileSystemOperations(string apiKey, string applicationName)
        {
            Guard.ArgumentNotNullOrWhiteSpace(apiKey, nameof(apiKey));
            Guard.ArgumentNotNullOrWhiteSpace(applicationName, nameof(applicationName));

            var bcs = new Google.Apis.Services.BaseClientService.Initializer
            {
                ApiKey = apiKey,
                ApplicationName = applicationName
            };

            _service = new DriveService(bcs);
        }


        public string CorrectPath(string path)
        {
            return path;
        }
        public IFileSystemStructureLinkInfo GetLinkInfo(string fullName)
        {
            Guard.ArgumentNotNullOrEmpty(fullName, nameof(fullName));

            try
            {
                var parts = fullName.Split('/');
                string parentId = null;
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    var request = _service.Files.List();
                    request.Q = $"name = '{part}'";
                    if (!string.IsNullOrEmpty(parentId))
                        request.Q += $" and parents in '{parentId}'";

                    request.Fields = $"files({QueryFields})";
                    var result = request.ExecuteAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    if (result == null || result.Files.Count == 0)
                        return null;

                    if (result.Files.Count > 1)
                        throw new Exception("Too many files found");

                    var file = result.Files[0];
                    if (i < parts.Length - 1)
                        parentId = file.Id;
                    else
                        return GetInfo(file, string.Join("/", parts.Take(parts.Length - 1).ToArray()));
                }

                return null;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public async Task<Stream> ReadFileAsync(FileLink file)
        {
            var id = (file.Info as IGoogleDriveLinkInfo)?.Id;
            if (string.IsNullOrEmpty(id))
                throw new Exception();

            var request = _service.Files.Get(id);
            var outputStream = new MemoryStream();
            await request.DownloadAsync(outputStream).ConfigureAwait(false);
            outputStream.Position = 0;

            return outputStream;
        }
        public async Task<IFileLinkInfo> WriteFileAsync(FileLink file, Stream stream, bool overwrite)
        {
            var id = (file.Info as IGoogleDriveLinkInfo)?.Id;
            var extension = !string.IsNullOrEmpty(file.Extension) ? file.Extension : ".txt";
            var mimeType = MimeTypeMapper.KnownTypes.TryGetValueDefault(extension);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = file.Name,
                MimeType = mimeType,
            };
            var fields = QueryFields;
            // Create
            if (string.IsNullOrEmpty(id))
            {
                var parentId = (file.Parent?.Info as IGoogleDriveLinkInfo)?.Id;
                if (!string.IsNullOrEmpty(parentId))
                    fileMetadata.Parents = new[] { parentId };

                var request = _service.Files.Create(fileMetadata, stream, mimeType);
                request.Fields = fields;
                var upload = await request.UploadAsync().ConfigureAwait(false);
                if (upload.Exception != null)
                    throw upload.Exception;

                return GetFileInfo(request.ResponseBody, file.ParentFullName);
            }
            // Update
            else
            {
                var request = _service.Files.Update(fileMetadata, id, stream, mimeType);
                request.Fields = fields;
                var upload = await request.UploadAsync().ConfigureAwait(false);
                if (upload.Exception != null)
                    throw upload.Exception;

                return GetFileInfo(request.ResponseBody, file.ParentFullName);
            }
        }
        public async Task<IFileLinkInfo> MoveFileAsync(FileLink source, FileLink destination, bool overwrite)
        {
            var file = new Google.Apis.Drive.v3.Data.File();

            // Rename
            if (source.Name != destination.Name)
                file.Name = destination.Name;

            var id = (source.Info as IGoogleDriveLinkInfo)?.Id;
            var request = _service.Files.Update(file, id);
            request.Fields = QueryFields;

            // Move
            var sourceParentId = (source.Parent?.Info as IGoogleDriveLinkInfo)?.Id;
            var destinationParentId = (destination.Parent?.Info as IGoogleDriveLinkInfo)?.Id;
            if (sourceParentId != destinationParentId)
            {
                request.AddParents = destinationParentId;
                request.RemoveParents = sourceParentId;
            }

            var metadata = await request.ExecuteAsync().ConfigureAwait(false);

            return GetFileInfo(metadata, destination.ParentFullName);
        }
        public async Task DeleteFileAsync(FileLink file)
        {
            var id = (file.Info as IGoogleDriveLinkInfo)?.Id;
            if (string.IsNullOrEmpty(id))
                throw new Exception();

            var request = _service.Files.Delete(id);
            var result = await request.ExecuteAsync().ConfigureAwait(false);
        }

        public async Task<IFileSystemStructureLinkInfo> CreateDirectoryAsync(DirectoryLink directory)
        {
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = directory.Name,
                MimeType = "application/vnd.google-apps.folder"
            };
            var parentId = (directory.Parent?.Info as IGoogleDriveLinkInfo)?.Id;
            if (!string.IsNullOrEmpty(parentId))
                fileMetadata.Parents = new[] { parentId };

            var request = _service.Files.Create(fileMetadata);
            request.Fields = QueryFields;
            var result = await request.ExecuteAsync().ConfigureAwait(false);

            return GetInfo(result, directory);
        }
        public async Task<IFileSystemStructureLinkInfo> MoveDirectoryAsync(DirectoryLink source, DirectoryLink destination)
        {
            var file = new Google.Apis.Drive.v3.Data.File();

            // Rename
            if (source.Name != destination.Name)
                file.Name = destination.Name;

            var id = (source.Info as IGoogleDriveLinkInfo)?.Id;
            var request = _service.Files.Update(file, id);
            request.Fields = QueryFields;

            // Move
            var sourceParentId = (source.Parent?.Info as IGoogleDriveLinkInfo)?.Id;
            var destinationParentId = (destination.Parent?.Info as IGoogleDriveLinkInfo)?.Id;
            if (sourceParentId != destinationParentId)
            {
                request.AddParents = destinationParentId;
                request.RemoveParents = sourceParentId;
            }
            var metadata = await request.ExecuteAsync().ConfigureAwait(false);

            return GetDirectoryInfo(metadata, destination.ParentFullName);
        }
        public async Task DeleteDirectoryAsync(DirectoryLink directory, bool recursive)
        {
            if (!recursive)
            {
                var directoryId = TryGetId(directory);
                var listRequest = _service.Files.List();
                listRequest.Q = $"parents in '{directoryId}'";
                listRequest.Fields = "files(id)";
                var listResult = await listRequest.ExecuteAsync().ConfigureAwait(false);
                if (listResult.Files.Any())
                    throw new IOException("Directory is not empty");
            }

            var id = (directory.Info as IGoogleDriveLinkInfo)?.Id;
            if (string.IsNullOrEmpty(id))
                throw new Exception();

            var request = _service.Files.Delete(id);
            var result = await request.ExecuteAsync().ConfigureAwait(false);
        }

        public async Task<IList<IFileSystemStructureLinkInfo>> GetLinksAsync(DirectoryLink directory, FileSystem.ListingOptions options)
        {
            Guard.ArgumentNotNull(directory, nameof(directory));
            Guard.ArgumentNotNull(options, nameof(options));

            var directoryId = TryGetId(directory);
            var links = new List<IFileSystemStructureLinkInfo>();
            string pageToken = null;
            do
            {
                var request = _service.Files.List();
                request.PageToken = pageToken;
                if (!string.IsNullOrEmpty(directoryId))
                    request.Q = $"parents in '{directoryId}'";

                if (options.HasSearchPattern)
                {
                    //request.Q += $" and name contains '{options.SearchPattern}'";
                    //options.SearchPatternHandled = true;
                }

                if (options.SearchForFiles != options.SearchForDirectories)
                {
                    if (options.SearchForFiles)
                        request.Q += "and mimeType != 'application/vnd.google-apps.folder'";

                    if (options.SearchForDirectories)
                        request.Q += "and mimeType = 'application/vnd.google-apps.folder'";
                }

                request.Fields = $"nextPageToken, files({QueryFields})";
                var result = await request.ExecuteAsync().ConfigureAwait(false);

                links.AddRange(result.Files.Select(file => GetInfo(file, directory.FullName)));

                pageToken = result.NextPageToken;

            } while (pageToken != null);


            return links;
        }
        public Task<IFileSystemStructureLinkInfo> UpdateMetadataAsync(FileSystemStructureLink link, IFileSystemLinkMetadata metadata)
        {
            Guard.ArgumentNotNull(link, nameof(link));
            Guard.ArgumentNotNull(metadata, nameof(metadata));

            return Task.Run(() => link.Info);
        }

        private static IFileSystemStructureLinkInfo GetInfo(Google.Apis.Drive.v3.Data.File metadata, DirectoryLink parentDirectory)
        {
            return GetInfo(metadata, parentDirectory?.ParentFullName);
        }
        private static IFileSystemStructureLinkInfo GetInfo(Google.Apis.Drive.v3.Data.File metadata, string parentFullName)
        {
            if (metadata == null)
                return null;

            if (metadata.MimeType == "application/vnd.google-apps.folder")
                return new DirectoryInfo(metadata, parentFullName);
            else
                return new FileInfo(metadata, parentFullName);
        }
        private static DirectoryInfo GetDirectoryInfo(Google.Apis.Drive.v3.Data.File metadata, string parentFullName)
        {
            return new DirectoryInfo(metadata, parentFullName);
        }
        private static FileInfo GetFileInfo(Google.Apis.Drive.v3.Data.File metadata, string parentFullName)
        {
            return new FileInfo(metadata, parentFullName);
        }

        private static string TryGetId(FileSystemStructureLink link)
        {
            var id = (link.Info as IGoogleDriveLinkInfo)?.Id;
            if (!string.IsNullOrEmpty(id))
                return id;

            throw new Exception();
        }

        private interface IGoogleDriveLinkInfo
        {
            string Id { get; }
        }
        private class FileInfo : IFileLinkInfo, IGoogleDriveLinkInfo
        {
            private Google.Apis.Drive.v3.Data.File Metadata { get; }

            public string Id => Metadata.Id;
            public string FullName { get; }
            public bool Exists => true;
            public bool IsDirectory => false;
            public DateTime? CreationTime => Metadata.CreatedTime;
            public DateTime? LastWriteTime => Metadata.ModifiedTime;
            public bool IsHidden => false;
            public bool IsReadOnly => false;

            public string ContentPath => Metadata.Parents.FirstOrDefault();
            public FileHash Hash { get; }
            public long Length => Metadata.Size.GetValueOrDefault();

            public FileInfo(Google.Apis.Drive.v3.Data.File metadata, string parentFullName)
            {
                Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

                FullName = !string.IsNullOrEmpty(parentFullName) ? $"{parentFullName}/{metadata.Name}" : metadata.Name;
                Hash = new FileHash(FileHashAlgorithm.Md5, metadata.Md5Checksum);
            }
        }
        private class DirectoryInfo : IFileSystemStructureLinkInfo, IGoogleDriveLinkInfo
        {
            private Google.Apis.Drive.v3.Data.File Metadata { get; }

            public string Id => Metadata.Id;
            public string FullName { get; }
            public bool Exists => true;
            public bool IsDirectory => true;
            public DateTime? CreationTime => Metadata.CreatedTime;
            public DateTime? LastWriteTime => Metadata.ModifiedTime;
            public bool IsHidden => false;
            public bool IsReadOnly => Metadata.ContentRestrictions?.Any(x => x.ReadOnly__ == true) == true;

            public DirectoryInfo(Google.Apis.Drive.v3.Data.File metadata, string parentFullName)
            {
                Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

                FullName = !string.IsNullOrEmpty(parentFullName) ? $"{parentFullName}/{metadata.Name}" : metadata.Name;
            }
        }
    }
}
