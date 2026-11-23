using System;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace SIPSorcery.Net.UnitTests;

[Trait("Category", "unit")]
public class SDPConnectionInformationUnitTest
{
    [Fact]
    public void ParseConnectionInformationParsesIPv4Fields()
    {
        var address = GetRandomIPAddress(AddressFamily.InterNetwork);
        var connectionLine = $"c=IN IP4 {address}";

        var connectionInformation = SDPConnectionInformation.ParseConnectionInformation(connectionLine);

        Assert.Equal("IN", connectionInformation.ConnectionNetworkType);
        Assert.Equal(SDPConnectionInformation.CONNECTION_ADDRESS_TYPE_IPV4, connectionInformation.ConnectionAddressType);
        Assert.Equal(address.ToString(), connectionInformation.ConnectionAddress);
    }

    [Fact]
    public void ParseConnectionInformationParsesIPv6Fields()
    {
        var address = GetRandomIPAddress(AddressFamily.InterNetworkV6);
        var connectionLine = $"c=IN IP6 {address}";

        var connectionInformation = SDPConnectionInformation.ParseConnectionInformation(connectionLine);

        Assert.Equal("IN", connectionInformation.ConnectionNetworkType);
        Assert.Equal(SDPConnectionInformation.CONNECTION_ADDRESS_TYPE_IPV6, connectionInformation.ConnectionAddressType);
        Assert.Equal(address.ToString(), connectionInformation.ConnectionAddress);
    }

    [Fact]
    public void ParseConnectionInformationTrimsOuterWhitespace()
    {
        var address = GetRandomIPAddress(AddressFamily.InterNetwork);
        var connectionLine = $"c=IN IP4 {address}   ";

        var connectionInformation = SDPConnectionInformation.ParseConnectionInformation(connectionLine);

        Assert.Equal("IN", connectionInformation.ConnectionNetworkType);
        Assert.Equal(SDPConnectionInformation.CONNECTION_ADDRESS_TYPE_IPV4, connectionInformation.ConnectionAddressType);
        Assert.Equal(address.ToString(), connectionInformation.ConnectionAddress);
    }

    [Fact]
    public void ParseConnectionInformationIgnoresFieldsAfterAddress()
    {
        var address = GetRandomIPAddress(AddressFamily.InterNetwork);
        var extraField = Guid.NewGuid().ToString("N");
        var connectionLine = $"c=IN IP4 {address} {extraField}";

        var connectionInformation = SDPConnectionInformation.ParseConnectionInformation(connectionLine);

        Assert.Equal("IN", connectionInformation.ConnectionNetworkType);
        Assert.Equal(SDPConnectionInformation.CONNECTION_ADDRESS_TYPE_IPV4, connectionInformation.ConnectionAddressType);
        Assert.Equal(address.ToString(), connectionInformation.ConnectionAddress);
    }

    [Fact]
    public void ParseConnectionInformationWithOnlyNetworkTypeRetainsDefaults()
    {
        var connectionInformation = SDPConnectionInformation.ParseConnectionInformation("c=IN");

        Assert.Equal("IN", connectionInformation.ConnectionNetworkType);
        Assert.Equal(SDPConnectionInformation.CONNECTION_ADDRESS_TYPE_IPV4, connectionInformation.ConnectionAddressType);
        Assert.Null(connectionInformation.ConnectionAddress);
    }

    [Fact]
    public void ParseConnectionInformationWithoutAddressRetainsNullAddress()
    {
        var connectionInformation = SDPConnectionInformation.ParseConnectionInformation("c=IN IP6");

        Assert.Equal("IN", connectionInformation.ConnectionNetworkType);
        Assert.Equal(SDPConnectionInformation.CONNECTION_ADDRESS_TYPE_IPV6, connectionInformation.ConnectionAddressType);
        Assert.Null(connectionInformation.ConnectionAddress);
    }

    [Fact]
    public void ToStringProducesIPv4ConnectionLineWithCrLf()
    {
        var address = GetRandomIPAddress(AddressFamily.InterNetwork);
        var connectionInformation = new SDPConnectionInformation(address);

        var result = connectionInformation.ToString();

        Assert.Equal($"c=IN IP4 {address}\r\n", result);
    }

    [Fact]
    public void ToStringProducesIPv6ConnectionLineWithCrLf()
    {
        var address = GetRandomIPAddress(AddressFamily.InterNetworkV6);
        var connectionInformation = new SDPConnectionInformation(address);

        var result = connectionInformation.ToString();

        Assert.Equal($"c=IN IP6 {address}\r\n", result);
    }

    private static IPAddress GetRandomIPAddress(AddressFamily addressFamily)
    {
        var bytes = new byte[addressFamily == AddressFamily.InterNetwork ? 4 : 16];
        new Random().NextBytes(bytes);
        return new IPAddress(bytes);
    }
}
