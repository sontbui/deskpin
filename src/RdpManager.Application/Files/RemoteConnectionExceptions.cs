namespace RdpManager.Application.Files;

/// <summary>
/// Nothing answered at the SSH/SFTP endpoint: a closed port, a stopped service, a wrong
/// host or a dead route. Derives from <see cref="IOException"/> so the transfer pipeline's
/// existing catch sites keep treating it as an I/O failure; the distinct type is what lets
/// the UI say "can't reach this machine" instead of filing it under "unexpected".
/// </summary>
public sealed class RemoteUnreachableException : IOException
{
    public RemoteUnreachableException() { }
    public RemoteUnreachableException(string message) : base(message) { }
    public RemoteUnreachableException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// The machine has no saved username/password yet. Derives from
/// <see cref="UnauthorizedAccessException"/> so it still reads as "denied" to anything that
/// only knows the base type, while letting the UI offer "add sign-in details" rather than
/// the misleading "your password was rejected".
/// </summary>
public sealed class RemoteCredentialsMissingException : UnauthorizedAccessException
{
    public RemoteCredentialsMissingException() { }
    public RemoteCredentialsMissingException(string message) : base(message) { }
    public RemoteCredentialsMissingException(string message, Exception? innerException) : base(message, innerException) { }
}
