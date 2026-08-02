// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

using System;

namespace Tmds.MDns
{
    public class ServiceAnnouncementEventArgs : EventArgs
    {
        public ServiceAnnouncementEventArgs(ServiceAnnouncement announcement)
        {
            Announcement = announcement;
        }

        public ServiceAnnouncement Announcement { private set; get; }
    }
}
