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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.InteropServices;

namespace Tmds.MDns
{
    class MdnsSocket : IDisposable
    {
        public static readonly IPEndPoint IPv4EndPoint = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
        public static readonly IPEndPoint IPv6EndPoint = new IPEndPoint(IPAddress.Parse("ff02::fb"), 5353);

        public static MdnsSocket CreateSocketIPv4(int index)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.HostToNetworkOrder(index));

            socket.Bind(new IPEndPoint(IPAddress.Any, IPv4EndPoint.Port));
            var tempEndpoint = new IPEndPoint(IPAddress.Any, IPv4EndPoint.Port);

            IPAddress ip = IPv4EndPoint.Address;
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ip, index));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            var info = new MdnsSocket(socket, IPv4EndPoint);
            return info;
        }

        public static MdnsSocket CreateSocketIPv6(int index)
        {
            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // The MulticastInterface fails for IPv6, see: https://github.com/dotnet/runtime/issues/24255
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                LinuxHelper.MultiCastV6(socket, index);
            }

            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, IPv6EndPoint.Port));
            var tempEndpoint = new IPEndPoint(IPAddress.IPv6Any, IPv6EndPoint.Port);

            IPAddress ip = IPv6EndPoint.Address;
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(ip, index));
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, 1);
            var info = new MdnsSocket(socket, IPv6EndPoint);
            return info;
        }

        private MdnsSocket(Socket socket, IPEndPoint endpoint)
        {
            _socket = socket;
            _endPoint = endpoint;
            var tempEndpoint = new IPEndPoint(endpoint.Address, endpoint.Port);
            _senderEndpoint = (EndPoint)tempEndpoint;
        }

        public delegate void OnReceivedFrom(IPAddress from, MemoryStream stream);
        private Socket _socket;
        private IPEndPoint _endPoint;
        private EndPoint _senderEndpoint;
        private readonly byte[] _buffer = new byte[9000];
        OnReceivedFrom _onReceivedFrom;

        public void StartReceive(OnReceivedFrom onReceivedFrom)
        {
            _onReceivedFrom = onReceivedFrom;
            StartReceive();
        }

        public void SendPackets(IList<ArraySegment<byte>> packets)
        {
            if (_socket == null)
            {
                return;
            }

            foreach (ArraySegment<byte> segment in packets)
            {
                _socket.SendTo(segment.Array, segment.Offset, segment.Count, SocketFlags.None, _endPoint);
            }
        }

        private void StartReceive()
        {
            _socket.BeginReceiveFrom(_buffer, 0, _buffer.Length, SocketFlags.None, ref _senderEndpoint, OnReceive, null);
        }

        private void OnReceive(IAsyncResult ar)
        {
            IPAddress receivedFrom;
            int length;
            try
            {
                if (_socket == null)
                {
                    return;
                }
                length = _socket.EndReceiveFrom(ar, ref _senderEndpoint);
                IPEndPoint remoteIpEndPoint = _senderEndpoint as IPEndPoint;
                receivedFrom = remoteIpEndPoint?.Address;

                var stream = new MemoryStream(_buffer, 0, length);
                _onReceivedFrom?.Invoke(receivedFrom, stream);
            }
            catch (Exception)
            {
            }

            StartReceive();
        }

        public void Dispose()
        {
            _socket.Dispose();
            _socket = null;
        }
    }
}