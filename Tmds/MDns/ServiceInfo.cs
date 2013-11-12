//Copyright (C) 2013  Tom Deseyn

//This library is free software; you can redistribute it and/or
//modify it under the terms of the GNU Lesser General Public
//License as published by the Free Software Foundation; either
//version 2.1 of the License, or (at your option) any later version.

//This library is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//Lesser General Public License for more details.

//You should have received a copy of the GNU Lesser General Public
//License along with this library; if not, write to the Free Software
//Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using NetworkInterfaceInformation = System.Net.NetworkInformation.NetworkInterface;

namespace Tmds.MDns
{
    class ServiceInfo
    {
        public ServiceInfo(NetworkInterface networkInterface, Name name)
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
            Port = -1;
            NetworkInterface = networkInterface;
        }

        public Name Name { get; set; }
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
