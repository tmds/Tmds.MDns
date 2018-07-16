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
        public NetworkInterfaceHandler(ServiceBrowser serviceBrowser, NetworkInterface networkInterface)
        {
            ServiceBrowser = serviceBrowser;
            NetworkInterface = networkInterface;
            _index = NetworkInterface.GetIPProperties().GetIPv4Properties().Index;
#if NETSTANDARD1_3
            _queryTimer = new Timer(OnQueryTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
            _receiveEventArgs = new SocketAsyncEventArgs();
            _receiveEventArgs.SetBuffer(_buffer, 0, _buffer.Length);
            _receiveEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceive);
#else
            _queryTimer = new Timer(OnQueryTimerElapsed);
#endif
        }

        public void StartBrowse(IEnumerable<Name> names)
        {
            foreach (var name in names)
            {
                var serviceHandler = new ServiceHandler(this, name);
                _serviceHandlers.Add(name, serviceHandler);
            }
            if (_isEnabled)
            {
                StartQuery();
            }
        }

        public void Enable()
        {
            if (_isEnabled)
            {
                return;
            }

            lock (this)
            {
                _isEnabled = true;

                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.HostToNetworkOrder(_index));
                _socket.Bind(new IPEndPoint(IPAddress.Any, IPv4EndPoint.Port));
                IPAddress ip = IPv4EndPoint.Address;
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ip, _index));
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);

                StartReceive();
                StartQuery();
            }
        }

        public void Disable()
        {
            if (!_isEnabled)
            {
                return;
            }

            lock (this)
            {
                _isEnabled = false;

                StopQuery();

                foreach (var serviceHandlerKV in _serviceHandlers)
                {
                    ServiceHandler serviceHandler = serviceHandlerKV.Value;
                    serviceHandler.ServiceInfos.Clear();
                }

                ((IDisposable)_socket).Dispose(); // .NET 3.5 Dispose is protected
                _socket = null;

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

        public ServiceBrowser ServiceBrowser { get; private set; }
        public NetworkInterface NetworkInterface { get; private set; }
        public int Index { get { return _index; } }
        public static readonly IPEndPoint IPv4EndPoint = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);

        internal void Send(IList<ArraySegment<byte>> packets)
        {
            try
            {
                foreach (ArraySegment<byte> segment in packets)
                {
                    _socket.SendTo(segment.Array, segment.Offset, segment.Count, SocketFlags.None, IPv4EndPoint);
                }
            }
            catch
            { }
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

        private void StartReceive()
        {
#if NETSTANDARD1_3
            bool pending = _socket.ReceiveAsync(_receiveEventArgs);
            if (!pending)
            {
                OnReceive(null, _receiveEventArgs);
            }
#else
            _socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, OnReceive, null);
#endif
        }

#if NETSTANDARD1_3
        private void OnReceive(object sender, SocketAsyncEventArgs args)
#else
        private void OnReceive(IAsyncResult ar)
#endif
        {
            lock (this)
            {
#if NETSTANDARD1_3
                if (args.SocketError != SocketError.Success)
                {
                    return;
                }
                int length = args.BytesTransferred;
#else
                int length;
                try
                {
                    if (_socket == null)
                    {
                        return;
                    }
                    length = _socket.EndReceive(ar);
                }
                catch (Exception)
                {
                    return;
                }
#endif
                var stream = new MemoryStream(_buffer, 0, length);
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
                                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                                {
                                    if (!NetworkInterface.Supports(NetworkInterfaceComponent.IPv6))
                                    {
                                        continue;
                                    }

                                    // Mono does not support IPv6 properties and always throws NotImplementedException.
                                    // Lets handle the case as with Supports.
                                    try
                                    {
                                        address.ScopeId = NetworkInterface.GetIPProperties().GetIPv6Properties().Index;
                                    }
                                    catch (NotImplementedException)
                                    {
                                        continue;
                                    }
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
                StartReceive();
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
                if (!_serviceHandlers.ContainsKey(name.SubName(1)))
                {
                    return;
                }

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
                        if (!_serviceHandlers.ContainsKey(packetName.SubName(1)))
                        {
                            continue;
                        }

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

        private bool _isEnabled;
        private Socket _socket;
        private readonly int _index;
        private readonly byte[] _buffer = new byte[9000];
#if NETSTANDARD1_3
        private readonly SocketAsyncEventArgs _receiveEventArgs;
#endif
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
