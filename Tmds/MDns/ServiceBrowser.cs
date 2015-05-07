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
using NetworkInterfaceInformation = System.Net.NetworkInformation.NetworkInterface;

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
            StartBrowse(new [] { serviceType }, synchronizationContext);
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


        public void StopBrowse()
        {
            foreach (NetworkInterfaceHandler handler in this._activeHandlers)
            {
                handler.Disable();
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

        internal void OnServiceAdded(ServiceInfo service)
        {
            var announcement = new ServiceAnnouncement
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
                lock (_services)
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
                lock (_services)
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

        internal void OnServiceChanged(ServiceInfo service)
        {
            ServiceAnnouncement announcement;
            lock (_serviceAnnouncements)
            {
                announcement = _serviceAnnouncements[Tuple.Create(service.NetworkInterface.Id, service.Name)];
            }
            var tmpAnnouncement = new ServiceAnnouncement()
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

            _interfaceHandlers = new Dictionary<int, NetworkInterfaceHandler>();
            NetworkChange.NetworkAddressChanged += CheckNetworkInterfaceStatuses;
            CheckNetworkInterfaceStatuses(null, null);
        }

        private void CheckNetworkInterfaceStatuses(object sender, EventArgs ev)
        {
            lock(_interfaceHandlers)
            {
                HashSet<NetworkInterfaceHandler> handlers = new HashSet<NetworkInterfaceHandler>(_interfaceHandlers.Values);
                NetworkInterfaceInformation[] interfaceInfos = NetworkInterfaceInformation.GetAllNetworkInterfaces();
                foreach (NetworkInterfaceInformation interfaceInfo in interfaceInfos)
                {
                    if (interfaceInfo.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    if (interfaceInfo.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    int index = interfaceInfo.GetIPProperties().GetIPv4Properties().Index;
                    NetworkInterfaceHandler interfaceHandler;
                    _interfaceHandlers.TryGetValue(index, out interfaceHandler);
                    if (interfaceHandler == null)
                    {
                        var networkInterface = new NetworkInterface(interfaceInfo);
                        index = interfaceInfo.GetIPProperties().GetIPv4Properties().Index;
                        interfaceHandler = new NetworkInterfaceHandler(this, networkInterface);
                        _interfaceHandlers.Add(index, interfaceHandler);
                        OnNetworkInterfaceAdded(networkInterface);
                        interfaceHandler.StartBrowse(_serviceTypes.Select(st => new Name(st.ToLower() + ".local.")));
                    }
                    if (interfaceInfo.OperationalStatus == OperationalStatus.Up)
                    {
                        interfaceHandler.Enable();
                        this._activeHandlers.Add(interfaceHandler);
                    }
                    else
                    {
                        interfaceHandler.Disable();
                    }
                    handlers.Remove(interfaceHandler);
                }
                foreach (NetworkInterfaceHandler handler in handlers)
                {
                    _interfaceHandlers.Remove(handler.Index);
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
        private Dictionary<int, NetworkInterfaceHandler> _interfaceHandlers;
        private HashSet<NetworkInterfaceHandler> _activeHandlers = new HashSet<NetworkInterfaceHandler>();
        private List<string> _serviceTypes = new List<string>();
    }
}
