using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MulticlassRace.Models;

namespace MulticlassRace.Data.Configurations;

public sealed class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        builder.Property(a => a.DriverName).IsRequired();

        // A trophy belongs to its season (and race): removing either takes the trophy with it.
        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(a => a.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Race>()
            .WithMany()
            .HasForeignKey(a => a.RaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.DriverId);
    }
}
