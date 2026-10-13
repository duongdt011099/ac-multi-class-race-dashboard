using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MulticlassRace.Models;

namespace MulticlassRace.Data.Configurations;

public sealed class ImportedLuaResultConfiguration : IEntityTypeConfiguration<ImportedLuaResult>
{
    public void Configure(EntityTypeBuilder<ImportedLuaResult> builder)
    {
        // Dedupe relies on one row per file, so the path is unique.
        builder.HasIndex(r => r.FullPath)
            .IsUnique();

        builder.HasIndex(r => new { r.FileName, r.SessionType })
            .HasDatabaseName("IX_ImportedLuaResults_FileName_SessionType");
    }
}
