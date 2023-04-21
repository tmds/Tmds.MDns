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

namespace Tmds.MDns
{
    class NetworkInterfaceHandler
    {
        private static readonly IPEndPoint IPv4EndPoint = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
        private static readonly IPEndPoint IPv6EndPoint = new IPEndPoint(IPAddress.Parse("ff02::fb"), 5353);

        public NetworkInterfaceHandler(ServiceBrowser serviceBrowser, int key, NetworkInterface networkInterface, IEnumerable<Name> names)
        {
            ServiceBrowser = serviceBrowser;
            NetworkInterface = networkInterface;
            _queryTimer = new Timer(OnQueryTimerElapsed);

            foreach (var name in names)
            {
                var serviceHandler = new ServiceHandler(this, name);
                _serviceHandlers.Add(name, serviceHandler);
            }
        }

        public void Refresh(NetworkInterface networkInterface)
        {
            var supportsIPv4 = networkInterface.Supports(NetworkInterfaceComponent.IPv4);
            var supportsIPv6 = networkInterface.Supports(NetworkInterfaceComponent.IPv6);

            if (supportsIPv4)
            {
                _unicastAddresses = networkInterface.GetIPProperties().UnicastAddresses;
            }

            if ((IsIpv4Enabled || !supportsIPv4) &&
                (IsIpv6Enabled || !supportsIPv6))
            {
                return;
            }

            lock (this)
            {
                if (supportsIPv4 && _ipv4Socket == null)
                {
                    int index = networkInterface.GetIPProperties().GetIPv4Properties().Index;
                    _ipv4Socket = CreateIpv4Socket(index);

                    StartReceive(_ipv4Socket, CreateEventArgs(_ipv4Socket, OnReceive));
                }

                if (supportsIPv6 && _ipv6Socket == null)
                {
                    _ipv6InterfaceIndex = networkInterface.GetIPProperties().GetIPv6Properties().Index;
                    _ipv6Socket = CreateIpv6Socket(_ipv6InterfaceIndex);

                    StartReceive(_ipv6Socket, CreateEventArgs(_ipv6Socket, OnReceive));
                }

                StartQuery();
            }
        }

        private static Socket CreateIpv4Socket(int index)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.HostToNetworkOrder(index));
            socket.Bind(new IPEndPoint(IPAddress.Any, IPv4EndPoint.Port));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(IPv4EndPoint.Address, index));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            return socket;
        }

        private static Socket CreateIpv6Socket(int index)
        {
            var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, index);
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, IPv6EndPoint.Port));
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(IPv6EndPoint.Address, index));
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, 1);
            return socket;
        }

        private static SocketAsyncEventArgs CreateEventArgs(Socket socket, EventHandler<SocketAsyncEventArgs> handler)
        {
            var args = new SocketAsyncEventArgs();
            byte[] buffer = new byte[9000];
            args.SetBuffer(buffer, 0, buffer.Length);
            args.Completed += handler;
            args.RemoteEndPoint = socket.LocalEndPoint;
            return args;
        }

        public void Disable()
        {
            if (!IsIpv4Enabled && !IsIpv6Enabled)
            {
                return;
            }

            lock (this)
            {
                StopQuery();

                foreach (var serviceHandlerKV in _serviceHandlers)
                {
                    ServiceHandler serviceHandler = serviceHandlerKV.Value;
                    serviceHandler.ServiceInfos.Clear();
                }

                _ipv4Socket?.Dispose();
                _ipv4Socket = null;
                _unicastAddresses = null;

                _ipv6Socket?.Dispose();
                _ipv6Socket = null;
                _ipv6InterfaceIndex = -1;

                foreach (var serviceKV in _serviceInfos)
                {
                    ServiceInfo service = serviceKV.Value;

                    if (service.IsComplete)
                    {
                        ServiceBrowser.OnServiceRemoved(service);
                    }
                }

                _serviceInfos.Clear();
                _hostInfos.Clear();
            }
        }

        public ServiceBrowser ServiceBrowser { get; }
        public NetworkInterface NetworkInterface { get; }
        public int Key { get; }

        internal void Send(IList<ArraySegment<byte>> packets)
        {
            try
            {
                var socket = _ipv4Socket;
                if (socket != null)
                {
                    SendPackets(socket, packets);
                }

                socket = _ipv6Socket;
                if (socket != null)
                {
                    SendPackets(socket, packets);
                }
            }
            catch
            { }
        }

        private static void SendPackets(Socket socket, IList<ArraySegment<byte>> packets)
        {
            foreach (ArraySegment<byte> segment in packets)
            {
                EndPoint sendToEndPoint = socket.AddressFamily == AddressFamily.InterNetwork ? IPv4EndPoint : IPv6EndPoint;
                socket.SendTo(segment.Array, segment.Offset, segment.Count, SocketFlags.None, sendToEndPoint);
            }
        }

        internal void OnServiceQuery(Name serviceName)
        {
            DateTime now = DateTime.Now;
            List<ServiceInfo> robustnessServices = null;
            ServiceHandler serviceHandler = _serviceHandlers[serviceName];
            foreach (ServiceInfo serviceInfo in serviceHandler.ServiceInfos)
            {
                serviceInfo.OpenQueryCount++;
                serviceInfo.LastQueryTime = now;
                if (serviceInfo.OpenQueryCount >= ServiceBrowser.QueryParameters.Robustness)
                {
                    if (robustnessServices == null)
                    {
                        robustnessServices = new List<ServiceInfo>();
                    }
                    robustnessServices.Add(serviceInfo);
                }
            }
            if (robustnessServices != null)
            {
                var timer = new Timer(o =>
                {
                    lock (o)
                    {
                        foreach (ServiceInfo robustnessService in robustnessServices)
                        {
                            if (robustnessService.OpenQueryCount >= ServiceBrowser.QueryParameters.Robustness)
                            {
                                Name name = robustnessService.Name;
                                RemoveService(name);
                            }
                        }
                    }
                }, this, ServiceBrowser.QueryParameters.ResponseTime, Timeout.Infinite);
            }
        }

        private void StartReceive(Socket socket, SocketAsyncEventArgs args)
        {
            try
            {
                bool pending = socket.ReceiveFromAsync(args);
                if (!pending)
                {
                    OnReceive(socket, args);
                }
            }
            catch (Exception)
            {
            }
        }

        private bool IsLocalNetworkAddress(IPAddress address)
        {
            switch (address.AddressFamily)
            {
                case AddressFamily.InterNetworkV6:
                    return address.ScopeId == _ipv6InterfaceIndex;

                case AddressFamily.InterNetwork:
                    var unicastAddresses = _unicastAddresses;
                    if (unicastAddresses == null)
                    {
                        return false;
                    }
                    for (int i = 0; i < unicastAddresses.Count; i++)
                    {
                        var unicastAddress = unicastAddresses[i];

                        if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var addr1 = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
                            var addr2 = BitConverter.ToUInt32(unicastAddress.Address.GetAddressBytes(), 0);
                            var mask = BitConverter.ToUInt32(unicastAddress.IPv4Mask.GetAddressBytes(), 0);
                            if ((addr1 & mask) == (addr2 & mask))
                            {
                                return true;
                            }
                        }

                    }
                    return false;
                default:
                    return false;
            }
        }

        private void OnReceive(object sender, SocketAsyncEventArgs args)
        {
            lock (this)
            {
                if (args.SocketError != SocketError.Success)
                {
                    return;
                }

                Socket socket = (Socket)sender;
                if (socket != _ipv4Socket && socket != _ipv6Socket)
                {
                    return;
                }

                IPAddress receivedFrom = (args.RemoteEndPoint as IPEndPoint).Address;
                if (!IsLocalNetworkAddress(receivedFrom))
                {
                    StartReceive(socket, args);
                    return;
                }

                int length = args.BytesTransferred;
                var stream = new MemoryStream(args.Buffer, 0, length);
                var reader = new DnsMessageReader(stream);
                bool validPacket = true;

                _packetServiceInfos.Clear();
                _packetHostAddresses.Clear();

                try
                {
                    Header header = reader.ReadHeader();

                    if (header.IsQuery && header.AnswerCount == 0)
                    {
                        for (int i = 0; i < header.QuestionCount; i++)
                        {
                            Question question = reader.ReadQuestion();
                            Name serviceName = question.QName;
                            if (_serviceHandlers.ContainsKey(serviceName))
                            {
                                if (header.TransactionID != _lastQueryId)
                                {
                                    OnServiceQuery(serviceName);
                                }
                            }
                        }
                    }
                    if (header.IsResponse && header.IsNoError)
                    {
                        for (int i = 0; i < header.QuestionCount; i++)
                        {
                            reader.ReadQuestion();
                        }

                        for (int i = 0; i < (header.AnswerCount + header.AuthorityCount + header.AdditionalCount); i++)
                        {
                            RecordHeader recordHeader = reader.ReadRecordHeader();

                            if ((recordHeader.Type == RecordType.A) || (recordHeader.Type == RecordType.AAAA)) // A or AAAA
                            {
                                IPAddress address = reader.ReadARecord();

                                // Set the IPv6 scope.
                                if (address.AddressFamily == AddressFamily.InterNetworkV6 && _ipv6InterfaceIndex != -1)
                                {
                                    address.ScopeId = _ipv6InterfaceIndex;
                                }
                                OnARecord(recordHeader.Name, address, recordHeader.Ttl);
                            }
                            else if ((recordHeader.Type == RecordType.SRV) ||
                                    (recordHeader.Type == RecordType.TXT) ||
                                    (recordHeader.Type == RecordType.PTR))
                            {
                                Name serviceName;
                                Name instanceName;
                                if (recordHeader.Type == RecordType.PTR)
                                {
                                    serviceName = recordHeader.Name;
                                    instanceName = reader.ReadPtrRecord();
                                }
                                else
                                {
                                    instanceName = recordHeader.Name;
                                    serviceName = instanceName.SubName(1);
                                }
                                if (_serviceHandlers.ContainsKey(serviceName))
                                {
                                    if (recordHeader.Ttl == 0)
                                    {
                                        PacketRemovesService(instanceName);
                                    }
                                    else
                                    {
                                        ServiceInfo service = FindOrCreatePacketService(instanceName);
                                        if (recordHeader.Type == RecordType.SRV)
                                        {
                                            SrvRecord srvRecord = reader.ReadSrvRecord();
                                            service.HostName = srvRecord.Target;
                                            service.Port = srvRecord.Port;
                                        }
                                        else if (recordHeader.Type == RecordType.TXT)
                                        {
                                            List<string> txts = reader.ReadTxtRecord();
                                            service.Txt = txts;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    validPacket = false;
                }
                if (validPacket)
                {
                    HandlePacketHostAddresses();
                    HandlePacketServiceInfos();
                }
                StartReceive(socket, args);
            }
        }

        private void OnARecord(Name name, IPAddress address, uint ttl)
        {
            HostAddresses hostAddresses;
            bool found = _packetHostAddresses.TryGetValue(name, out hostAddresses);
            if (!found)
            {
                hostAddresses = new HostAddresses();
                _packetHostAddresses.Add(name, hostAddresses);
            }
            List<IPAddress> addresses = null;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                if (hostAddresses.IPv4Addresses == null)
                {
                    hostAddresses.IPv4Addresses = new List<IPAddress>();
                }
                addresses = hostAddresses.IPv4Addresses;
            }
            else if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (hostAddresses.IPv6Addresses == null)
                {
                    hostAddresses.IPv6Addresses = new List<IPAddress>();
                }
                addresses = hostAddresses.IPv6Addresses;
            }
            if (ttl != 0)
            {
                addresses.Add(address);
            }
        }

        private void PacketRemovesService(Name name)
        {
            _packetServiceInfos.Remove(name);
            _packetServiceInfos.Add(name, null);
        }

        private ServiceInfo FindOrCreatePacketService(Name name)
        {
            ServiceInfo service;
            bool found = _packetServiceInfos.TryGetValue(name, out service);
            if (service == null)
            {
                if (found)
                {
                    _packetServiceInfos.Remove(name);
                }
                service = new ServiceInfo(NetworkInterface, name);
                _packetServiceInfos.Add(name, service);
            }
            return service;
        }

        private void HandlePacketHostAddresses()
        {
            foreach (var hostAddressKV in _packetHostAddresses)
            {
                Name name = hostAddressKV.Key;
                HostAddresses hostAddresses = hostAddressKV.Value;

                HostInfo hostInfo;
                _hostInfos.TryGetValue(name, out hostInfo);
                if (hostInfo == null)
                {
                    return;
                }

                List<IPAddress> packetAddresses = new List<IPAddress>();
                if (hostAddresses.IPv4Addresses == null)
                {
                    if (hostInfo.Addresses != null)
                    {
                        packetAddresses.AddRange(hostInfo.Addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork));
                    }
                }
                else
                {
                    packetAddresses.AddRange(hostAddresses.IPv4Addresses);
                }
                if (hostAddresses.IPv6Addresses == null)
                {
                    if (hostInfo.Addresses != null)
                    {
                        packetAddresses.AddRange(hostInfo.Addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6));
                    }
                }
                else
                {
                    packetAddresses.AddRange(hostAddresses.IPv6Addresses);
                }

                if (packetAddresses.Count == 0)
                {
                    _hostInfos.Remove(name);
                    foreach (var service in hostInfo.ServiceInfos)
                    {
                        PacketRemovesService(service.Name);
                    }
                }
                else
                {
                    var addresses = hostInfo.Addresses;
                    bool same = (addresses != null) && (addresses.Count == packetAddresses.Count) && (addresses.TrueForAll(packetAddresses.Contains));
                    if (!same)
                    {
                        foreach (var service in hostInfo.ServiceInfos)
                        {
                            ServiceInfo newService = FindOrCreatePacketService(service.Name);
                            newService.Addresses = packetAddresses;
                        }
                        hostInfo.Addresses = packetAddresses;
                    }
                }
            }
        }

        private void RemoveService(Name name)
        {
            ServiceInfo service;
            _serviceInfos.TryGetValue(name, out service);
            if (service != null)
            {
                _serviceInfos.Remove(name);
                _serviceHandlers[name.SubName(1)].ServiceInfos.Remove(service);
                if (service.IsComplete)
                {
                    ServiceBrowser.OnServiceRemoved(service);
                }
                if (service.HostName != null)
                {
                    ClearServiceHostInfo(service);
                }
            }
        }

        private void HandlePacketServiceInfos()
        {
            foreach (var serviceKV in _packetServiceInfos)
            {
                Name packetName = serviceKV.Key;
                ServiceInfo packetService = serviceKV.Value;

                if (packetService == null)
                {
                    RemoveService(packetName);
                }
                else
                {
                    bool modified = false;
                    bool wasComplete = false;

                    ServiceInfo service;
                    _serviceInfos.TryGetValue(packetName, out service);

                    if (service == null)
                    {
                        service = packetService;
                        _serviceInfos.Add(packetName, service);
                        _serviceHandlers[packetName.SubName(1)].ServiceInfos.Add(service);

                        if (service.HostName != null)
                        {
                            AddServiceHostInfo(service);
                        }

                        modified = true;
                    }
                    else
                    {
                        service.OpenQueryCount = 0;
                        wasComplete = service.IsComplete;

                        if (packetService.Port != -1 && service.Port != packetService.Port)
                        {
                            service.Port = packetService.Port;
                            modified = true;
                        }
                        if (packetService.Name.ToString() != service.Name.ToString())
                        {
                            service.Name = packetService.Name;
                            modified = true;
                        }
                        if (packetService.Txt != null && (service.Txt == null || !packetService.Txt.SequenceEqual(service.Txt)))
                        {
                            service.Txt = packetService.Txt;
                            modified = true;
                        }
                        if (packetService.HostName != null && (service.HostName == null || service.HostName.ToString() != packetService.HostName.ToString()))
                        {
                            if (service.HostName != null)
                            {
                                ClearServiceHostInfo(service);
                            }

                            service.HostName = packetService.HostName;
                            AddServiceHostInfo(service);
                            modified = true;
                        }
                        if (packetService.Addresses != null)
                        {
                            service.Addresses = packetService.Addresses;
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        if (wasComplete != service.IsComplete)
                        {
                            if (wasComplete)
                            {
                                ServiceBrowser.OnServiceRemoved(service);
                            }
                            else
                            {
                                ServiceBrowser.OnServiceAdded(service);
                            }
                        }
                        else if (service.IsComplete)
                        {
                            ServiceBrowser.OnServiceChanged(service);
                        }
                    }
                }
            }
        }

        private void ClearServiceHostInfo(ServiceInfo service)
        {
            Name hostname = service.HostName;
            HostInfo hostInfo;
            _hostInfos.TryGetValue(hostname, out hostInfo);
            if (hostInfo != null)
            {
                hostInfo.ServiceInfos.Remove(service);
                if (hostInfo.ServiceInfos.Count == 0)
                {
                    _hostInfos.Remove(hostname);
                }
                service.HostName = null;
                service.Addresses = null;
            }
        }

        private void AddServiceHostInfo(ServiceInfo service)
        {
            Name hostname = service.HostName;

            HostInfo hostInfo;
            _hostInfos.TryGetValue(hostname, out hostInfo);
            if (hostInfo == null)
            {
                hostInfo = new HostInfo();
                HostAddresses hostAddresses;
                if (_packetHostAddresses.TryGetValue(hostname, out hostAddresses))
                {
                    hostInfo.Addresses = hostAddresses.IPv4Addresses;
                    if (hostInfo.Addresses == null)
                    {
                        hostInfo.Addresses = hostAddresses.IPv6Addresses;
                    }
                    else if (hostAddresses.IPv6Addresses != null)
                    {
                        hostInfo.Addresses.AddRange(hostAddresses.IPv6Addresses);
                    }
                }
                _hostInfos.Add(hostname, hostInfo);
            }

            Debug.Assert(!hostInfo.ServiceInfos.Contains(service));
            hostInfo.ServiceInfos.Add(service);

            service.Addresses = hostInfo.Addresses;
        }

        private void StartQuery()
        {
            _queryCount = 0;
            ScheduleQueryTimer(0);
        }

        private void StopQuery()
        {
            ScheduleQueryTimer(Timeout.Infinite);
        }

        private void OnQueryTimerElapsed(object obj)
        {
            lock (this)
            {
                QueryParameters queryParameters = ServiceBrowser.QueryParameters;
                DateTime now = DateTime.Now;

                bool sendQuery = false;
                _lastQueryId = (ushort)_randomGenerator.Next(0, ushort.MaxValue);
                var writer = new DnsMessageWriter();
                writer.WriteQueryHeader(_lastQueryId);

                foreach (var serviceKV in _serviceHandlers)
                {
                    Name Name = serviceKV.Key;
                    var ServiceInfos = serviceKV.Value.ServiceInfos;
                    bool sendQueryForService = false;
                    if (_queryCount < queryParameters.StartQueryCount)
                    {
                        sendQueryForService = true;
                    }
                    else
                    {
                        if (ServiceInfos.Count == 0)
                        {
                            sendQueryForService = true;
                        }
                        else
                        {
                            foreach (ServiceInfo service in ServiceInfos)
                            {
                                if (service.LastQueryTime <= now.AddMilliseconds(-queryParameters.QueryInterval))
                                {
                                    sendQueryForService = true;
                                }
                            }
                        }
                    }
                    if (sendQueryForService)
                    {
                        OnServiceQuery(Name);
                        writer.WriteQuestion(Name, RecordType.PTR);
                    }
                    sendQuery = sendQuery || sendQueryForService;
                }

                foreach (var hostKV in _hostInfos)
                {
                    var name = hostKV.Key;
                    var adresses = hostKV.Value.Addresses;
                    if (hostKV.Value.Addresses == null)
                    {
                        writer.WriteQuestion(name, RecordType.A);
                        writer.WriteQuestion(name, RecordType.AAAA);
                        sendQuery = true;
                    }
                }

                if (sendQuery)
                {
                    var packets = writer.Packets;
                    Send(packets);

                    _queryCount++;
                }

                ScheduleQueryTimer(_queryCount >= queryParameters.StartQueryCount ? queryParameters.QueryInterval : queryParameters.StartQueryInterval);
            }
        }

        private void ScheduleQueryTimer(int ms)
        {
            _queryTimer.Change(ms, Timeout.Infinite);
        }

        private Socket _ipv4Socket;
        private Socket _ipv6Socket;

        private bool IsIpv4Enabled => _ipv4Socket != null;
        private bool IsIpv6Enabled => _ipv6Socket != null;

        private int _ipv6InterfaceIndex = -1;
        private UnicastIPAddressInformationCollection _unicastAddresses;

        private readonly Dictionary<Name, ServiceInfo> _packetServiceInfos = new Dictionary<Name, ServiceInfo>();
        private readonly Dictionary<Name, HostAddresses> _packetHostAddresses = new Dictionary<Name, HostAddresses>();
        private readonly Dictionary<Name, ServiceInfo> _serviceInfos = new Dictionary<Name, ServiceInfo>();
        private readonly Dictionary<Name, HostInfo> _hostInfos = new Dictionary<Name, HostInfo>();
        private readonly Dictionary<Name, ServiceHandler> _serviceHandlers = new Dictionary<Name, ServiceHandler>();
        private int _queryCount;
        private readonly Timer _queryTimer;
        private Random _randomGenerator = new Random();
        private ushort _lastQueryId;
    }
}
