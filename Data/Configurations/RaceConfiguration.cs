using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MulticlassRace.Models;

namespace MulticlassRace.Data.Configurations;

public sealed class RaceConfiguration : IEntityTypeConfiguration<Race>
{
    public void Configure(EntityTypeBuilder<Race> builder)
    {
        builder.HasMany(r => r.EnteredTeams)
            .WithMany();

        // A setting that races use cannot be deleted from under them.
        builder.HasOne(r => r.PointSetting)
            .WithMany()
            .HasForeignKey(r => r.PointSettingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ClassSetting)
            .WithMany(c => c.Races)
            .HasForeignKey(r => r.ClassSettingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
