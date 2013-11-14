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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NetworkInterfaceInformation = System.Net.NetworkInformation.NetworkInterface;

namespace Tmds.MDns
{
    public class ServiceBrowser
    {
        public ServiceBrowser()
        {
            QueryParameters = new QueryParameters();
        }

        public void StartBrowse(string serviceType)
        {
            serviceType = serviceType.ToLower();
            
            if (ServiceTypes.Contains(serviceType))
            {
                return;
            }
            ServiceTypes.Add(serviceType);

            if (!IsBrowsing)
            {
                startBrowsing();
            }
            Name name = new Name(serviceType + ".local.");
            lock (InterfaceHandlers)
            {
                foreach (var interfaceHandler in InterfaceHandlers)
                {
                    interfaceHandler.Value.StartBrowse(name);
                }
            }
        }

        public QueryParameters QueryParameters { get; private set; }
        public SynchronizationContext SynchronizationContext { get; set; }
        public bool IsBrowsing { get; private set; }

        public event EventHandler<ServiceAnnouncementEventArgs> ServiceAdded;
        public event EventHandler<ServiceAnnouncementEventArgs> ServiceRemoved;
        public event EventHandler<ServiceAnnouncementEventArgs> ServiceChanged;
        public event EventHandler<InterfaceDetectedEventArgs> InterfaceDetected;

        internal void onServiceAdded(ServiceInfo service)
        {
            Logger.Debug("{0} Service Added: {1}", service.NetworkInterface.Name, service.Name);
            if (ServiceAdded != null)
            {
                ServiceAnnouncement announcement = new ServiceAnnouncement();
                announcement.Hostname = service.HostName.Labels[0];
                announcement.Domain = service.HostName.SubName(1).ToString();
                announcement.Addresses = service.Addresses;
                announcement.Instance = service.Name.Labels[0];
                announcement.NetworkInterface = service.NetworkInterface;
                announcement.Port = (ushort)service.Port;
                announcement.Txt = service.Txt;
                announcement.Type = service.Name.SubName(1, 2).ToString();
                ServiceAnnouncements.Add(Tuple.Create(service.NetworkInterface.Id, service.Name), announcement);

                synchronizationContextPost(o => ServiceAdded(this, new ServiceAnnouncementEventArgs(announcement)));
            }
        }

        internal void onServiceRemoved(ServiceInfo service)
        {
            Logger.Debug("{0} Service Removed: {1}", service.NetworkInterface.Name, service.Name);
            if (ServiceRemoved != null)
            {
                var key = Tuple.Create(service.NetworkInterface.Id, service.Name);
                ServiceAnnouncement announcement = ServiceAnnouncements[key];
                ServiceAnnouncements.Remove(key);
                synchronizationContextPost(o =>
                {
                    announcement.IsRemoved = true;
                    ServiceRemoved(this, new ServiceAnnouncementEventArgs(announcement));
                });
            }
        }

        internal void onServiceChanged(ServiceInfo service)
        {
            Logger.Debug("{0} Service Changed: {1}", service.NetworkInterface.Name, service.Name);
            if (ServiceChanged != null)
            {
                ServiceAnnouncement announcement = ServiceAnnouncements[Tuple.Create(service.NetworkInterface.Id, service.Name)];
                ServiceAnnouncement tmpAnnouncement = new ServiceAnnouncement();
                tmpAnnouncement.Hostname = service.HostName.Labels[0];
                tmpAnnouncement.Domain = service.HostName.SubName(1).ToString();
                tmpAnnouncement.Addresses = service.Addresses;
                tmpAnnouncement.Instance = service.Name.Labels[0];
                tmpAnnouncement.NetworkInterface = service.NetworkInterface;
                tmpAnnouncement.Port = (ushort)service.Port;
                tmpAnnouncement.Txt = service.Txt;
                tmpAnnouncement.Type = service.Name.SubName(1, 2).ToString();

                synchronizationContextPost(o =>
                {
                    announcement.Hostname = tmpAnnouncement.Hostname;
                    announcement.Domain = tmpAnnouncement.Domain;
                    announcement.Addresses = tmpAnnouncement.Addresses;
                    announcement.Instance = tmpAnnouncement.Instance;
                    announcement.NetworkInterface = tmpAnnouncement.NetworkInterface;
                    announcement.Port = tmpAnnouncement.Port;
                    announcement.Txt = tmpAnnouncement.Txt;
                    announcement.Type = tmpAnnouncement.Type;
                    ServiceChanged(this, new ServiceAnnouncementEventArgs(announcement));
                });
            }
        }

        private void onInterfaceDetect(InterfaceDetectedEventArgs e
            )
        {
            if (InterfaceDetected != null)
            {
                InterfaceDetected(this, e);
            }
        }

        private void startBrowsing()
        {
            if (IsBrowsing)
            {
                return;
            }
            IsBrowsing = true;

            if (SynchronizationContext == null)
            {
                SynchronizationContext = System.Threading.SynchronizationContext.Current;
            }

            InterfaceHandlers = new Dictionary<int, NetworkInterfaceHandler>();

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

                NetworkInterface networkInterface = new NetworkInterface(interfaceInfo);
                InterfaceDetectedEventArgs e = new InterfaceDetectedEventArgs(networkInterface);

                e.Add = true;
                onInterfaceDetect(e);
                if (e.Add)
                {
                    int index = interfaceInfo.GetIPProperties().GetIPv4Properties().Index;
                    InterfaceHandlers.Add(index, new NetworkInterfaceHandler(this, networkInterface));
                }
            }
            NetworkChange.NetworkAddressChanged += checkNetworkInterfaceStatuses;
            checkNetworkInterfaceStatuses(null, null);
        }

        private void checkNetworkInterfaceStatuses(object sender, EventArgs e)
        {
            lock(InterfaceHandlers)
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
                    NetworkInterfaceHandler interfaceHandler = null;
                    InterfaceHandlers.TryGetValue(index, out interfaceHandler);
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

        private void synchronizationContextPost(SendOrPostCallback cb)
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

        private Dictionary<Tuple<string, Name>, ServiceAnnouncement> ServiceAnnouncements = new Dictionary<Tuple<string, Name>, ServiceAnnouncement>();
        private List<string> ServiceTypes = new List<string>();
        private Dictionary<int, NetworkInterfaceHandler> InterfaceHandlers;
        private static Logger Logger = LogManager.GetCurrentClassLogger();
    }
}
