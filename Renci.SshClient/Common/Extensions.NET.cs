using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;

namespace Renci.SshNet.Common
{
    /// <summary>
    /// Collection of different extension method specific for .NET 4.0
    /// </summary>
    internal static partial class Extensions
    {
        /// <summary>
        /// Determines whether [is null or white space] [the specified value].
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>
        ///   <c>true</c> if [is null or white space] [the specified value]; otherwise, <c>false</c>.
        /// </returns>
        internal static bool IsNullOrWhiteSpace(this string value)
        {
            if (string.IsNullOrEmpty(value)) return true;

            return value.All(char.IsWhiteSpace);
        }

        internal static IPAddress GetIPAddress(this string host)
        {
            IPAddress ipAddress;
            if (!IPAddress.TryParse(host, out ipAddress))
            {
                var endpointPairs = DatagramSocket.GetEndpointPairsAsync(new HostName(host), "0").GetResults();
                ipAddress = IPAddress.Parse(endpointPairs[0].RemoteHostName.DisplayName); 
            }

            return ipAddress;
        }
    }
}
