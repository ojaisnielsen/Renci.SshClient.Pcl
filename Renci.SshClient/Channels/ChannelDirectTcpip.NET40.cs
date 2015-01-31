using System.Globalization;
using System.Linq;
using System;
using System.Net;
using Renci.SshNet.Common;
using System.Threading;
using Renci.SshNet.Messages.Transport;
using System.Diagnostics;
using System.Collections.Generic;
using System.Net.Sockets;
using Windows.Networking.Sockets;
using Windows.Networking;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
namespace Renci.SshNet.Channels
{
    /// <summary>
    /// Implements "direct-tcpip" SSH channel.
    /// </summary>
    internal partial class ChannelDirectTcpip 
    {
        partial void InternalSocketReceive(byte[] buffer, ref int read)
        {
            using (var reader = new DataReader(_socket.InputStream))
            {
                var tempBuffer = reader.ReadBuffer((uint)buffer.Length);
                tempBuffer.CopyTo(buffer);
                read = (int)tempBuffer.Length;
            }
        }

        partial void InternalSocketSend(byte[] data)
        {
            using (var writer = new DataWriter(_socket.OutputStream))
            {
                writer.WriteBytes(data);
                writer.StoreAsync().AsTask().Wait();
                writer.FlushAsync().AsTask().Wait();
                writer.DetachStream();
            }
        }
    }
}
