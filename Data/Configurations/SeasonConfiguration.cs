using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MulticlassRace.Models;

namespace MulticlassRace.Data.Configurations;

public sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        // Seasons without races may lose their class system when it is deleted; races restrict it already.
        builder.HasOne(s => s.ClassSetting)
            .WithMany()
            .HasForeignKey(s => s.ClassSettingId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
