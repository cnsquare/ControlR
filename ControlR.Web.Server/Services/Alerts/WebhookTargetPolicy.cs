using System.Net;
using System.Net.Sockets;

namespace ControlR.Web.Server.Services.Alerts;

/// <summary>
/// Decides where a webhook may go. Tenants choose the URL and the server makes the request, so
/// without this a tenant could reach services that only the server can see.
/// </summary>
public static class WebhookTargetPolicy
{
  public static bool IsAllowedAddress(IPAddress address)
  {
    if (address.IsIPv4MappedToIPv6)
    {
      address = address.MapToIPv4();
    }

    if (IPAddress.IsLoopback(address) ||
        address.Equals(IPAddress.Any) ||
        address.Equals(IPAddress.IPv6Any))
    {
      return false;
    }

    if (address.AddressFamily == AddressFamily.InterNetwork)
    {
      var bytes = address.GetAddressBytes();
      return bytes[0] switch
      {
        0 or 10 or 127 => false,
        >= 224 => false,
        100 when bytes[1] is >= 64 and <= 127 => false,
        169 when bytes[1] == 254 => false,
        172 when bytes[1] is >= 16 and <= 31 => false,
        192 when bytes[1] == 168 => false,
        _ => true,
      };
    }

    if (address.AddressFamily == AddressFamily.InterNetworkV6)
    {
      var first = address.GetAddressBytes()[0];
      return !address.IsIPv6LinkLocal &&
        !address.IsIPv6SiteLocal &&
        !address.IsIPv6Multicast &&
        (first & 0xFE) != 0xFC;
    }

    return false;
  }

  public static bool IsAllowedUrl(string url, bool allowPrivateTargets)
  {
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
    {
      return false;
    }

    if (allowPrivateTargets)
    {
      return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }

    if (uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback)
    {
      return false;
    }

    return !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || IsAllowedAddress(address);
  }
}
