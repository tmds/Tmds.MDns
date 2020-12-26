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

using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;


namespace Tmds.MDns
{
    public class ServiceAnnouncement
    {
        public readonly EndPoint LocalEndpoint;
        public ServiceAnnouncement(EndPoint localEndPoint)
        {
            this.LocalEndpoint = localEndPoint;
        }
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
