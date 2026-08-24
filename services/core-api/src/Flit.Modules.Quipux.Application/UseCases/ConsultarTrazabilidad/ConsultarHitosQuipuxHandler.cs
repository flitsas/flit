using Flit.Modules.Quipux.Domain.LogQx;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarTrazabilidad;

/// <summary>
/// Hitos de una radicación (HU #11787): la línea de tiempo con el sondeo repetido YA AGRUPADO.
/// </summary>
/// <remarks>
/// <para>Es la pieza que hace usable la pantalla. El caso de referencia —el trámite 27172 de FLIT
/// 1.0— acumula 1.065 eventos para representar cinco cosas: se consolidó, se subió, se radicó, se
/// está consultando, y falta la decisión. Los otros 1.060 son el mismo latido repetido cada diez
/// minutos.</para>
/// <para>La agrupación se resuelve AQUÍ y no en el navegador (ADR-0051, D2): así el payload es
/// constante con independencia de cuánto lleve el trámite esperando.</para>
/// </remarks>
public sealed class ConsultarHitosQuipuxHandler
{
    private readonly IQuipuxTrazabilidadRepository _repository;
    private readonly TimeProvider _clock;

    public ConsultarHitosQuipuxHandler(
        IQuipuxTrazabilidadRepository repository,
        TimeProvider? clock = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Devuelve <c>null</c> si la radicación no existe — el borde lo traduce a 404.</summary>
    public async Task<ConsultarHitosQuipuxResult?> HandleAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var radicacion = await _repository
            .GetRadicacionAsync(submissionId, cancellationToken)
            .ConfigureAwait(false);

        if (radicacion is null)
        {
            return null;
        }

        var eventos = await _repository
            .ListEventosParaHitosAsync(submissionId, cancellationToken)
            .ConfigureAwait(false);

        return new ConsultarHitosQuipuxResult(
            MapRadicacion(radicacion, _clock.GetUtcNow()),
            Agrupar(eventos));
    }

    /// <summary>
    /// Recorre los eventos en orden y colapsa las RACHAS de latidos consecutivos en un solo bloque.
    /// </summary>
    /// <remarks>
    /// Un evento que no es latido corta la racha y se emite tal cual. Por eso una radicación que se
    /// sondeó mil veces, recibió el rechazo y siguió sondeando produce DOS bloques con el rechazo en
    /// medio, y no un único bloque de mil uno que escondería el rechazo entre el ruido — que es
    /// justamente lo que no debe pasar.
    /// <para>Pública y estática a propósito: es una función pura y es la regla que decide qué se le
    /// oculta al usuario, así que tiene que poder probarse sola, sin montar el handler ni la base.</para>
    /// </remarks>
    public static List<QuipuxHitoView> Agrupar(IReadOnlyList<QuipuxEventoResumen> eventos)
    {
        var hitos = new List<QuipuxHitoView>();
        List<QuipuxEventoResumen>? racha = null;

        void CerrarRacha()
        {
            if (racha is null)
            {
                return;
            }

            hitos.Add(BloqueDeSondeo(racha));
            racha = null;
        }

        foreach (var e in eventos)
        {
            if (QuipuxSondeo.EsLatido(e))
            {
                (racha ??= []).Add(e);
                continue;
            }

            CerrarRacha();
            hitos.Add(new QuipuxHitoView(
                Tipo: QuipuxHitoTipo.Hito,
                Stage: e.Stage,
                Outcome: e.Outcome,
                OccurredAt: e.OccurredAt,
                Hasta: null,
                DurationMs: e.DurationMs,
                Codigo: e.Codigo,
                EstadoTramite: e.EstadoTramite,
                Mensaje: e.Mensaje,
                CorrelationId: e.CorrelationId,
                Consultas: null,
                DuracionMediaMs: null));
        }

        CerrarRacha();
        return hitos;
    }

    private static QuipuxHitoView BloqueDeSondeo(List<QuipuxEventoResumen> racha)
    {
        var conDuracion = racha.Where(x => x.DurationMs is not null).ToList();

        // La media solo se calcula sobre los que la traen: los eventos previos a la instrumentación
        // no tienen duración, y contarlos como cero rebajaría la media y mentiría sobre el servicio.
        long? media = conDuracion.Count > 0
            ? (long)Math.Round(conDuracion.Average(x => x.DurationMs!.Value))
            : null;

        var ultimo = racha[^1];

        return new QuipuxHitoView(
            Tipo: QuipuxHitoTipo.Sondeo,
            Stage: ultimo.Stage,
            Outcome: ultimo.Outcome,
            OccurredAt: racha[0].OccurredAt,
            Hasta: ultimo.OccurredAt,
            DurationMs: null,
            Codigo: ultimo.Codigo,
            EstadoTramite: ultimo.EstadoTramite,
            Mensaje: null,
            CorrelationId: null,
            Consultas: racha.Count,
            DuracionMediaMs: media);
    }

    private static QuipuxRadicacionView MapRadicacion(
        QuipuxTrazabilidadRadicacion r, DateTimeOffset now)
    {
        // Solo los estados no terminales acumulan espera; en un aprobado o un rechazado la
        // antigüedad no significa nada porque el trámite ya se resolvió.
        DateTimeOffset? esperandoDesde = r.Status is "pendiente" or "registrado" ? r.CreatedAt : null;
        double? horas = esperandoDesde is { } desde && desde <= now
            ? (now - desde).TotalHours
            : null;

        return new QuipuxRadicacionView(
            r.Id,
            r.ProcedureInstanceId,
            r.ReferenceNumber,
            r.Plate,
            r.ProcedureTypeName,
            r.ClientTenantName,
            r.TransitOfficeName,
            r.DivipoCode,
            r.DocumentoQx,
            r.Status,
            r.Attempts,
            r.PollCount,
            r.QxRegisterCode,
            r.QxProcedureCode,
            r.RejectionReason,
            r.CreatedAt,
            r.RegisteredAt,
            r.LastPolledAt,
            r.CompletedAt,
            r.UpdatedAt,
            esperandoDesde,
            horas,
            r.Intento,
            r.TotalIntentos,
            r.Hermanas
                .Select(h => new QuipuxHermanaView(h.Id, h.Intento, h.Status, h.CreatedAt))
                .ToList());
    }
}
