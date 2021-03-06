﻿using System;
using System.Net.Sockets;
using Renci.SshNet.Common;

namespace Renci.SshNet.Channels
{
    /// <summary>
    /// A "direct-tcpip" SSH channel.
    /// </summary>
    internal interface IChannelDirectTcpip : IDisposable
    {
        /// <summary>
        /// Occurs when an exception is thrown while processing channel messages.
        /// </summary>
        event EventHandler<ExceptionEventArgs> Exception;

        /// <summary>
        /// Gets a value indicating whether this channel is open.
        /// </summary>
        /// <value>
        /// <c>true</c> if this channel is open; otherwise, <c>false</c>.
        /// </value>
        bool IsOpen { get; }

        /// <summary>
        /// Gets the local channel number.
        /// </summary>
        /// <value>
        /// The local channel number.
        /// </value>
        uint LocalChannelNumber { get; }

        /// <summary>
        /// Binds the channel to the remote host.
        /// </summary>
        void Bind();

        /// <summary>
        /// Closes the channel.
        /// </summary>
        void Close();
    }
}
