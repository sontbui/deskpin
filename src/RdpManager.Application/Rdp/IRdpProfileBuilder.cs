using RdpManager.Domain.Entities;

namespace RdpManager.Application.Rdp;

public interface IRdpProfileBuilder
{
    /// <summary>Builds the full text of a temporary .rdp file. Never contains a password.</summary>
    string Build(Machine machine, RdpOptions options);
}
