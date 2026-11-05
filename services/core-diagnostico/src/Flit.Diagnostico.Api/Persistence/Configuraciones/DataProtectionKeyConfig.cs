using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.data_protection_keys</c> (HU #13735, patrón <c>HU10233_DataProtectionKeys</c> de core-api y de
/// core-consultas): las llaves de ASP.NET Data Protection del servicio, que cifran los documentos de propietario y locatario.
/// Sin ellas en la base, un reinicio dejaría ilegibles los documentos. Son del servicio, no de una empresa: no llevan
/// <c>tenant_id</c> ni filtro. La forma de la tabla la fija el paquete.
/// </summary>
internal sealed class DataProtectionKeyConfig : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        builder.ToTable("data_protection_keys", t => t.HasComment("Llaves de ASP.NET Data Protection del servicio (HU #13735). Sin tenant."));
        builder.HasKey(x => x.Id).HasName("pk_data_protection_keys");
    }
}
