using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class MapResultConfiguration : BaseEntityLongConfiguration<MapResult>
{
    public override void Configure(EntityTypeBuilder<MapResult> builder)
    {
        base.Configure(builder);

        builder.ToTable("map_results");

        builder.Property(x => x.GroupId).HasColumnName("group_id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ReferralJobId).HasColumnName("referral_job_id").IsRequired();

        builder.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("jsonb").IsRequired();

        builder.Property(x => x.CarCount).HasColumnName("car_count").IsRequired();
        builder.Property(x => x.UnresolvedCount).HasColumnName("unresolved_count").IsRequired();
        builder.Property(x => x.PreparedAt).HasColumnName("prepared_at").IsRequired();

        // صف واحد بس لكل مساحة عمل: صف واحد لكل جروب، أو صف واحد لكل يوزر مستقل.
        builder.HasIndex(x => x.GroupId).IsUnique().HasFilter("group_id IS NOT NULL");
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("group_id IS NULL");
    }
}

public class MapUploadedFileConfiguration : BaseEntityConfiguration<MapUploadedFile>
{
    public override void Configure(EntityTypeBuilder<MapUploadedFile> builder)
    {
        base.Configure(builder);

        builder.ToTable("map_uploaded_files");

        builder.Property(x => x.GroupId).HasColumnName("group_id");
        builder.Property(x => x.UploadedBy).HasColumnName("uploaded_by").IsRequired();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.RowCount).HasColumnName("row_count").IsRequired();
        builder.Property(x => x.UploadedAt).HasColumnName("uploaded_at").IsRequired();

        builder.HasIndex(x => x.GroupId).IsUnique().HasFilter("group_id IS NOT NULL");
        builder.HasIndex(x => x.UploadedBy).IsUnique().HasFilter("group_id IS NULL");
    }
}

public class MapUploadedPlateRowConfiguration : BaseEntityLongConfiguration<MapUploadedPlateRow>
{
    public override void Configure(EntityTypeBuilder<MapUploadedPlateRow> builder)
    {
        base.Configure(builder);

        builder.ToTable("map_uploaded_plate_rows");

        builder.Property(x => x.MapUploadedFileId).HasColumnName("map_uploaded_file_id").IsRequired();

        builder.Property(x => x.RawPlate).HasColumnName("raw_plate").HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();
        builder.Property(x => x.VehicleType).HasColumnName("vehicle_type").HasMaxLength(100);
        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(150);
        builder.Property(x => x.Street).HasColumnName("street").HasMaxLength(150);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.RecorderName).HasColumnName("recorder_name").HasMaxLength(150);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasMaxLength(100);

        builder.Property(x => x.LocationLink).HasColumnName("location_link").HasMaxLength(500);
        builder.Property(x => x.Latitude).HasColumnName("latitude");
        builder.Property(x => x.Longitude).HasColumnName("longitude");

        builder.Property(x => x.ExtraDataJson).HasColumnName("extra_data_json").HasColumnType("jsonb");
        builder.Property(x => x.DataFileName).HasColumnName("data_file_name").HasMaxLength(255);
        builder.HasIndex(x => x.MapUploadedFileId);
        builder.HasIndex(x => x.NormalizedPlate);

        builder.HasOne(x => x.MapUploadedFile)
            .WithMany(x => x.Rows)
            .HasForeignKey(x => x.MapUploadedFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
