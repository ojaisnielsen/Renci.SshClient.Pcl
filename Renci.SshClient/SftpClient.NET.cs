using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using System.Globalization;
using Windows.Storage;
using Windows.Storage.Search;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Renci.SshNet
{
    /// <summary>
    /// Implementation of the SSH File Transfer Protocol (SFTP) over SSH.
    /// </summary>
    public partial class SftpClient
    {
        #region SynchronizeDirectories

        /// <summary>
        /// Synchronizes the directories.
        /// </summary>
        /// <param name="sourcePath">The source path.</param>
        /// <param name="destinationPath">The destination path.</param>
        /// <param name="searchPattern">The search pattern.</param>
        /// <returns>List of uploaded files.</returns>
        public IEnumerable<IStorageFile> SynchronizeDirectories(string sourcePath, string destinationPath, string searchPattern)
        {
            return InternalSynchronizeDirectories(sourcePath, destinationPath, searchPattern, null).Result;
        }

        /// <summary>
        /// Begins the synchronize directories.
        /// </summary>
        /// <param name="sourcePath">The source path.</param>
        /// <param name="destinationPath">The destination path.</param>
        /// <param name="searchPattern">The search pattern.</param>
        /// <param name="asyncCallback">The async callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>
        /// An <see cref="System.IAsyncResult" /> that represents the asynchronous directory synchronization.
        /// </returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="sourcePath"/> is <c>null</c>.</exception>
        /// <exception cref="System.ArgumentException"><paramref name="destinationPath"/> is <c>null</c> or contains only whitespace.</exception>
        public IAsyncResult BeginSynchronizeDirectories(string sourcePath, string destinationPath, string searchPattern, AsyncCallback asyncCallback, object state)
        {
            if (sourcePath == null)
                throw new ArgumentNullException("sourcePath");
            if (destinationPath.IsNullOrWhiteSpace())
                throw new ArgumentException("destDir");

            var asyncResult = new SftpSynchronizeDirectoriesAsyncResult(asyncCallback, state);

            ExecuteThread(() =>
            {
                try
                {
                    var result = InternalSynchronizeDirectories(sourcePath, destinationPath, searchPattern, asyncResult).Result;

                    asyncResult.SetAsCompleted(result, false);
                }
                catch (Exception exp)
                {
                    asyncResult.SetAsCompleted(exp, false);
                }
            });

            return asyncResult;
        }

        /// <summary>
        /// Ends the synchronize directories.
        /// </summary>
        /// <param name="asyncResult">The async result.</param>
        /// <returns>List of uploaded files.</returns>
        /// <exception cref="System.ArgumentException">Either the IAsyncResult object did not come from the corresponding async method on this type, or EndExecute was called multiple times with the same IAsyncResult.</exception>
        public IEnumerable<IStorageFile> EndSynchronizeDirectories(IAsyncResult asyncResult)
        {
            var ar = asyncResult as SftpSynchronizeDirectoriesAsyncResult;

            if (ar == null || ar.EndInvokeCalled)
                throw new ArgumentException("Either the IAsyncResult object did not come from the corresponding async method on this type, or EndExecute was called multiple times with the same IAsyncResult.");

            // Wait for operation to complete, then return result or throw exception
            return ar.EndInvoke();
        }

        private async Task<IEnumerable<IStorageFile>> InternalSynchronizeDirectories(string sourcePath, string destinationPath, string searchPattern, SftpSynchronizeDirectoriesAsyncResult asynchResult)
        {
            if (destinationPath.IsNullOrWhiteSpace())
                throw new ArgumentException("destinationPath");

            StorageFolder sourceDirectory;
            try
            {
                sourceDirectory = await StorageFolder.GetFolderFromPathAsync(sourcePath);
            }
            catch (FileNotFoundException)
            {
                throw new FileNotFoundException(string.Format("Source directory not found: {0}", sourcePath));
            }

            var uploadedFiles = new List<IStorageFile>();

            var searchRegex = new Regex(string.Join(string.Empty, searchPattern.Select(c => c == '*' ? ".*" : c == '?' ? ".?" : Regex.Escape(c.ToString()))));

            var sourceFiles = (await sourceDirectory.GetFilesAsync()).Where<StorageFile>(file => searchRegex.IsMatch(file.Name)).ToList();

            if (!sourceFiles.Any())
                return uploadedFiles;

            #region Existing Files at The Destination

            var destFiles = InternalListDirectory(destinationPath, null);
            var destDict = new Dictionary<string, SftpFile>();
            foreach (var destFile in destFiles)
            {
                if (destFile.IsDirectory)
                    continue;
                destDict.Add(destFile.Name, destFile);
            }

            #endregion

            #region Upload the difference

            const Flags uploadFlag = Flags.Write | Flags.Truncate | Flags.CreateNewOrOpen;
            foreach (var localFile in sourceFiles)
            {
                var isDifferent = !destDict.ContainsKey(localFile.Name);

                if (!isDifferent)
                {
                    var temp = destDict[localFile.Name];
                    //  TODO:   Use md5 to detect a difference
                    //ltang: File exists at the destination => Using filesize to detect the difference
                    isDifferent = (await localFile.GetBasicPropertiesAsync()).Size != (ulong)temp.Length;
                }

                if (isDifferent)
                {
                    var remoteFileName = string.Format(CultureInfo.InvariantCulture, @"{0}/{1}", destinationPath, localFile.Name);
                    try
                    {
                        using (var file = await localFile.OpenStreamForReadAsync())
                        {
                            InternalUploadFile(file, remoteFileName, uploadFlag, null, null);
                        }

                        uploadedFiles.Add(localFile);

                        if (asynchResult != null)
                        {
                            asynchResult.Update(uploadedFiles.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(string.Format("Failed to upload {0} to {1}", localFile.Path, remoteFileName), ex);
                    }
                }
            }

            #endregion

            return uploadedFiles;
        }

        #endregion
    }
}
