using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>Caché global de <c>GET /fuentes</c> del API (un departamento por fila, refresco cada 6 h).</summary>
public sealed class FuenteApi
{
    public string DepartamentoCodigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public bool Activa { get; set; }

    /// <summary>jsonb con el esquema de parámetros del departamento.</summary>
    public string ParamsSchema { get; set; } = "{}";

    public DateTimeOffset SincronizadoEn { get; set; }
}

/// <summary><c>impuestos.fuentes_api</c> (plan §4.1).</summary>
internal sealed class FuenteApiConfig : IEntityTypeConfiguration<FuenteApi>
{
    public void Configure(EntityTypeBuilder<FuenteApi> builder)
    {
        builder.ToTable("fuentes_api", t =>
        {
            t.HasComment("Caché de GET /fuentes del API de liquidación (global, refresco cada 6 h).");
            t.HasCheckConstraint("ck_fuentes_api_params_schema", "jsonb_typeof(params_schema) = 'object'");
        });
        builder.HasKey(x => x.DepartamentoCodigo).HasName("pk_fuentes_api");
        builder.Property(x => x.DepartamentoCodigo).HasMaxLength(2).ValueGeneratedNever();
        builder.Property(x => x.Nombre).HasMaxLength(80);
        builder.Property(x => x.ParamsSchema).HasColumnType("jsonb");
    }
}
