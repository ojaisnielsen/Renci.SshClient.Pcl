using System;
using System.Threading;
using System.Threading.Tasks;

namespace Renci.SshNet
{
    public partial class PasswordAuthenticationMethod
    {
        /// <summary>
        /// Executes the specified action in a separate thread.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        partial void ExecuteThread(Action action)
        {
            Task.Run(action);
        }
    }
}
