using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class HistoricalPlateConfiguration : BaseEntityLongConfiguration<HistoricalPlate>
{
    public override void Configure(EntityTypeBuilder<HistoricalPlate> builder)
    {
        base.Configure(builder);

        builder.ToTable("historical_plates");

        builder.Property(x => x.DataFileId).HasColumnName("data_file_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.RawPlate).HasColumnName("raw_plate").HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();
        builder.Property(x => x.VehicleType).HasColumnName("vehicle_type").HasMaxLength(100);
        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(150);
        builder.Property(x => x.Street).HasColumnName("street").HasMaxLength(150);
        builder.Property(x => x.Location)
            .HasColumnName("location")
            .HasColumnType("varchar(500)")
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("varchar(500)")
            .HasMaxLength(500)
            .IsRequired(false); ;

        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.RecorderName).HasColumnName("recorder_name").HasMaxLength(150);


        // الفرز بيعمل JOIN على normalized_plate وبعدين بيفلتر بالملف، فـ data_file_id
        // جوه نفس الـ index بيخلّي Postgres يستبعد الملفات اللي برّه النطاق من غير ما يقرا الصف.
        builder.HasIndex(x => new { x.NormalizedPlate, x.DataFileId });
        // للحذف/الاستبدال بالملف وللـ FK. مفيش index على user_id لأن مفيش query بتستخدمه.
        builder.HasIndex(x => x.DataFileId);

        builder.HasOne(x => x.DataFile)
            .WithMany(x => x.HistoricalPlates)
            .HasForeignKey(x => x.DataFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}