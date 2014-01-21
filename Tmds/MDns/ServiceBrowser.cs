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

        public void StartBrowse(string serviceType)
        {
            StartBrowse(new [] { serviceType });
        }

        public void StartBrowse(IEnumerable<string> serviceTypes)
        {
            if (IsBrowsing)
            {
                throw new Exception("Already browsing");
            }
            StartBrowsing();
            foreach (var interfaceHandler in _interfaceHandlers)
            {
                interfaceHandler.Value.StartBrowse(serviceTypes.Select(st => new Name(st.ToLower() + ".local.")));
            }
        }

        public QueryParameters QueryParameters { get; private set; }
        public SynchronizationContext SynchronizationContext { get; set; }
        public bool IsBrowsing { get; private set; }
        public ICollection<ServiceAnnouncement> Services { private set; get; }

        public event EventHandler<ServiceAnnouncementEventArgs> ServiceAdded;
        public event EventHandler<ServiceAnnouncementEventArgs> ServiceRemoved;
        public event EventHandler<ServiceAnnouncementEventArgs> ServiceChanged;
        public event EventHandler<InterfaceDetectedEventArgs> InterfaceDetected;

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

        private void OnInterfaceDetect(InterfaceDetectedEventArgs e)
        {
            if (InterfaceDetected != null)
            {
                InterfaceDetected(this, e);
            }
        }

        private void StartBrowsing()
        {
            if (IsBrowsing)
            {
                return;
            }
            IsBrowsing = true;

            if (SynchronizationContext == null)
            {
                SynchronizationContext = SynchronizationContext.Current;
            }

            _interfaceHandlers = new Dictionary<int, NetworkInterfaceHandler>();

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

                var networkInterface = new NetworkInterface(interfaceInfo);
                var e = new InterfaceDetectedEventArgs(networkInterface)
                {
                    Add = true
                };

                OnInterfaceDetect(e);
                if (e.Add)
                {
                    int index = interfaceInfo.GetIPProperties().GetIPv4Properties().Index;
                    _interfaceHandlers.Add(index, new NetworkInterfaceHandler(this, networkInterface));
                }
            }
            NetworkChange.NetworkAddressChanged += CheckNetworkInterfaceStatuses;
            CheckNetworkInterfaceStatuses(null, null);
        }

        private void CheckNetworkInterfaceStatuses(object sender, EventArgs e)
        {
            lock(_interfaceHandlers)
            {
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
                    if (interfaceHandler != null)
                    {
                        if (interfaceInfo.OperationalStatus == OperationalStatus.Up)
                        {
                            interfaceHandler.Enable();
                        }
                        else
                        {
                            interfaceHandler.Disable();
                        }
                    }
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
    }
}
