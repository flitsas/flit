using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.saneamiento_renglones</c> (HU #14576, schema.md §3.2/§5.3, ADR-0087): clase (a) con <c>xmin</c>, FK compuesta
/// RESTRICT al saneamiento. El renglón se quita con borrado lógico (<c>quitado_en</c>, SP-2, decisión E2 que manda sobre C17): el
/// único por fuente (<see cref="IndiceFuente"/>) solo cuenta los no quitados. <c>evidencia_reemplazos</c> (0..20) sostiene el tope de
/// reemplazos de la evidencia (SP-19). Las seis columnas de la evidencia van todas nulas o todas con valor.
/// </summary>
internal sealed class RenglonSaneamientoConfig : IEntityTypeConfiguration<RenglonSaneamiento>
{
    /// <summary>Una sola fila no quitada por fuente automática; OTRO puede repetirse.</summary>
    public const string IndiceFuente = "ux_saneamiento_renglones_fuente";

    public void Configure(EntityTypeBuilder<RenglonSaneamiento> builder)
    {
        static string F(FuenteSaneamiento f) => CodigosDeEnum.Codigo(f);
        static string H(HallazgoSaneamiento h) => CodigosDeEnum.Codigo(h);
        var otro = F(FuenteSaneamiento.Otro);
        var evidencia = new[] { "evidencia_nombre", "evidencia_sha256", "evidencia_tamano", "evidencia_subida_por", "evidencia_subida_en" };

        builder.ToTable("saneamiento_renglones", t =>
        {
            t.HasComment("Renglón de un saneamiento por fuente (clase (a) de ADR-0072, ADR-0087): hallazgo, resultado, nota y PDF opcional de evidencia. OTRO lo agrega FLIT. Se quita con borrado lógico (quitado_en).");
            t.HasCheckConstraint("ck_saneamiento_renglones_fuente", $"fuente IN ({CodigosDeEnum.ListaSql<FuenteSaneamiento>()})");
            t.HasCheckConstraint("ck_saneamiento_renglones_hallazgo", $"hallazgo IS NULL OR hallazgo IN ({CodigosDeEnum.ListaSql<HallazgoSaneamiento>()})");
            t.HasCheckConstraint("ck_saneamiento_renglones_resultado", $"resultado IN ({CodigosDeEnum.ListaSql<ResultadoRenglonSaneamiento>()})");
            t.HasCheckConstraint("ck_saneamiento_renglones_otro",
                $"(fuente = '{otro}') = (hallazgo IS NULL) AND (fuente = '{otro}') = (descripcion IS NOT NULL) AND automatico = (fuente <> '{otro}') AND (descripcion IS NULL OR char_length(btrim(descripcion)) BETWEEN 3 AND 200)");
            t.HasCheckConstraint("ck_saneamiento_renglones_hallazgo_fuente",
                "hallazgo IS NULL"
                + $" OR (hallazgo = '{H(HallazgoSaneamiento.Deuda)}' AND fuente IN ('{F(FuenteSaneamiento.SimitVehiculo)}', '{F(FuenteSaneamiento.SimitPropietario)}', '{F(FuenteSaneamiento.SimitLocatario)}', '{F(FuenteSaneamiento.Impuesto)}'))"
                + $" OR (hallazgo = '{H(HallazgoSaneamiento.SinVerificar)}' AND fuente = '{F(FuenteSaneamiento.SimitLocatario)}')"
                + $" OR (hallazgo = '{H(HallazgoSaneamiento.NoVigente)}' AND fuente IN ('{F(FuenteSaneamiento.Soat)}', '{F(FuenteSaneamiento.Rtm)}'))");
            t.HasCheckConstraint("ck_saneamiento_renglones_nota", "nota IS NULL OR btrim(nota) <> ''");
            t.HasCheckConstraint("ck_saneamiento_renglones_evidencia",
                string.Join(" AND ", evidencia.Select(c => $"(evidencia_referencia IS NULL) = ({c} IS NULL)")));
            t.HasCheckConstraint("ck_saneamiento_renglones_evidencia_sha256", "evidencia_sha256 IS NULL OR evidencia_sha256 ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_saneamiento_renglones_evidencia_tamano", "evidencia_tamano IS NULL OR evidencia_tamano > 0");
            t.HasCheckConstraint("ck_saneamiento_renglones_actualizado", "(actualizado_por IS NULL) = (actualizado_en IS NULL)");
            t.HasCheckConstraint("ck_saneamiento_renglones_reemplazos", "evidencia_reemplazos BETWEEN 0 AND 20");
        });
        builder.HasKey(x => x.Id).HasName("pk_saneamiento_renglones");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del saneamiento.");
        builder.Property(x => x.SaneamientoId).HasComment("Saneamiento al que pertenece.");
        builder.Property(x => x.Fuente).HasConversion(CodigosDeEnum.Convertidor<FuenteSaneamiento>()).HasMaxLength(24)
            .HasComment("SIMIT_VEHICULO, SIMIT_PROPIETARIO, SIMIT_LOCATARIO, IMPUESTO, SOAT, RTM u OTRO. Una sola no quitada por fuente salvo OTRO.");
        builder.Property(x => x.Hallazgo).HasConversion(CodigosDeEnum.Convertidor<HallazgoSaneamiento>()).HasMaxLength(16)
            .HasComment("DEUDA, NO_VIGENTE o SIN_VERIFICAR; foto al tomar (no se regenera). Null en OTRO.");
        builder.Property(x => x.Descripcion).HasMaxLength(200).HasComment("@pii:low Descripción de un renglón OTRO (texto de FLIT, 3..200).");
        builder.Property(x => x.Automatico).HasComment("True si lo generó el sistema al tomar; false en OTRO.");
        builder.Property(x => x.Resultado).HasConversion(CodigosDeEnum.Convertidor<ResultadoRenglonSaneamiento>()).HasMaxLength(12)
            .HasDefaultValueSql("'PENDIENTE'").ValueGeneratedNever()
            .HasComment("PENDIENTE, SANEADO o NO_PROCEDE.");
        builder.Property(x => x.Nota).HasMaxLength(500).HasComment("@pii:low Nota de FLIT, visible al cliente; nunca en logs.");
        builder.Property(x => x.EvidenciaReferencia).HasMaxLength(256)
            .HasComment("@pii:none Referencia del archivo en el almacén (el PDF puede traer datos de terceros, C21); nunca se expone ni se loguea.");
        builder.Property(x => x.EvidenciaNombre).HasMaxLength(200)
            .HasComment("@pii:low Nombre original del PDF, saneado (puede traer un nombre de persona); nunca en logs.");
        builder.Property(x => x.EvidenciaSha256).HasColumnType("char(64)").HasComment("@pii:none SHA-256 hex del PDF; nunca se expone.");
        builder.Property(x => x.EvidenciaTamano).HasComment("@pii:none Tamaño del PDF en bytes.");
        builder.Property(x => x.EvidenciaSubidaPor).HasComment("@pii:low Usuario FLIT que cargó el PDF vigente.");
        builder.Property(x => x.EvidenciaSubidaEn).HasComment("@pii:none Instante de carga del PDF vigente.");
        builder.Property(x => x.EvidenciaReemplazos).HasDefaultValueSql("0").ValueGeneratedNever()
            .HasComment("@pii:none Veces que se reemplazó el PDF del renglón (0..20, SP-19).");
        builder.Property(x => x.CreadoPor).HasComment("@pii:low Usuario FLIT que tomó (automáticos) o agregó (OTRO).");
        builder.Property(x => x.CreadoEn).HasComment("Instante de creación del renglón (timestamptz).");
        builder.Property(x => x.ActualizadoPor).HasComment("@pii:low Último usuario FLIT que cambió resultado, nota o evidencia.");
        builder.Property(x => x.ActualizadoEn).HasComment("Instante del último cambio; null si nunca cambió.");
        builder.Property(x => x.QuitadoEn)
            .HasComment("Borrado lógico (SP-2): instante en que FLIT quitó el renglón; null si sigue. Las lecturas filtran quitado_en IS NULL; quién lo quitó queda en saneamiento_historial.");
        foreach (var inmutable in new[] { nameof(RenglonSaneamiento.SaneamientoId), nameof(RenglonSaneamiento.Fuente),
                     nameof(RenglonSaneamiento.Hallazgo), nameof(RenglonSaneamiento.Automatico) })
            builder.Property(inmutable).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        builder.HasOne<Saneamiento>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SaneamientoId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_saneamiento_renglones_saneamientos").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.SaneamientoId, x.Fuente }).IsUnique()
            .HasFilter($"fuente <> '{otro}' AND quitado_en IS NULL").HasDatabaseName(IndiceFuente);
        builder.HasIndex(x => new { x.TenantId, x.SaneamientoId }).HasDatabaseName("ix_saneamiento_renglones_saneamiento");

        builder.Property(x => x.Xmin).IsRowVersion();
    }
}
