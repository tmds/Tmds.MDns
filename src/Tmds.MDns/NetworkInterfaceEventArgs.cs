// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System;
using System.Net.NetworkInformation;

namespace Tmds.MDns
{
    public class NetworkInterfaceEventArgs : EventArgs
    {
        public NetworkInterfaceEventArgs(NetworkInterface networkInterface)
        {
            NetworkInterface = networkInterface;
        }

        public NetworkInterface NetworkInterface { private set; get; }
    }
}
