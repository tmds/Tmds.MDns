Tmds.MDns
=========

Multicast DNS ServiceBrowser

Example
-------

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using Tmds.MDns;

    namespace ServiceFinder
    {
        class Program
        {
            static void Main(string[] args)
            {
                string serviceType = "_workstation._tcp";
                if (args.Length >= 1)
                {
                    serviceType = args[0];
                }

                ServiceBrowser serviceBrowser = new ServiceBrowser();
                serviceBrowser.ServiceAdded += onServiceAdded;
                serviceBrowser.ServiceRemoved += onServiceRemoved;
                serviceBrowser.ServiceChanged += onServiceChanged;
            
                Console.WriteLine("Browsing for type: {0}", serviceType);
                serviceBrowser.StartBrowse(serviceType);
                Console.ReadLine();
            }

            static void onServiceChanged(object sender, ServiceAnnouncementEventArgs e)
            {
                printService('~', e.Announcement);
            }

            static void onServiceRemoved(object sender, ServiceAnnouncementEventArgs e)
            {
                printService('-', e.Announcement);
            }

            static void onServiceAdded(object sender, ServiceAnnouncementEventArgs e)
            {
                printService('+', e.Announcement);
            }

            static void printService(char startChar, ServiceAnnouncement service)
            {
                Console.WriteLine("{0} '{1}' on {2}", startChar, service.Instance, service.NetworkInterface.Name);
                Console.WriteLine("\tHost: {0} ({1})", service.Hostname, ipAddresses(service.Addresses));
                Console.WriteLine("\tPort: {0}", service.Port);
                Console.WriteLine("\tTxt : [{0}]", txtString(service.Txt));
            }
        
            static string txtString(IList<string> txt)
            {
                StringBuilder sb = new StringBuilder();
                foreach (string s in txt)
                {
                    if (sb.Length != 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(s);
                }
                return sb.ToString();
            }

            static string ipAddresses(IList<IPAddress> addresses)
            {
                StringBuilder sb = new StringBuilder();
                foreach (IPAddress address in addresses)
                {
                    if (sb.Length != 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(address.ToString());
                }
                return sb.ToString();
            }
        }
    }
