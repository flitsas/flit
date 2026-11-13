using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Notificaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.notificaciones</c> (HU #14563 B8, schema §3.2, ADR-0086): clase (a) de ADR-0072 con <c>xmin</c>, filtro por empresa
/// y cinco excepciones escritas en ADR-0086 (purga a 90 días, CASCADE hacia la cabecera, <c>referencia_id</c> sin FK, índice de
/// retención sin <c>tenant_id</c> al frente y purga entre empresas). El CHECK de <c>datos</c> impide guardar texto libre: objeto plano
/// de hasta 512 bytes, claves <c>^[a-z_]{1,32}$</c>, textos <c>^[A-Z0-9_]{1,32}$</c> y números de 0 a 100000 (hallazgo L1 de seguridad).
/// </summary>
internal sealed class NotificacionConfig : IEntityTypeConfiguration<Notificacion>
{
    internal const string CheckDatos =
        "jsonb_typeof(datos) = 'object' AND NOT jsonb_path_exists(datos, 'strict $.* ? (@.type() == \"object\" || @.type() == \"array\" || (@.type() == \"string\" && !(@ like_regex \"^[A-Z0-9_]{1,32}$\")) || (@.type() == \"number\" && (@ < 0 || @ > 100000)))') AND NOT jsonb_path_exists(datos, 'strict $.keyvalue() ? (!(@.key like_regex \"^[a-z_]{1,32}$\"))') AND octet_length(datos::text) <= 512";

    public void Configure(EntityTypeBuilder<Notificacion> builder)
    {
        var tipos = string.Join(", ", Notificacion.Tipos.Select(t => $"'{t}'"));
        builder.ToTable("notificaciones", t =>
        {
            t.HasComment("Avisos de la campana de Diagnóstico por usuario (clase (a) de ADR-0072 con excepción de retención de ADR-0086): sin texto, documentos ni PII; el mensaje se arma al leer. Purga física a los 90 días.");
            t.HasCheckConstraint("ck_notificaciones_tipo", $"tipo IN ({tipos})");
            t.HasCheckConstraint("ck_notificaciones_datos", CheckDatos);
            t.HasCheckConstraint("ck_notificaciones_leida", "leida_en IS NULL OR leida_en >= creada_en");
        });
        builder.HasKey(x => x.Id).HasName("pk_notificaciones");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del lote y de la notificación.");
        builder.Property(x => x.UsuarioId).HasComment("@pii:low Destinatario: seudónimo uuid (claim sub); quien disparó el hecho.");
        builder.Property(x => x.Tipo).HasMaxLength(32)
            .HasComment("ZIP_EVIDENCIAS_LISTO, ZIP_EVIDENCIAS_FALLIDO, DERIVACION_TERMINADA o LOTE_TERMINADO (D8: sin DICTAMEN_AJUSTADO).");
        builder.Property(x => x.DiagnosticoId).HasComment("Lote al que se refiere el aviso.");
        builder.Property(x => x.ReferenciaId)
            .HasComment("Id del hecho según tipo: evidencias.id (ZIP_*), derivacion_operaciones.id (DERIVACION_TERMINADA), historial_estado_diagnostico.id (LOTE_TERMINADO). Sin FK (polimórfica, ADR-0086).");
        builder.Property(x => x.Datos).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb")
            .HasComment("@pii:none Solo contadores y códigos (p. ej. {\"creadas\":3,\"fallidas\":1}); el CHECK impide texto libre.");
        builder.Property(x => x.CreadaEn).HasComment("Instante del hecho (timestamptz). Base de la retención.");
        builder.Property(x => x.LeidaEn)
            .HasComment("Instante en que el destinatario la marcó leída; null si no. Se escribe GREATEST(ahora, creada_en).");
        builder.Property(x => x.Xmin).IsRowVersion();

        builder.HasOne<CabeceraDiagnostico>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticoId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_notificaciones_diagnosticos")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.UsuarioId, x.Tipo, x.ReferenciaId }).IsUnique().HasDatabaseName("ux_notificaciones_idempotencia");
        builder.HasIndex(x => new { x.TenantId, x.UsuarioId, x.CreadaEn, x.Id }).IsDescending(false, false, true, true)
            .IncludeProperties(nameof(Notificacion.LeidaEn)).HasDatabaseName("ix_notificaciones_bandeja");
        builder.HasIndex(x => new { x.TenantId, x.UsuarioId }).HasFilter("leida_en IS NULL").HasDatabaseName("ix_notificaciones_no_leidas");
        builder.HasIndex(x => new { x.TenantId, x.DiagnosticoId }).HasDatabaseName("ix_notificaciones_diagnostico");
        builder.HasIndex(x => x.CreadaEn).HasDatabaseName("ix_notificaciones_retencion");
    }
}
