using Flit.Impuestos.Api.Modulos.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// Configuración de Impuestos por compañía en dos niveles: contractual (Super Admin: tope del lote y departamentos habilitados) y
/// operativa (Admin: tope propio, nunca mayor que el contractual, y correos de aviso).
/// </summary>
public sealed class ConfiguracionEmpresa : IEntidadDeEmpresa
{
    public Guid TenantId { get; set; }

    public int MaxRegistrosLoteContractual { get; set; } = 500;

    /// <summary>Códigos DANE de departamento habilitados; null = todos los activos de <c>/fuentes</c>.</summary>
    public string[]? DepartamentosHabilitados { get; set; }

    /// <summary>Tope operativo; menor o igual al contractual (lo valida la aplicación).</summary>
    public int? MaxRegistrosLoteOperativo { get; set; }

    public string[] CorreosAviso { get; set; } = [];

    public long Version { get; set; } = 1;

    public DateTimeOffset ActualizadoEn { get; set; }

    public Guid? ActualizadoPor { get; set; }

    /// <summary>
    /// Token de concurrencia optimista (columna de sistema <c>xmin</c>): un guardado sobre una lectura vieja falla con
    /// <c>DbUpdateConcurrencyException</c> en vez de pisar la fila. <see cref="Version"/> es otra cosa: la versión de la configuración.
    /// </summary>
    public uint Xmin { get; set; }
}

/// <summary><c>impuestos.configuracion_empresa</c> (plan §4.1).</summary>
internal sealed class ConfiguracionEmpresaConfig : IEntityTypeConfiguration<ConfiguracionEmpresa>
{
    public void Configure(EntityTypeBuilder<ConfiguracionEmpresa> builder)
    {
        builder.ToTable("configuracion_empresa", t =>
        {
            t.HasComment("Configuración de Impuestos por compañía (contractual y operativa).");
            t.HasCheckConstraint("ck_configuracion_empresa_max_contractual", "max_registros_lote_contractual BETWEEN 1 AND 5000");
        });
        builder.HasKey(x => x.TenantId).HasName("pk_configuracion_empresa");
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Property(x => x.MaxRegistrosLoteContractual).HasDefaultValue(500).ValueGeneratedNever();
        builder.Property(x => x.DepartamentosHabilitados).HasColumnType("varchar(2)[]").HasComment("NULL = todos los activos de /fuentes.");
        builder.Property(x => x.CorreosAviso).HasColumnType("varchar(254)[]").HasDefaultValueSql("'{}'").ValueGeneratedNever()
            .HasComment("@pii:low Correos de aviso de la compañía.");
        builder.Property(x => x.Version).HasDefaultValue(1L).ValueGeneratedNever();
        builder.Property(x => x.ActualizadoPor).HasComment(ComentariosPii.Actor);
        builder.Property(x => x.Xmin).IsRowVersion();
    }
}
