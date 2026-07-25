namespace RdpManager.Domain.Entities;

public sealed class Tag
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;

    private Tag() { } // EF

    public Tag(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name must not be empty.", nameof(name));
        Name = name.Trim();
    }
}
