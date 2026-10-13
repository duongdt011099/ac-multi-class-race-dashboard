using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MulticlassRace.Data.Converters;
using MulticlassRace.Data.Seed;
using MulticlassRace.Models;

namespace MulticlassRace.Data.Configurations;

public sealed class PointSettingConfiguration : IEntityTypeConfiguration<PointSetting>
{
    public void Configure(EntityTypeBuilder<PointSetting> builder)
    {
        builder.HasKey(p => p.SettingId);

        builder.Property(p => p.Config)
            .HasConversion(new JsonDictionaryConverter())
            .Metadata.SetValueComparer(new JsonDictionaryComparer());

        builder.HasData(DefaultPointSettingSeed.Setting());
    }
}
