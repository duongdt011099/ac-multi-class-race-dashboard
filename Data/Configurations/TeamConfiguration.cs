using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MulticlassRace.Models;

namespace MulticlassRace.Data.Configurations;

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.HasMany(t => t.ClassSettingItems)
            .WithMany(i => i.Teams)
            .UsingEntity(j => j.ToTable("TeamClassSettingItem"));
    }
}
