using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.saneamiento_historial</c> (HU #14576, schema.md §3.3/§5.4, ADR-0087): clase (b) de ADR-0072, solo inserción y sin
/// token. <c>renglon_id</c> va sin FK (el renglón se puede quitar y el historial no se reescribe; excepción de ADR-0087). Los CHECK
/// atan cada evento a sus columnas: transiciones con estado nuevo, eventos de renglón con renglón y fuente, y motivo solo donde §3.2
/// lo exige.
/// </summary>
internal sealed class EventoSaneamientoConfig : IEntityTypeConfiguration<EventoSaneamiento>
{
    public void Configure(EntityTypeBuilder<EventoSaneamiento> builder)
    {
        static string E(TipoEventoSaneamiento e) => CodigosDeEnum.Codigo(e);
        var estados = CodigosDeEnum.ListaSql<EstadoSaneamiento>();
        var deRenglon = string.Join(", ", new[] { TipoEventoSaneamiento.RenglonAgregado, TipoEventoSaneamiento.RenglonQuitado,
            TipoEventoSaneamiento.RenglonActualizado, TipoEventoSaneamiento.EvidenciaCargada, TipoEventoSaneamiento.EvidenciaQuitada }.Select(e => $"'{E(e)}'"));
        var deTransicion = string.Join(", ", new[] { TipoEventoSaneamiento.Solicitado, TipoEventoSaneamiento.Desmarcado, TipoEventoSaneamiento.Tomado,
            TipoEventoSaneamiento.CerradoSaneado, TipoEventoSaneamiento.CerradoNoProcede, TipoEventoSaneamiento.Reabierto, TipoEventoSaneamiento.AnuladoFlit }.Select(e => $"'{E(e)}'"));
        var conMotivo = string.Join(", ", new[] { TipoEventoSaneamiento.CerradoNoProcede, TipoEventoSaneamiento.Reabierto,
            TipoEventoSaneamiento.AnuladoFlit }.Select(e => $"'{E(e)}'"));

        builder.ToTable("saneamiento_historial", t =>
        {
            t.HasComment("Eventos del circuito de saneamiento (clase (b) de ADR-0072, ADR-0087): auditoría con actor e instante; renglon_id sin FK. Solo inserción.");
            t.HasCheckConstraint("ck_saneamiento_historial_evento", $"evento IN ({CodigosDeEnum.ListaSql<TipoEventoSaneamiento>()})");
            t.HasCheckConstraint("ck_saneamiento_historial_estado_anterior", $"estado_anterior IS NULL OR estado_anterior IN ({estados})");
            t.HasCheckConstraint("ck_saneamiento_historial_estado_nuevo", $"estado_nuevo IS NULL OR estado_nuevo IN ({estados})");
            t.HasCheckConstraint("ck_saneamiento_historial_fuente", $"fuente IS NULL OR fuente IN ({CodigosDeEnum.ListaSql<FuenteSaneamiento>()})");
            t.HasCheckConstraint("ck_saneamiento_historial_renglon",
                $"(evento IN ({deRenglon})) = (renglon_id IS NOT NULL) AND (renglon_id IS NULL) = (fuente IS NULL)");
            t.HasCheckConstraint("ck_saneamiento_historial_transicion",
                $"(evento IN ({deTransicion})) = (estado_nuevo IS NOT NULL) AND (estado_anterior IS NULL) = (evento = '{E(TipoEventoSaneamiento.Solicitado)}' OR estado_nuevo IS NULL)");
            t.HasCheckConstraint("ck_saneamiento_historial_motivo",
                $"(evento IN ({conMotivo})) = (motivo IS NOT NULL) AND (motivo IS NULL OR char_length(btrim(motivo)) BETWEEN 10 AND 500)");
        });
        builder.HasKey(x => x.Id).HasName("pk_saneamiento_historial");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación; referencia_id de los avisos SANEAMIENTO_*.");
        builder.Property(x => x.TenantId).HasComment("Empresa dueña del saneamiento (también cuando actúa FLIT).");
        builder.Property(x => x.SaneamientoId).HasComment("Saneamiento del evento.");
        builder.Property(x => x.Evento).HasConversion(CodigosDeEnum.Convertidor<TipoEventoSaneamiento>()).HasMaxLength(24)
            .HasComment("SOLICITADO, DESMARCADO, TOMADO, RENGLON_*, EVIDENCIA_*, OBSERVACION_EDITADA, CERRADO_*, REABIERTO o ANULADO_FLIT.");
        builder.Property(x => x.EstadoAnterior).HasConversion(CodigosDeEnum.Convertidor<EstadoSaneamiento>()).HasMaxLength(16)
            .HasComment("Estado de partida en las transiciones; null en SOLICITADO y en los eventos de edición.");
        builder.Property(x => x.EstadoNuevo).HasConversion(CodigosDeEnum.Convertidor<EstadoSaneamiento>()).HasMaxLength(16)
            .HasComment("Estado de llegada en las transiciones; null en los eventos de edición.");
        builder.Property(x => x.RenglonId).HasComment("Renglón del evento; sin FK porque el renglón se puede quitar (ADR-0087).");
        builder.Property(x => x.Fuente).HasConversion(CodigosDeEnum.Convertidor<FuenteSaneamiento>()).HasMaxLength(24)
            .HasComment("Copia de la fuente del renglón, legible aunque el renglón ya no esté.");
        builder.Property(x => x.UsuarioId).HasComment("@pii:low Actor (claim sub, seudónimo). Al cliente solo se le muestra COMPANIA o FLIT.");
        builder.Property(x => x.EsFlit).HasComment("True si actuó por operación FLIT.");
        builder.Property(x => x.Motivo).HasMaxLength(500)
            .HasComment("@pii:low Motivo de NO_PROCEDE, reapertura o anulación FLIT (10..500); nunca en logs ni avisos.");
        builder.Property(x => x.OcurridoEn).HasComment("Instante del evento (timestamptz).");

        builder.HasOne<Saneamiento>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SaneamientoId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .HasConstraintName("fk_saneamiento_historial_saneamientos").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.SaneamientoId, x.OcurridoEn, x.Id }).HasDatabaseName("ix_saneamiento_historial_saneamiento");

        // Sin token de concurrencia: clase (b).
    }
}
