using Flit.Impuestos.Api.Modulos.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// Una lectura del Super Admin sobre los datos de otra compañía (forma de ADR-0073). <see cref="TenantId"/> es la compañía
/// consultada; la fila la escribe <see cref="AuditoriaLecturaBackoffice"/> antes de leer. Solo se inserta.
/// </summary>
public sealed class LecturaBackoffice : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ActorId { get; init; }

    /// <summary>Compañía cuyos datos se leyeron.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Qué se leyó (p. ej. <c>liquidaciones</c>, <c>dashboard</c>); sin valores de la consulta.</summary>
    public string Recurso { get; init; } = string.Empty;

    public DateTimeOffset En { get; init; }
}

/// <summary><c>impuestos.auditoria_lectura_backoffice</c> (plan §4.1).</summary>
internal sealed class LecturaBackofficeConfig : IEntityTypeConfiguration<LecturaBackoffice>
{
    public void Configure(EntityTypeBuilder<LecturaBackoffice> builder)
    {
        builder.ToTable("auditoria_lectura_backoffice", t => t.HasComment("Lecturas del Super Admin entre compañías (forma ADR-0073). Solo inserción."));
        builder.HasKey(x => x.Id).HasName("pk_auditoria_lectura_backoffice");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Recurso).HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.En }).HasDatabaseName("ix_auditoria_lectura_backoffice_tenant_en");
    }
}
