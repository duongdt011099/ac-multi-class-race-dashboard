using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MulticlassRace.Data.Seed;
using MulticlassRace.Models;

namespace MulticlassRace.Data.Configurations;

public sealed class ClassSettingConfiguration : IEntityTypeConfiguration<ClassSetting>
{
    public void Configure(EntityTypeBuilder<ClassSetting> builder)
    {
        builder.HasMany(c => c.Items)
            .WithOne(i => i.ClassSetting)
            .HasForeignKey(i => i.ClassSettingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasData(DefaultClassSettingSeed.Setting());
    }
}

public sealed class ClassSettingItemConfiguration : IEntityTypeConfiguration<ClassSettingItem>
{
    public void Configure(EntityTypeBuilder<ClassSettingItem> builder)
    {
        builder.HasData(DefaultClassSettingSeed.Items());
    }
}
