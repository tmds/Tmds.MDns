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
    class NetworkInterfaceHandlerSocket : IDisposable
    {
        public static readonly IPEndPoint IPv4EndPoint = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
        public static readonly IPEndPoint IPv6EndPoint = new IPEndPoint(IPAddress.Parse("ff02::fb"), 5353);

        public static NetworkInterfaceHandlerSocket CreateSocketIPv4(int index)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.HostToNetworkOrder(index));

            socket.Bind(new IPEndPoint(IPAddress.Any, IPv4EndPoint.Port));

            IPAddress ip = IPv4EndPoint.Address;
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ip, index));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            var info = new NetworkInterfaceHandlerSocket(socket, IPv4EndPoint);
            return info;
        }

        public static NetworkInterfaceHandlerSocket CreateSocketIPv6(int index)
        {
            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, index);

            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, IPv6EndPoint.Port));

            IPAddress ip = IPv6EndPoint.Address;
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(ip, index));
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, 1);
            var info = new NetworkInterfaceHandlerSocket(socket, IPv6EndPoint);
            return info;
        }

        private NetworkInterfaceHandlerSocket(Socket socket, IPEndPoint endpoint)
        {
            _socket = socket;
            _receiveEndPoint = endpoint;
            var tempEndpoint = new IPEndPoint(endpoint.Address, endpoint.Port);
            _sendEndpoint = (EndPoint)tempEndpoint;
        }

        public delegate void OnReceivedFrom(IPAddress from, MemoryStream stream);
        private Socket _socket;
        private readonly IPEndPoint _receiveEndPoint;
        private EndPoint _sendEndpoint;
        private readonly byte[] _buffer = new byte[9000];
        OnReceivedFrom _onReceivedFrom;

        public void StartReceive(OnReceivedFrom onReceivedFrom)
        {
            _onReceivedFrom = onReceivedFrom;
            StartReceive();
        }

        public void SendPackets(IList<ArraySegment<byte>> packets)
        {
            if (_socket == null || _isDisposed)
            {
                return;
            }

            foreach (ArraySegment<byte> segment in packets)
            {
                _socket.SendTo(segment.Array, segment.Offset, segment.Count, SocketFlags.None, _receiveEndPoint);
            }
        }

        private void StartReceive()
        {
            if (_socket == null || _isDisposed)
            {
                return;
            }

            try
            {
                _socket.BeginReceiveFrom(_buffer, 0, _buffer.Length, SocketFlags.None, ref _sendEndpoint, OnReceive, null);
            }
            catch (Exception)
            {
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            if (_socket == null || _isDisposed)
            {
                return;
            }

            IPAddress receivedFrom;
            int length;
            try
            {
                length = _socket.EndReceiveFrom(ar, ref _sendEndpoint);
                IPEndPoint remoteIpEndPoint = _sendEndpoint as IPEndPoint;
                receivedFrom = remoteIpEndPoint?.Address;

                var stream = new MemoryStream(_buffer, 0, length);
                _onReceivedFrom?.Invoke(receivedFrom, stream);
            }
            catch (Exception)
            {
            }

            StartReceive();
        }

        private bool _isDisposed = false;

        public void Dispose()
        {
            _isDisposed = true;
            _socket.Close();
            _socket.Dispose();
            _socket = null;
        }
    }
}