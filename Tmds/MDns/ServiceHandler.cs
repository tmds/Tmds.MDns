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
using System.Threading;

namespace Tmds.MDns
{
    class ServiceHandler
    {
        public ServiceHandler(NetworkInterfaceHandler networkInterfaceHandler, Name name)
        {
            Name = name;
            NetworkInterfaceHandler = networkInterfaceHandler;
            _timer = new Timer(OnTimerElapsed);
            ServiceInfos = new List<ServiceInfo>();
        }
        
        public void StartBrowse()
        {
            _queryCount = 0;
            ScheduleTimer(0);
        }

        public void StopBrowse()
        {
            ScheduleTimer(Timeout.Infinite);
        }

        public Name Name { get; private set; }
        public NetworkInterfaceHandler NetworkInterfaceHandler { get; private set; }
        public List<ServiceInfo> ServiceInfos { get; private set; }
        public ushort LastTransactionId { get; set; }

        private void OnTimerElapsed(object obj)
        {
            QueryParameters queryParameters = NetworkInterfaceHandler.ServiceBrowser.QueryParameters;
            DateTime now = DateTime.Now;

            bool sendQuery = false;
            if (_queryCount < queryParameters.StartQueryCount)
            {
                sendQuery = true;
            }
            else
            {
                if (ServiceInfos.Count == 0)
                {
                    sendQuery = true;
                }
                else
                {
                    foreach (ServiceInfo service in ServiceInfos)
                    {
                        if (service.LastQueryTime <= (now - new TimeSpan(0, 0, 0, 0, queryParameters.QueryInterval)))
                        {
                            sendQuery = true;
                        }
                    }
                }
            }

            if (sendQuery)
            {
                NetworkInterfaceHandler.OnServiceQuery(Name);

                LastTransactionId = (ushort)_randomGenerator.Next(0, ushort.MaxValue);
                
                var writer = new DnsMessageWriter();
                writer.WriteQueryHeader(LastTransactionId);
                writer.WriteQuestion(Name, RecordType.PTR);
                
                var packets = writer.Packets;
                NetworkInterfaceHandler.Send(packets);
                
                _queryCount++;
            }

            ScheduleTimer(_queryCount >= queryParameters.StartQueryCount ? queryParameters.QueryInterval : queryParameters.StartQueryInterval);
        }

        private void ScheduleTimer(int ms)
        {
            _timer.Change(ms, Timeout.Infinite);
        }

        private int _queryCount;
        private readonly Timer _timer;
        private Random _randomGenerator = new Random();
    }
        
}
