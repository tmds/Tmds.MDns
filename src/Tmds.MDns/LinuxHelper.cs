using System;
using System.Net.Sockets;

namespace Tmds
{
    static class LinuxHelper
    {
        public static unsafe void MultiCastV6(Socket socket, int index)
        {
#if NET6_0
            int setVal = index;
            int rv = Tmds.Linux.LibC.setsockopt(
                socket.Handle.ToInt32(),
                Tmds.Linux.LibC.IPPROTO_IPV6,
                Tmds.Linux.LibC.IPV6_MULTICAST_IF,
                &setVal, sizeof(int));

            if (rv != 0)
            {
                throw new Exception("Socket multicast if addr failed.");
            }
#endif
        }
    }
}
