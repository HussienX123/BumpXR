using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BumpXR.Transfer
{
    public static class NetworkUtil
    {
        /// <summary>
        /// Wi-Fi IPv4 of this device (prefers an interface named wlan/wifi), else any non-loopback IPv4, else null.
        /// </summary>
        public static string GetWifiIPv4()
        {
            string fallback = null;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    string name = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    bool isWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                                  || name.Contains("wlan") || name.Contains("wi-fi") || name.Contains("wifi");

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        if (isWifi) return ua.Address.ToString();
                        if (fallback == null) fallback = ua.Address.ToString();
                    }
                }
            }
            catch
            {
                // Some platforms throw on interface enumeration; report "no address".
            }

            return fallback;
        }
    }
}

