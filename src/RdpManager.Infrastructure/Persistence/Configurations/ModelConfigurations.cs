using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RdpManager.Domain.Entities;

namespace RdpManager.Infrastructure.Persistence.Configurations;

public sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> b)
    {
        b.ToTable("Machines");
        b.HasKey(m => m.Id);
        b.Property(m => m.Name).IsRequired().HasMaxLength(200);
        b.Property(m => m.Username).HasMaxLength(200);
        b.Property(m => m.Gateway).HasMaxLength(253);
        b.Property(m => m.Notes);
        b.Property(m => m.IsFavorite);
        b.Property(m => m.DisplayMode).HasConversion<int>();
        b.Property(m => m.Redirection).HasConversion<int>();
        b.Property(m => m.CreatedAt);
        b.Property(m => m.LastConnectedAt);

        // Value object: HostAddress -> Host + Port columns.
        b.OwnsOne(m => m.Host, host =>
        {
            host.Property(h => h.Host).HasColumnName("Host").IsRequired().HasMaxLength(253);
            host.Property(h => h.Port).HasColumnName("Port");
        });
        b.Navigation(m => m.Host).IsRequired();

        // Value object: CredentialRef -> CredentialKind + CredentialRef columns.
        b.OwnsOne(m => m.Credential, cred =>
        {
            cred.Property(c => c.Kind).HasColumnName("CredentialKind").HasConversion<int>();
            cred.Property(c => c.Reference).HasColumnName("CredentialRef").HasMaxLength(400);
        });
        b.Navigation(m => m.Credential).IsRequired();

        // One-to-one owned display profile (with nested owned monitors).
        b.HasOne(m => m.DisplayProfile)
            .WithOne()
            .HasForeignKey<DisplayProfile>(p => p.MachineId)
            .OnDelete(DeleteBehavior.Cascade);

        // Many-to-many tags.
        b.HasMany(m => m.Tags).WithMany()
            .UsingEntity(j => j.ToTable("MachineTags"));

        b.Navigation(m => m.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class DisplayProfileConfiguration : IEntityTypeConfiguration<DisplayProfile>
{
    public void Configure(EntityTypeBuilder<DisplayProfile> b)
    {
        b.ToTable("DisplayProfiles");
        b.HasKey(p => p.Id);
        b.Property(p => p.MachineId).IsRequired();
        b.Property(p => p.ConfiguredAt);

        // Regular one-to-many entity relationship (NOT owned): the profile can be deleted and
        // re-inserted independently, which is exactly what reconfiguring monitors does.
        b.HasMany(p => p.SelectedMonitors)
            .WithOne()
            .HasForeignKey("DisplayProfileId")
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.SelectedMonitors).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class MonitorFingerprintConfiguration : IEntityTypeConfiguration<MonitorFingerprint>
{
    public void Configure(EntityTypeBuilder<MonitorFingerprint> b)
    {
        b.ToTable("MonitorFingerprints");
        b.HasKey(m => m.Id);
        b.Property(m => m.OrdinalInProfile);
        b.Property(m => m.EdidManufacturer).HasMaxLength(8);
        b.Property(m => m.EdidProductCode).HasMaxLength(32);
        b.Property(m => m.EdidSerial).HasMaxLength(64);
        b.Property(m => m.DevicePath).IsRequired().HasMaxLength(400);
        b.Property(m => m.IsPrimary);

        b.OwnsOne(m => m.Geometry, geo =>
        {
            geo.Property(g => g.X).HasColumnName("X");
            geo.Property(g => g.Y).HasColumnName("Y");
            geo.Property(g => g.Width).HasColumnName("Width");
            geo.Property(g => g.Height).HasColumnName("Height");
            geo.Property(g => g.Orientation).HasColumnName("Orientation").HasConversion<int>();
        });
        b.Navigation(m => m.Geometry).IsRequired();
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("Tags");
        b.HasKey(t => t.Id);
        b.Property(t => t.Name).IsRequired().HasMaxLength(64);
        b.HasIndex(t => t.Name).IsUnique();
    }
}

public sealed class HistoryConfiguration : IEntityTypeConfiguration<SessionHistoryEntry>
{
    public void Configure(EntityTypeBuilder<SessionHistoryEntry> b)
    {
        b.ToTable("SessionHistory");
        b.HasKey(h => h.Id);
        // Enums persist as int by convention; nullable enum -> nullable int automatically.
        b.Property(h => h.Detail);
        b.Property(h => h.OccurredAt);
        b.HasIndex(h => new { h.MachineId, h.OccurredAt });
    }
}

public sealed class SecretConfiguration : IEntityTypeConfiguration<ProtectedSecret>
{
    public void Configure(EntityTypeBuilder<ProtectedSecret> b)
    {
        b.ToTable("Secrets");
        b.HasKey(s => s.Reference);
        b.Property(s => s.ProtectedBlob).IsRequired();
        b.Property(s => s.Entropy).IsRequired();
        b.Property(s => s.CreatedAt);
    }
}

public sealed class SettingConfiguration : IEntityTypeConfiguration<SettingEntry>
{
    public void Configure(EntityTypeBuilder<SettingEntry> b)
    {
        b.ToTable("Settings");
        b.HasKey(s => s.Key);
        b.Property(s => s.Key).HasMaxLength(128);
        b.Property(s => s.Value).IsRequired();
    }
}
