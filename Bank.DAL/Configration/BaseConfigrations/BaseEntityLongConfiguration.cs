using Bank.DAL.Models.BaseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.BaseConfigrations;

public abstract class BaseEntityLongConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : BaseEntityLong
{
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
    }
}