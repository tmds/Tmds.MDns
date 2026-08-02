// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System.Collections.Generic;
using System.Net;

namespace Tmds.MDns
{
    class HostInfo
    {
        public HostInfo()
        {
            ServiceInfos = new List<ServiceInfo>();
        }

        public List<ServiceInfo> ServiceInfos { get; private set; }
        public List<IPAddress> Addresses { get; set; }
    }
}
