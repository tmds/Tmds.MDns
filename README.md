[![AppVeyor](https://ci.appveyor.com/api/projects/status/g2arq8vpwasvvu4t?svg=true)](https://ci.appveyor.com/project/tmds/tmds-mdns/branch/master)
[![NuGet](https://img.shields.io/nuget/v/Tmds.MDns.svg)](https://www.nuget.org/packages/Tmds.MDns)

Tmds.MDns
=========

This library allows to find services announced via multicast DNS (RFC6762 and RFC6763).

Version 0.7.0+ is compatible with .NET Core and .NET Framework 4.0+.
Version 0.6 and below also supported .NET Framework 2.0, 3.5.
Support for these versions was dropped due to a dotnet cli msbuild issue:
https://github.com/Microsoft/msbuild/issues/1333.

Example
-------

This examples shows how to use the ServiceBrowser class to find \_workstation.\_tcp_ types.

```C#
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
            Console.WriteLine("\tHost: {0} ({1})", service.Hostname, string.Join(", ", service.Addresses));
            Console.WriteLine("\tPort: {0}", service.Port);
            Console.WriteLine("\tTxt : [{0}]", string.Join(", ", service.Txt));
        }
    }
}
```

Implementation
--------------

The library does not aim to be a reference DNS querier and cache as described in RFC6762. These are the key differences:

- The DNS time to lives are not used. The user can set his own query interval to obtain the **responsiveness** (time to detect, time to live) he desires. The query interval does not increase exponentially as suggested by the RFC, queries are sent at a regular pace.
- To avoid that each service browser instance queries the network for the same service, an instance will not query when it hears another querrier. As a result (in steady state) only **one querrier** will be present, while the others will be listening.
- The library is **self-contained** and does not use a system service (e.g. avahi, bonjour) for mDNS. RFC6762 recommends the use of a single service, but it is not possible to combine this requirement with the differences described in this paragraph.
- The library **automatically resolves the IP addresses** of the service host. No separate resolve operation is required.
- The library launches events in a **_SynchronizationContext_**. This context can be set explicitly or it is captured automatically when the browse operation is executed. Using this library in a WinForms/WPF application is easy as the user can update the UI directly in the event handlers.
- When there is no **_SynchronizationContext_**, events for different interfaces may be generated simultaneously. When iterating over the Services, lock the property to ensure the iterator is not invalidated.