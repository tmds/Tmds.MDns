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

using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Tmds.MDns
{
    class NetworkInterfaceHandler
    {
        public NetworkInterfaceHandler(ServiceBrowser serviceBrowser, NetworkInterface networkInterface)
        {
            ServiceBrowser = serviceBrowser;
            NetworkInterface = networkInterface;
            Index = NetworkInterface.Information.GetIPProperties().GetIPv4Properties().Index;
        }

        public void StartBrowse(Name name)
        {
            ServiceHandler serviceHandler = new ServiceHandler(this, name);
            ServiceHandlers.Add(name, serviceHandler);
            if (IsEnabled)
            {
                serviceHandler.StartBrowse();
            }
        }

        public void Enable()
        {
            if (IsEnabled)
            {
                return;
            }
            lock (this)
            {
                Logger.Debug("{0} Enable", NetworkInterface.Name);
                IsEnabled = true;

                Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, (int)IPAddress.HostToNetworkOrder(Index));
                Socket.Bind(new IPEndPoint(IPAddress.Any, IPv4EndPoint.Port));
                IPAddress ip = IPv4EndPoint.Address;
                Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ip, Index));
                Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                
                startReceive();

                foreach (var serviceHandlerKV in ServiceHandlers)
                {
                    serviceHandlerKV.Value.StartBrowse();
                }
            }
        }

        public void Disable()
        {
            if (!IsEnabled)
            {
                return;
            }

            lock (this)
            {
                Logger.Debug("{0} Disable", NetworkInterface.Name);
                IsEnabled = false;

                foreach (var serviceHandlerKV in ServiceHandlers)
                {
                    ServiceHandler serviceHandler = serviceHandlerKV.Value;
                    serviceHandler.StopBrowse();
                    serviceHandler.ServiceInfos.Clear();
                }

                Socket.Dispose();
                Socket = null;

                foreach (var serviceKV in ServiceInfos)
                {
                    ServiceInfo service = serviceKV.Value;

                    if (service.IsComplete)
                    {
                        ServiceBrowser.onServiceRemoved(service);
                    }
                }

                ServiceInfos.Clear();
                HostInfos.Clear();
            }
        }

        public ServiceBrowser ServiceBrowser { get; private set; }
        public NetworkInterface NetworkInterface { get; private set; }

        internal void send(IList<ArraySegment<byte>> packets)
        {
            lock (this)
            {
                try
                {
                    foreach (ArraySegment<byte> segment in packets)
                    {
                        int bytesSent = Socket.SendTo(segment.Array, segment.Offset, segment.Count, SocketFlags.None, IPv4EndPoint);
                    }
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        internal void onServiceQuery(Name serviceName)
        {
            Logger.Debug("{0} onServiceQuery {1}", NetworkInterface.Name, serviceName);
            DateTime now = DateTime.Now;
            List<ServiceInfo> robustnessServices = null;
            ServiceHandler serviceHandler = ServiceHandlers[serviceName];
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
                    Logger.Debug("> Robustness {0}", serviceInfo.Name);
                    robustnessServices.Add(serviceInfo);
                }
            }
            if (robustnessServices != null)
            {
                Logger.Debug("> Schedule Robustness Timer");
                Timer timer = new Timer(o =>
                {
                    lock (o)
                    {
                        Logger.Debug("{0} Robustness Timer {1}", NetworkInterface.Name, serviceName);
                        foreach (ServiceInfo robustnessService in robustnessServices)
                        {
                            if (robustnessService.OpenQueryCount >= ServiceBrowser.QueryParameters.Robustness)
                            {
                                Name name = robustnessService.Name;
                                Logger.Debug(">> Remove Service {0}", name);
                                removeService(name);
                            }
                        }
                    }
                }, this, ServiceBrowser.QueryParameters.ResponseTime, System.Threading.Timeout.Infinite);
            }
        }

        private void startReceive()
        {
            Socket.BeginReceive(Buffer, 0, Buffer.Length, SocketFlags.None, onReceive, null);
        }

        private void onReceive(IAsyncResult ar)
        {
            lock (this)
            {
                int length = 0;
                try
                {
                    if (Socket == null)
                    {
                        return;
                    }
                    length = Socket.EndReceive(ar);
                }
                catch (Exception)
                {
                    return;
                }
                MemoryStream stream = new MemoryStream(Buffer, 0, length);
                DnsMessageReader reader = new DnsMessageReader(stream);
                Header header = reader.ReadHeader();

                if (header.IsQuery && header.QuestionCount == 1 && header.AnswerCount == 0)
                {
                    Question question = reader.ReadQuestion();
                    Name serviceName = question.QName;
                    if (ServiceHandlers.ContainsKey(serviceName))
                    {
                        if (header.TransactionID != ServiceHandlers[serviceName].LastTransactionId)
                        {
                            Logger.Debug("{0} Receive Query", NetworkInterface.Name);
                            onServiceQuery(serviceName);
                        }
                    }
                }
                if (header.IsResponse && header.IsNoError && header.IsAuthorativeAnswer)
                {
                    for (int i = 0; i < header.QuestionCount; i++)
                    {
                        Question question = reader.ReadQuestion();
                    }

                    Logger.Debug("{0} ReceiveResponse", NetworkInterface.Name);
                    
                    PacketServiceInfos.Clear();
                    PacketHostAddresses.Clear();

                    for (int i = 0; i < header.AnswerCount; i++)
                    {
                        RecordHeader recordHeader = reader.ReadRecordHeader();
                        if ((recordHeader.Type == RecordType.A) || (recordHeader.Type == RecordType.AAAA)) // A or AAAA
                        {
                            IPAddress address = reader.ReadARecord();
                            Logger.Debug("> A record {0} -> {1} ({2})", recordHeader.Name, address, recordHeader.Ttl);
                            onARecord(recordHeader.Name, address, recordHeader.Ttl);
                        }
                        else if ((recordHeader.Type == RecordType.SRV) ||
                                 (recordHeader.Type == RecordType.TXT) ||
                                (recordHeader.Type == RecordType.PTR))
                        {
                            Name serviceName = null;
                            Name instanceName = null;
                            if (recordHeader.Type == RecordType.PTR)
                            {
                                serviceName = recordHeader.Name;
                                instanceName = reader.ReadPtrRecord();
                                Logger.Debug("> PTR record {0} -> {1} ({2})", recordHeader.Name, instanceName, recordHeader.Ttl);
                            }
                            else
                            {
                                instanceName = recordHeader.Name;
                                serviceName = instanceName.SubName(1);
                                if (recordHeader.Type == RecordType.SRV)
                                {
                                    Logger.Debug("> PTR record {0} ({1})", recordHeader.Name, recordHeader.Ttl);
                                }
                                else
                                {
                                    Logger.Debug("> TXT record {0} ({1})", recordHeader.Name, recordHeader.Ttl);
                                }
                            }
                            if (ServiceHandlers.ContainsKey(serviceName))
                            {
                                if (recordHeader.Ttl == 0)
                                {
                                    Logger.Debug(">> Remove Service {0}", instanceName);
                                    packetRemovesService(instanceName);
                                }
                                else
                                {
                                    ServiceInfo service = findOrCreatePacketService(instanceName);
                                    if (recordHeader.Type == RecordType.SRV)
                                    {
                                        SrvRecord srvRecord = reader.ReadSrvRecord();
                                        service.HostName = srvRecord.Target;
                                        service.Port = srvRecord.Port;
                                        Logger.Debug(">> Service {0} Host={1} Port={2}", instanceName, service.HostName, service.Port);
                                    }
                                    else if (recordHeader.Type == RecordType.TXT)
                                    {
                                        List<string> txts = reader.ReadTxtRecord();
                                        service.Txt = txts;
                                        Logger.Debug(">> Service {0} Txt={1}", instanceName, string.Concat(txts));
                                    }
                                }
                            }
                        }
                    }
                    handlePacketHostAddresses();
                    handlePacketServiceInfos();
                }
                startReceive();
            }
        }

        private void onARecord(Name name, IPAddress address, uint ttl)
        {
            if (ttl == 0)
            {
                PacketHostAddresses.Remove(name);
                PacketHostAddresses.Add(name, null);
            }
            else
            {
                List<IPAddress> addresses = null;
                bool found = PacketHostAddresses.TryGetValue(name, out addresses);
                if (addresses == null)
                {
                    if (found)
                    {
                        PacketServiceInfos.Remove(name);
                    }
                    addresses = new List<IPAddress>();
                    PacketHostAddresses.Add(name, addresses);
                }
                addresses.Add(address);
            }
        }

        private void packetRemovesService(Name name)
        {
            PacketServiceInfos.Remove(name);
            PacketServiceInfos.Add(name, null);
        }

        private ServiceInfo findOrCreatePacketService(Name name)
        {
            ServiceInfo service = null;
            bool found = PacketServiceInfos.TryGetValue(name, out service);
            if (service == null)
            {
                if (found)
                {
                    PacketServiceInfos.Remove(name);
                }
                service = new ServiceInfo(NetworkInterface, name);
                PacketServiceInfos.Add(name, service);
            }
            return service;
        }

        private void handlePacketHostAddresses()
        {
            foreach (var hostAddressKV in PacketHostAddresses)
            {
                Name name = hostAddressKV.Key;
                List<IPAddress> packetAddresses = hostAddressKV.Value;

                HostInfo hostInfo = null;
                HostInfos.TryGetValue(name, out hostInfo);
                if (hostInfo == null)
                {
                    return;
                }

                if (packetAddresses == null)
                {
                    Logger.Debug("~ Remove Host {0}", name);
                    HostInfos.Remove(name);
                    foreach (var service in hostInfo.ServiceInfos)
                    {
                        Logger.Debug("~~ Remove Host service {0}", service.Name);
                        packetRemovesService(service.Name);
                    }
                }
                else
                {
                    var addresses = hostInfo.Addresses;
                    bool same = (addresses != null) && (addresses.Count == packetAddresses.Count) && (addresses.TrueForAll(address => packetAddresses.Contains(address)));
                    if (!same)
                    {
                        Logger.Debug("~ New Adresses for Host {0}", name);
                        foreach (var service in hostInfo.ServiceInfos)
                        {
                            ServiceInfo newService = findOrCreatePacketService(service.Name);
                            Logger.Debug("~~ New Adresses for Host service {0}", service.Name);
                            newService.Addresses = packetAddresses;
                        }
                        hostInfo.Addresses = packetAddresses;
                    }
                }
            }
        }

        private void removeService(Name name)
        {
            ServiceInfo service = null;
            ServiceInfos.TryGetValue(name, out service);
            if (service != null)
            {
                ServiceInfos.Remove(name);
                ServiceHandlers[name.SubName(1)].ServiceInfos.Remove(service);
                if (service.IsComplete)
                {
                    ServiceBrowser.onServiceRemoved(service);
                }
                if (service.HostName != null)
                {
                    clearServiceHostInfo(service);
                }
            }
        }

        private void handlePacketServiceInfos()
        {
            foreach (var serviceKV in PacketServiceInfos)
            {
                Name packetName = serviceKV.Key;
                ServiceInfo packetService = serviceKV.Value;

                if (packetService == null)
                {
                    Logger.Debug("~ Remove Service {0}", packetName);
                    removeService(packetName);
                }
                else
                {
                    bool modified = false;
                    bool wasComplete = false;

                    ServiceInfo service = null;
                    ServiceInfos.TryGetValue(packetName, out service);

                    if (service == null)
                    {
                        Logger.Debug("~ New Service {0}", packetName);
                        service = packetService;
                        ServiceInfos.Add(packetName, service);
                        ServiceHandlers[packetName.SubName(1)].ServiceInfos.Add(service);

                        if (service.HostName != null)
                        {
                            Logger.Debug("~~ addServiceHostInfo {0}", service.HostName);
                            addServiceHostInfo(service);
                        }

                        modified = true;
                    }
                    else
                    {
                        service.OpenQueryCount = 0;
                        wasComplete = service.IsComplete;

                        if (packetService.Port != -1 && service.Port != packetService.Port)
                        {
                            Logger.Debug("~ Service {0} Set Port", packetName);
                            service.Port = packetService.Port;
                            modified = true;
                        }
                        if (packetService.Name.ToString() != service.Name.ToString())
                        {
                            Logger.Debug("~ Service {0} Set Name", packetName);
                            service.Name = packetService.Name;
                            modified = true;
                        }
                        if (packetService.Txt != null && (service.Txt == null || !packetService.Txt.SequenceEqual(service.Txt)))
                        {
                            Logger.Debug("~ Service {0} Set Txt", packetName);
                            service.Txt = packetService.Txt;
                            modified = true;
                        }
                        if (packetService.HostName != null && (service.HostName == null || service.HostName.ToString() != packetService.HostName.ToString()))
                        {
                            Logger.Debug("~ Service {0} Set Hostname", packetName);
                            if (service.HostName != null)
                            {
                                Logger.Debug("~~ clearServiceHostInfo {0}", service.HostName);
                                clearServiceHostInfo(service);
                            }

                            service.HostName = packetService.HostName;
                            Logger.Debug("~~ addServiceHostInfo {0}", packetService.HostName);
                            addServiceHostInfo(service);
                            modified = true;
                        }
                        if (packetService.Addresses != null)
                        {
                            Logger.Debug("~ Service {0} Set Addresses", packetName);
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
                                ServiceBrowser.onServiceRemoved(service);
                            }
                            else
                            {
                                ServiceBrowser.onServiceAdded(service);
                            }
                        }
                        else if (service.IsComplete)
                        {
                            ServiceBrowser.onServiceChanged(service);
                        }
                    }
                }
            }
        }

        private void clearServiceHostInfo(ServiceInfo service)
        {
            Name hostname = service.HostName;
            HostInfo hostInfo = HostInfos[hostname];

            hostInfo.ServiceInfos.Remove(service);
            if (hostInfo.ServiceInfos.Count == 0)
            {
                HostInfos.Remove(hostname);
            }
            service.HostName = null;
            service.Addresses = null;
        }

        private void addServiceHostInfo(ServiceInfo service)
        {
            Name hostname = service.HostName;
            
            HostInfo hostInfo = null;
            HostInfos.TryGetValue(hostname, out hostInfo);
            if (hostInfo == null)
            {
                hostInfo = new HostInfo();
                List<IPAddress> addresses = null;
                PacketHostAddresses.TryGetValue(hostname, out addresses);
                hostInfo.Addresses = addresses;
                HostInfos.Add(hostname, hostInfo);
            }
            
            Debug.Assert(!hostInfo.ServiceInfos.Contains(service));
            hostInfo.ServiceInfos.Add(service);
            
            service.Addresses = hostInfo.Addresses;
        }

        internal Random RandomGenerator = new Random();
        private static IPEndPoint IPv4EndPoint = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
        private bool IsEnabled;
        private Socket Socket;
        private int Index;
        private byte[] Buffer = new byte[9000];
        private Dictionary<Name, ServiceInfo> PacketServiceInfos = new Dictionary<Name, ServiceInfo>();
        private Dictionary<Name, List<IPAddress>> PacketHostAddresses = new Dictionary<Name, List<IPAddress>>();
        private Dictionary<Name, ServiceInfo> ServiceInfos = new Dictionary<Name, ServiceInfo>();
        private Dictionary<Name, HostInfo> HostInfos = new Dictionary<Name, HostInfo>();
        private Dictionary<Name, ServiceHandler> ServiceHandlers = new Dictionary<Name, ServiceHandler>();
        private static Logger Logger = LogManager.GetCurrentClassLogger();
    }
}
