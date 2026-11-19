using Flit.Diagnostico.Api.Modulos.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.lecturas_backoffice</c> (HU #13722): auditoría de solo inserción de las lecturas entre empresas del
/// backoffice FLIT (F00.1-H5). Sin columnas de edición, borrado lógico ni <c>row_version</c>: una lectura no se modifica
/// (excepción documentada en ADR-0072).
/// </summary>
internal sealed class LecturaBackofficeConfig : IEntityTypeConfiguration<LecturaBackoffice>
{
    public void Configure(EntityTypeBuilder<LecturaBackoffice> builder)
    {
        builder.ToTable("lecturas_backoffice", t => t.HasComment(
            "Lecturas entre empresas del backoffice FLIT (D-16, ADR-0069/ADR-0072). Solo inserción."));
        builder.HasKey(x => x.Id).HasName("pk_lecturas_backoffice");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa del lector (token del backoffice).");
        builder.Property(x => x.EmpresaConsultada).HasComment("Empresa cuyos datos se leyeron (?empresa=).");
        builder.Property(x => x.UsuarioId).HasComment("@pii:low Usuario que leyó (claim sub, seudónimo).");
        builder.Property(x => x.Ruta).HasMaxLength(500).HasComment("Ruta y consulta leídas, sin cuerpo.");
        builder.Property(x => x.Fecha).HasDefaultValueSql("now()").HasComment("Momento de la lectura.");

        builder.HasIndex(x => new { x.TenantId, x.Fecha }).HasDatabaseName("ix_lecturas_backoffice_tenant_fecha");
        builder.HasIndex(x => new { x.TenantId, x.EmpresaConsultada, x.Fecha }).HasDatabaseName("ix_lecturas_backoffice_tenant_empresa_fecha");
    }
}
