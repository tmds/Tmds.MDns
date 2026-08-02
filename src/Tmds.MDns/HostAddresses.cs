// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System.Collections.Generic;
using System.Net;

namespace Tmds.MDns
{
    class HostAddresses
    {
        public List<IPAddress> IPv4Addresses { get; set; }
        public List<IPAddress> IPv6Addresses { get; set; }
    }
}
