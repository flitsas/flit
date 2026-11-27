using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// <c>impuestos.data_protection_keys</c> (plan §4.2): el anillo de llaves de Data Protection del servicio, protegido con el
/// certificado propio. Son del servicio, no de una compañía: sin <c>tenant_id</c> ni filtro. La forma la fija el paquete.
/// </summary>
internal sealed class DataProtectionKeyConfig : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        builder.ToTable("data_protection_keys", t => t.HasComment("Llaves de ASP.NET Data Protection del servicio, cifradas con su certificado. Sin tenant."));
        builder.HasKey(x => x.Id).HasName("pk_data_protection_keys");
    }
}
