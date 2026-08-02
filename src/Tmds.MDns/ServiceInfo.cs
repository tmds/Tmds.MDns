// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;

namespace Tmds.MDns
{
    class ServiceInfo
    {
        public ServiceInfo(NetworkInterface networkInterface, Name name, HashSet<Name> serviceTypeNames)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }
            if (networkInterface == null)
            {
                throw new ArgumentNullException("networkInterface");
            }
            Name = name;
            ServiceTypeNames = serviceTypeNames != null ? new HashSet<Name>(serviceTypeNames) : new HashSet<Name>();
            Port = -1;
            NetworkInterface = networkInterface;
        }

        public Name Name { get; set; }
        public HashSet<Name> ServiceTypeNames { get; }
        public Name HostName { get; set; }
        public int Port { get; set; }
        public IList<IPAddress> Addresses { get; set; }
        public IList<string> Txt { get; set; }
        public NetworkInterface NetworkInterface { get; private set; }
        public int OpenQueryCount { get; set; }
        public DateTime LastQueryTime { get; set; }

        public bool IsComplete
        {
            get
            {
                return ((HostName != null) && (Port != -1) && (Addresses != null) && (Txt != null));
            }
        }
    }
}
