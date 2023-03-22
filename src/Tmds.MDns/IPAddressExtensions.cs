namespace Tmds.MDns
{
    using System.Net;

    /// <summary>
    /// Utility class for IP Address extensions
    /// </summary>
    public static class IPAddressExtensions
    {
        private static IPAddress GetNetworkAddress(this IPAddress address, IPAddress subnetMask)
        {
            var ipAdressBytes = address.GetAddressBytes();
            var subnetMaskBytes = subnetMask.GetAddressBytes();

            if (ipAdressBytes.Length != subnetMaskBytes.Length)
                return IPAddress.None;

            var broadcastAddress = new byte[ipAdressBytes.Length];
            for (var i = 0; i < broadcastAddress.Length; i++)
            {
                broadcastAddress[i] = (byte)(ipAdressBytes[i] & (subnetMaskBytes[i]));
            }
            return new IPAddress(broadcastAddress);
        }

        /// <summary>
        /// Check if two IP Addresses are in the same subnet
        /// </summary>
        /// <param name="address1">First IP Address</param>
        /// <param name="address2">Second IP Address</param>
        /// <param name="subnetMask">Subnet mask</param>
        /// <returns>true if address1 and address2 are in the same subnet as specified by subnetMask</returns>
        public static bool IsInSameSubnet(this IPAddress address1, IPAddress address2, IPAddress subnetMask)
        {
            var network1 = address1.GetNetworkAddress(subnetMask);
            var network2 = address2.GetNetworkAddress(subnetMask);

            return network1.Equals(network2);
        }
    }
}