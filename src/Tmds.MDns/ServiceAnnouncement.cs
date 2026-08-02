// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;


namespace Tmds.MDns
{
    public class ServiceAnnouncement
    {
        public string Instance { get; internal set; }
        public string Type { get; internal set; }
        public string Domain { get; internal set; }
        public string Hostname { get; internal set; }
        public ushort Port { get; internal set; }
        public IList<IPAddress> Addresses { get; internal set; }
        public NetworkInterface NetworkInterface { get; internal set; }
        public IList<string> Txt { get; internal set;}
        public bool IsRemoved { get; internal set; }
    }
}
