// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Tmds.MDns
{
    class ServiceHandler
    {
        public ServiceHandler(NetworkInterfaceHandler networkInterfaceHandler, Name name)
        {
            Name = name;
            NetworkInterfaceHandler = networkInterfaceHandler;
            ServiceInfos = new List<ServiceInfo>();
        }
        
        public Name Name { get; private set; }
        public NetworkInterfaceHandler NetworkInterfaceHandler { get; private set; }
        public List<ServiceInfo> ServiceInfos { get; private set; }
    }
}
