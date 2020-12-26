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
using System.Net.NetworkInformation;
using System.Threading;
using System.Linq;

namespace Tmds.MDns
{
    public class ServiceBrowser
    {
        public ServiceBrowser()
        {
            QueryParameters = new QueryParameters();
            Services = new ReadOnlyCollectionWrapper<ServiceAnnouncement>(_services);
        }

        public void StartBrowse(string serviceType, SynchronizationContext synchronizationContext)
        {
            StartBrowse(new[] { serviceType }, synchronizationContext);
        }

        public void StartBrowse(IEnumerable<string> serviceTypes, SynchronizationContext synchronizationContext)
        {
            if (IsBrowsing)
            {
                throw new Exception("Already browsing");
            }
            _serviceTypes.AddRange(serviceTypes);
            StartBrowsing(synchronizationContext);
        }

        public void StopBrowse()
        {
            if (!IsBrowsing)
            {
                return;
            }
            IsBrowsing = false;

            NetworkChange.NetworkAddressChanged -= _networkAddressChangedEventHandler;
            lock (_interfaceHandlers)
            {
                foreach (var interfaceHandler in _interfaceHandlers.Values)
                {
                    interfaceHandler.Disable();
                    OnNetworkInterfaceRemoved(interfaceHandler.NetworkInterface);
                }
                _interfaceHandlers = null;
            }
            _serviceTypes.Clear();
            SynchronizationContext = null;
        }

        public void StartBrowse(IEnumerable<string> serviceTypes, bool useSynchronizationContext = true)
        {
            if (useSynchronizationContext)
            {
                StartBrowse(serviceTypes, SynchronizationContext.Current);
            }
            else
            {
                StartBrowse(serviceTypes, null);
            }
        }

        public void StartBrowse(string serviceType, bool useSynchronizationContext = true)
        {
            StartBrowse(new[] { serviceType }, useSynchronizationContext);
        }

        public QueryParameters QueryParameters { get; private set; }
        public SynchronizationContext SynchronizationContext { get; set; }
        public bool IsBrowsing { get; private set; }
        public ICollection<ServiceAnnouncement> Services { private set; get; }

        public event EventHandler<ServiceAnnouncementEventArgs> ServiceAdded;
        public event EventHandler<ServiceAnnouncementEventArgs> ServiceRemoved;
        public event EventHandler<ServiceAnnouncementEventArgs> ServiceChanged;
        public event EventHandler<NetworkInterfaceEventArgs> NetworkInterfaceAdded;
        public event EventHandler<NetworkInterfaceEventArgs> NetworkInterfaceRemoved;

        internal void OnServiceAdded(ServiceInfo service, System.Net.EndPoint localEndPoint)
        {
            var announcement = new ServiceAnnouncement(localEndPoint)
            {
                Hostname = service.HostName.Labels[0],
                Domain = service.HostName.SubName(1).ToString(),
                Addresses = service.Addresses,
                Instance = service.Name.Labels[0],
                NetworkInterface = service.NetworkInterface,
                Port = (ushort)service.Port,
                Txt = service.Txt,
                Type = service.Name.SubName(1, 2).ToString()
            };
            lock (_serviceAnnouncements)
            {
                _serviceAnnouncements.Add(Tuple.Create(service.NetworkInterface.Id, service.Name), announcement);
            }
            SynchronizationContextPost(o =>
            {
                lock (Services)
                {
                    _services.Add(announcement);
                }
                if (ServiceAdded != null)
                {
                    ServiceAdded(this, new ServiceAnnouncementEventArgs(announcement));
                }
            });
        }

        internal void OnServiceRemoved(ServiceInfo service)
        {
            var key = Tuple.Create(service.NetworkInterface.Id, service.Name);
            ServiceAnnouncement announcement;
            lock (_serviceAnnouncements)
            {
                announcement = _serviceAnnouncements[key];
                _serviceAnnouncements.Remove(key);
            }
            SynchronizationContextPost(o =>
            {
                announcement.IsRemoved = true;
                lock (Services)
                {
                    _services.Remove(announcement);
                }
                if (ServiceRemoved != null)
                {
                    ServiceRemoved(this, new ServiceAnnouncementEventArgs(announcement));
                }
            });
        }

        void OnNetworkInterfaceAdded(NetworkInterface networkInterface)
        {
            SynchronizationContextPost(o =>
            {
                if (NetworkInterfaceAdded != null)
                {
                    NetworkInterfaceAdded(this, new NetworkInterfaceEventArgs(networkInterface));
                }
            });
        }

        void OnNetworkInterfaceRemoved(NetworkInterface networkInterface)
        {
            SynchronizationContextPost(o =>
            {
                if (NetworkInterfaceRemoved != null)
                {
                    NetworkInterfaceRemoved(this, new NetworkInterfaceEventArgs(networkInterface));
                }
            });
        }

        internal void OnServiceChanged(ServiceInfo service, System.Net.EndPoint localEndPoint)
        {
            ServiceAnnouncement announcement;
            lock (_serviceAnnouncements)
            {
                announcement = _serviceAnnouncements[Tuple.Create(service.NetworkInterface.Id, service.Name)];
            }
            var tmpAnnouncement = new ServiceAnnouncement(localEndPoint)
            {
                Hostname = service.HostName.Labels[0],
                Domain = service.HostName.SubName(1).ToString(),
                Addresses = service.Addresses,
                Instance = service.Name.Labels[0],
                NetworkInterface = service.NetworkInterface,
                Port = (ushort)service.Port,
                Txt = service.Txt,
                Type = service.Name.SubName(1, 2).ToString()
            };
            SynchronizationContextPost(o =>
            {
                announcement.Hostname = tmpAnnouncement.Hostname;
                announcement.Domain = tmpAnnouncement.Domain;
                announcement.Addresses = tmpAnnouncement.Addresses;
                announcement.Instance = tmpAnnouncement.Instance;
                announcement.NetworkInterface = tmpAnnouncement.NetworkInterface;
                announcement.Port = tmpAnnouncement.Port;
                announcement.Txt = tmpAnnouncement.Txt;
                announcement.Type = tmpAnnouncement.Type;
                if (ServiceChanged != null)
                {
                    ServiceChanged(this, new ServiceAnnouncementEventArgs(announcement));
                }
            });
        }

        private void StartBrowsing(SynchronizationContext synchronizationContext)
        {
            if (IsBrowsing)
            {
                return;
            }
            IsBrowsing = true;
            SynchronizationContext = synchronizationContext;

            Dictionary<string, NetworkInterfaceHandler> interfaceHandlers = new Dictionary<string, NetworkInterfaceHandler>();
            _interfaceHandlers = interfaceHandlers;
            _networkAddressChangedEventHandler = (s, e) =>
            {
                CheckNetworkInterfaceStatuses(interfaceHandlers);
            };
            NetworkChange.NetworkAddressChanged += _networkAddressChangedEventHandler;
            CheckNetworkInterfaceStatuses(interfaceHandlers);
        }

        private void CheckNetworkInterfaceStatuses(Dictionary<string, NetworkInterfaceHandler> interfaceHandlers)
        {
            lock (interfaceHandlers)
            {
                if (interfaceHandlers != _interfaceHandlers)
                {
                    return;
                }

                HashSet<NetworkInterfaceHandler> handlers = new HashSet<NetworkInterfaceHandler>(_interfaceHandlers.Values);
                NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface networkInterface in networkInterfaces)
                {
                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }
                    if (!networkInterface.SupportsMulticast)
                    {
                        continue;
                    }
                    if (!networkInterface.Supports(NetworkInterfaceComponent.IPv4))
                    {
                        continue;
                    }

                    int index = networkInterface.GetIPProperties().GetIPv4Properties().Index;
                    foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicastAddress.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            {
                                NetworkInterfaceHandler interfaceHandler;
                                string key = NetworkInterfaceHandler.CalcKey(index, unicastAddress.Address);
                                _interfaceHandlers.TryGetValue(key, out interfaceHandler);
                                if (interfaceHandler == null)
                                {
                                    index = networkInterface.GetIPProperties().GetIPv4Properties().Index;
                                    interfaceHandler = new NetworkInterfaceHandler(this, networkInterface, unicastAddress.Address);
                                    _interfaceHandlers.Add(key, interfaceHandler);
                                    OnNetworkInterfaceAdded(networkInterface);
                                    interfaceHandler.StartBrowse(_serviceTypes.Select(st => new Name(st.ToLower() + ".local.")));
                                }
                                if (networkInterface.OperationalStatus == OperationalStatus.Up)
                                {
                                    interfaceHandler.Enable();
                                }
                                else
                                {
                                    interfaceHandler.Disable();
                                }
                                handlers.Remove(interfaceHandler);
                            }
                        }
                    }
                }
                foreach (NetworkInterfaceHandler handler in handlers)
                {
                    _interfaceHandlers.Remove(handler.key);
                    handler.Disable();
                    OnNetworkInterfaceRemoved(handler.NetworkInterface);
                }
            }
        }

        private void SynchronizationContextPost(SendOrPostCallback cb)
        {
            if (SynchronizationContext != null)
            {
                SynchronizationContext.Post(cb, null);
            }
            else
            {
                cb(null);
            }
        }

        private readonly HashSet<ServiceAnnouncement> _services = new HashSet<ServiceAnnouncement>();
        private readonly Dictionary<Tuple<string, Name>, ServiceAnnouncement> _serviceAnnouncements = new Dictionary<Tuple<string, Name>, ServiceAnnouncement>();
        private Dictionary<string, NetworkInterfaceHandler> _interfaceHandlers;
        NetworkAddressChangedEventHandler _networkAddressChangedEventHandler;
        private List<string> _serviceTypes = new List<string>();
    }
}
