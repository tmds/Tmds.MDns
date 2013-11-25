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

using NetworkInterfaceInformation = System.Net.NetworkInformation.NetworkInterface;

namespace Tmds.MDns
{
    public class NetworkInterface
    {
        public string Name { get { return Information.Name; } }
        public string Description { get { return Information.Description; } }
        public string Id { get { return Information.Id; } }

        public override bool Equals(object obj)
        {
            NetworkInterface networkInterface = obj as NetworkInterface;
            if (networkInterface == null)
            {
                return false;
            }
            return Id.Equals(networkInterface.Id);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        internal NetworkInterface(NetworkInterfaceInformation info)
        {
            Information = info;
        }

        internal NetworkInterfaceInformation Information { get; private set; }
    }
}
