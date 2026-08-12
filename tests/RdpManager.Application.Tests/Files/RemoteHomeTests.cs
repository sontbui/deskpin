using RdpManager.Application.Files;
using RdpManager.Domain.Enums;
using Xunit;

namespace RdpManager.Application.Tests.Files;

public sealed class RemoteHomeTests
{
    [Theory]
    [InlineData("son.bui", @"C:\Users\son.bui")]
    [InlineData(@"CORP\svc_build", @"C:\Users\svc_build")]
    [InlineData("svc_build@corp.local", @"C:\Users\svc_build")]
    [InlineData(null, @"C:\")]
    [InlineData("   ", @"C:\")]
    public void Windows_home_rides_the_admin_share_path(string? username, string expected) =>
        Assert.Equal(expected, RemoteHome.PathFor(MachineOs.Windows, username, "10.0.4.21"));

    [Theory]
    [InlineData(MachineOs.Linux)]
    [InlineData(MachineOs.MacOs)]
    public void Posix_home_uses_the_samba_home_share_convention(MachineOs os)
    {
        Assert.Equal(@"\\10.0.4.21\son", RemoteHome.PathFor(os, "son", "10.0.4.21"));
        Assert.Equal(@"\\10.0.4.21\home", RemoteHome.PathFor(os, null, "10.0.4.21"));
    }

    [Theory]
    [InlineData("son.bui", "son.bui")]
    [InlineData(@"CORP\son", "son")]
    [InlineData("son@corp.local", "son")]
    [InlineData(@"CORP\son@x", "son")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData(@"CORP\", null)]
    public void UserLeaf_strips_domain_decorations(string? input, string? expected) =>
        Assert.Equal(expected, RemoteHome.UserLeaf(input));
}
