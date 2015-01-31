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

namespace Renci.SshNet
{
    public partial class Session
    {
        private const byte Null = 0x00;
        private const byte CarriageReturn = 0x0d;
        private const byte LineFeed = 0x0a;

        /// <summary>
        /// Holds the lock object to ensure read access to the socket is synchronized.
        /// </summary>
        private readonly object _socketReadLock = new object();

        /// <summary>
        /// Establishes a socket connection to the specified host and port.
        /// </summary>
        /// <param name="host">The host name of the server to connect to.</param>
        /// <param name="port">The port to connect to.</param>
        /// <exception cref="SshOperationTimeoutException">The connection failed to establish within the configured <see cref="Renci.SshNet.ConnectionInfo.Timeout"/>.</exception>
        /// <exception cref="SocketException">An error occurred trying to establish the connection.</exception>
        partial void SocketConnect(string host, int port)
        {
            const int socketBufferSize = 2 * MaximumSshPacketSize;

            var timeout = ConnectionInfo.Timeout;
            var ep = new EndpointPair(null, string.Empty, new HostName(host), port.ToString());

            _socket = new SocketWrapper();
            _socket.Control.NoDelay = true;
            _socket.Control.OutboundBufferSizeInBytes = socketBufferSize;

            Log(string.Format("Initiating connect to '{0}:{1}'.", ConnectionInfo.Host, ConnectionInfo.Port));

            var connectResult = _socket.ConnectAsync(ep).AsTask();
            if (Task.WhenAny(connectResult, Task.Delay(timeout)).Result != connectResult)
            {
                throw new SshOperationTimeoutException(string.Format(CultureInfo.InvariantCulture,
                    "Connection failed to establish within {0:F0} milliseconds.", timeout.TotalMilliseconds));
            }
        }

        /// <summary>
        /// Performs a blocking read on the socket until a line is read.
        /// </summary>
        /// <param name="response">The line read from the socket, or <c>null</c> when the remote server has shutdown and all data has been received.</param>
        /// <param name="timeout">A <see cref="TimeSpan"/> that represents the time to wait until a line is read.</param>
        /// <exception cref="SshOperationTimeoutException">The read has timed-out.</exception>
        /// <exception cref="SocketException">An error occurred when trying to access the socket.</exception>
        partial void SocketReadLine(ref string response, TimeSpan timeout)
        {
            var encoding = new ASCIIEncoding();
            var buffer = new List<byte>();
            var data = new byte[1].AsBuffer();

            // read data one byte at a time to find end of line and leave any unhandled information in the buffer
            // to be processed by subsequent invocations
            do
            {
                var asyncResult = _socket.InputStream.ReadAsync(data, 1, InputStreamOptions.None).AsTask();
                if (Task.WhenAny(asyncResult, Task.Delay(timeout)).Result != asyncResult)
                {
                    throw new SshOperationTimeoutException(string.Format(CultureInfo.InvariantCulture,
                        "Socket read operation has timed out after {0:F0} milliseconds.", timeout.TotalMilliseconds));
                }

                var received = asyncResult.Result;

                if (received.Length == 0)
                    // the remote server shut down the socket
                    break;

                buffer.Add(received.GetByte(0));
            }
            while (!(buffer.Count > 0 && (buffer[buffer.Count - 1] == LineFeed || buffer[buffer.Count - 1] == Null)));

            var bytes = buffer.ToArray();
            if (bytes.Length == 0)
                response = null;
            else if (bytes.Length == 1 && bytes[bytes.Length - 1] == 0x00)
                // return an empty version string if the buffer consists of only a 0x00 character
                response = string.Empty;
            else if (bytes.Length > 1 && bytes[bytes.Length - 2] == CarriageReturn)
                // strip trailing CRLF
                response = encoding.GetString(bytes, 0, bytes.Length - 2);
            else if (bytes.Length > 1 && bytes[bytes.Length - 1] == LineFeed)
                // strip trailing LF
                response = encoding.GetString(bytes, 0, bytes.Length - 1);
            else
                response = encoding.GetString(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Performs a blocking read on the socket until <paramref name="length"/> bytes are received.
        /// </summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="buffer">The buffer to read to.</param>
        /// <exception cref="SshConnectionException">The socket is closed.</exception>
        /// <exception cref="SocketException">The read failed.</exception>
        partial void SocketRead(int length, ref byte[] buffer, TimeSpan timeout)
        {
            var receivedTotal = 0;  // how many bytes is already received

            do
            {
                var tempBuffer = new byte[length - receivedTotal].AsBuffer();
                var asyncResult = _socket.InputStream.ReadAsync(tempBuffer, (uint)(length - receivedTotal), InputStreamOptions.None).AsTask();
                if (Task.WhenAny(asyncResult, Task.Delay(timeout)).Result != asyncResult)
                {
                    throw new SshOperationTimeoutException(string.Format(CultureInfo.InvariantCulture,
                        "Socket read operation has timed out after {0:F0} milliseconds.", timeout.TotalMilliseconds));
                }

                tempBuffer = asyncResult.Result;
                tempBuffer.CopyTo(0, buffer, receivedTotal, length - receivedTotal);
                if (tempBuffer.Length > 0)
                {
                    // signal that bytes have been read from the socket
                    // this is used to improve accuracy of Session.IsSocketConnected
                    _bytesReadFromSocket.Set();
                    receivedTotal += (int)tempBuffer.Length;
                    continue;
                }

                // 2012-09-11: Kenneth_aa
                // When Disconnect or Dispose is called, this throws SshConnectionException(), which...
                // 1 - goes up to ReceiveMessage() 
                // 2 - up again to MessageListener()
                // which is where there is a catch-all exception block so it can notify event listeners.
                // 3 - MessageListener then again calls RaiseError().
                // There the exception is checked for the exception thrown here (ConnectionLost), and if it matches it will not call Session.SendDisconnect().
                //
                // Adding a check for _isDisconnecting causes ReceiveMessage() to throw SshConnectionException: "Bad packet length {0}".
                //

                if (_isDisconnecting)
                    throw new SshConnectionException("An established connection was aborted by the software in your host machine.", DisconnectReason.ConnectionLost);
                throw new SshConnectionException("An established connection was aborted by the server.", DisconnectReason.ConnectionLost);
            } while (receivedTotal < length);
        }

        /// <summary>
        /// Writes the specified data to the server.
        /// </summary>
        /// <param name="data">The data to write to the server.</param>
        /// <param name="offset">The zero-based offset in <paramref name="data"/> at which to begin taking data from.</param>
        /// <param name="length">The number of bytes of <paramref name="data"/> to write.</param>
        private void SocketWrite(byte[] data, int offset, int length)
        {
            using (var writer = new DataWriter(_socket.OutputStream))
            {
                writer.WriteBuffer(data.AsBuffer(), (uint)offset, (uint)length);
                writer.StoreAsync().AsTask().Wait();
                writer.FlushAsync().AsTask().Wait();
                writer.DetachStream();
            }
        }

        [Conditional("DEBUG")]
        partial void Log(string text)
        {
            Debug.WriteLine(text);
        }
    }
}
