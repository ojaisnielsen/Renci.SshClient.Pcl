using System;
using System.Text;
using Renci.SshNet.Channels;
//using System.IO;
using Windows.Storage;
using Renci.SshNet.Common;
using System.Text.RegularExpressions;
using Windows.Storage.Streams;
using System.IO;

namespace Renci.SshNet
{
    /// <summary>
    /// Provides SCP client functionality.
    /// </summary>
    public partial class ScpClient
    {
        private static readonly Regex DirectoryInfoRe = new Regex(@"D(?<mode>\d{4}) (?<length>\d+) (?<filename>.+)");
        private static readonly Regex TimestampRe = new Regex(@"T(?<mtime>\d+) 0 (?<atime>\d+) 0");

        /// <summary>
        /// Uploads the specified file to the remote host.
        /// </summary>
        /// <param name="fileInfo">The file system info.</param>
        /// <param name="path">The path.</param>
        /// <exception cref="ArgumentNullException"><paramref name="fileInfo" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
        public void Upload(IStorageFile fileInfo, string path)
        {
            if (fileInfo == null)
                throw new ArgumentNullException("fileInfo");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path");

            using (var input = ServiceFactory.CreatePipeStream())
            using (var channel = Session.CreateChannelSession())
            {
                channel.DataReceived += delegate(object sender, ChannelDataEventArgs e)
                {
                    input.Write(e.Data, 0, e.Data.Length);
                    input.Flush();
                };

                channel.Open();

                if (!channel.SendExecRequest(string.Format("scp -t \"{0}\"", path)))
                    throw new SshException("Secure copy execution request was rejected by the server. Please consult the server logs.");

                CheckReturnCode(input);

                InternalUpload(channel, input, fileInfo, fileInfo.Name);

                channel.Close();
            }
        }

        /// <summary>
        /// Uploads the specified directory to the remote host.
        /// </summary>
        /// <param name="directoryInfo">The directory info.</param>
        /// <param name="path">The path.</param>
        /// <exception cref="ArgumentNullException">fileSystemInfo</exception>
        /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
        public async void Upload(IStorageFolder directoryInfo, string path)
        {
            if (directoryInfo == null)
                throw new ArgumentNullException("directoryInfo");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path");

            using (var input = new PipeStream())
            using (var channel = Session.CreateChannelSession())
            {
                channel.DataReceived += delegate(object sender, ChannelDataEventArgs e)
                {
                    input.Write(e.Data, 0, e.Data.Length);
                    input.Flush();
                };

                channel.Open();

                //  Send channel command request
                channel.SendExecRequest(string.Format("scp -rt \"{0}\"", path));
                CheckReturnCode(input);
                var directoryProperties = await directoryInfo.GetBasicPropertiesAsync();
                InternalSetTimestamp(channel, input, directoryProperties.DateModified.DateTime, directoryProperties.DateModified.DateTime);
                SendData(channel, string.Format("D0755 0 {0}\n", Path.GetFileName(path)));
                CheckReturnCode(input);

                InternalUpload(channel, input, directoryInfo);

                SendData(channel, "E\n");
                CheckReturnCode(input);

                channel.Close();
            }
        }

        /// <summary>
        /// Downloads the specified file from the remote host to local file.
        /// </summary>
        /// <param name="filename">Remote host file name.</param>
        /// <param name="fileInfo">Local file information.</param>
        /// <exception cref="ArgumentNullException"><paramref name="fileInfo"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="filename"/> is null or empty.</exception>
        public void Download(string filename, IStorageFile fileInfo)
        {
            if (string.IsNullOrEmpty(filename))
                throw new ArgumentException("filename");
            if (fileInfo == null)
                throw new ArgumentNullException("fileInfo");

            using (var input = new PipeStream())
            using (var channel = Session.CreateChannelSession())
            {
                channel.DataReceived += delegate(object sender, ChannelDataEventArgs e)
                {
                    input.Write(e.Data, 0, e.Data.Length);
                    input.Flush();
                };

                channel.Open();

                //  Send channel command request
                channel.SendExecRequest(string.Format("scp -pf \"{0}\"", filename));
                SendConfirmation(channel); //  Send reply

                InternalDownload(channel, input, fileInfo);

                channel.Close();
            }
        }

        /// <summary>
        /// Downloads the specified directory from the remote host to local directory.
        /// </summary>
        /// <param name="directoryName">Remote host directory name.</param>
        /// <param name="directoryInfo">Local directory information.</param>
        /// <exception cref="ArgumentException"><paramref name="directoryName"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="directoryInfo"/> is null.</exception>
        public void Download(string directoryName, IStorageFolder directoryInfo)
        {
            if (string.IsNullOrEmpty(directoryName))
                throw new ArgumentException("directoryName");
            if (directoryInfo == null)
                throw new ArgumentNullException("directoryInfo");

            using (var input = new PipeStream())
            using (var channel = Session.CreateChannelSession())
            {
                channel.DataReceived += delegate(object sender, ChannelDataEventArgs e)
                {
                    input.Write(e.Data, 0, e.Data.Length);
                    input.Flush();
                };

                channel.Open();

                //  Send channel command request
                channel.SendExecRequest(string.Format("scp -prf \"{0}\"", directoryName));
                SendConfirmation(channel); //  Send reply

                InternalDownload(channel, input, directoryInfo);

                channel.Close();
            }
        }

        private async void InternalUpload(IChannelSession channel, Stream input, IStorageFile fileInfo, string filename)
        {
            var fileProperties = await fileInfo.GetBasicPropertiesAsync();
            InternalSetTimestamp(channel, input, fileProperties.DateModified.DateTime, fileProperties.DateModified.DateTime);
            using (var source = await fileInfo.OpenStreamForReadAsync())
            {
                InternalUpload(channel, input, source, filename);
            }
        }

        private async void InternalUpload(IChannelSession channel, Stream input, IStorageFolder directoryInfo)
        {
            //  Upload file
            var files = await directoryInfo.GetFilesAsync();
            foreach (var file in files)
            {
                InternalUpload(channel, input, file, file.Name);
            }

            //  Upload directories
            var directories = await directoryInfo.GetFoldersAsync();
            foreach (var directory in directories)
            {
                var directoryProperties = await directory.GetBasicPropertiesAsync();
                InternalSetTimestamp(channel, input, directoryProperties.DateModified.DateTime, directoryProperties.DateModified.DateTime);
                SendData(channel, string.Format("D0755 0 {0}\n", directory.Name));
                CheckReturnCode(input);

                InternalUpload(channel, input, directory);

                SendData(channel, "E\n");
                CheckReturnCode(input);
            }
        }

        private async void InternalDownload(IChannelSession channel, Stream input, IStorageItem fileSystemInfo)
        {
            var modifiedTime = DateTime.Now;
            var accessedTime = DateTime.Now;
            var startDirectoryFullName = fileSystemInfo.Path;
            var currentDirectoryFullName = startDirectoryFullName;
            var directoryCounter = 0;

            while (true)
            {
                var message = ReadString(input);

                if (message == "E")
                {
                    SendConfirmation(channel); //  Send reply

                    directoryCounter--;
                    currentDirectoryFullName = Path.GetDirectoryName(currentDirectoryFullName);

                    if (directoryCounter == 0)
                        break;
                    continue;
                }

                var match = DirectoryInfoRe.Match(message);
                if (match.Success)
                {
                    SendConfirmation(channel); //  Send reply

                    //  Read directory
                    var mode = long.Parse(match.Result("${mode}"));
                    var filename = match.Result("${filename}");

                    IStorageFolder newDirectoryInfo;
                    if (directoryCounter > 0)
                    {
                        newDirectoryInfo = await (await StorageFolder.GetFolderFromPathAsync(currentDirectoryFullName)).CreateFolderAsync(filename);
                    }
                    else
                    {
                        //  Dont create directory for first level
                        newDirectoryInfo = fileSystemInfo as IStorageFolder;
                    }

                    directoryCounter++;

                    currentDirectoryFullName = newDirectoryInfo.Path;
                    continue;
                }

                match = FileInfoRe.Match(message);
                if (match.Success)
                {
                    //  Read file
                    SendConfirmation(channel); //  Send reply

                    var mode = match.Result("${mode}");
                    var length = long.Parse(match.Result("${length}"));
                    var fileName = match.Result("${filename}");

                    var fileInfo = fileSystemInfo as IStorageFile;

                    if (fileInfo == null)
                        fileInfo = await StorageFile.GetFileFromPathAsync(Path.Combine(currentDirectoryFullName, fileName));

                    using (var output = await fileInfo.OpenStreamForWriteAsync())
                    {
                        InternalDownload(channel, input, output, fileName, length);
                    }

                    if (directoryCounter == 0)
                        break;
                    continue;
                }

                match = TimestampRe.Match(message);
                if (match.Success)
                {
                    //  Read timestamp
                    SendConfirmation(channel); //  Send reply

                    var mtime = long.Parse(match.Result("${mtime}"));
                    var atime = long.Parse(match.Result("${atime}"));

                    var zeroTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                    modifiedTime = zeroTime.AddSeconds(mtime);
                    accessedTime = zeroTime.AddSeconds(atime);
                    continue;
                }

                SendConfirmation(channel, 1, string.Format("\"{0}\" is not valid protocol message.", message));
            }
        }

        partial void SendData(IChannelSession channel, string command)
        {
            channel.SendData(Encoding.UTF8.GetBytes(command));
        }
    }
}
