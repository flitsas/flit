using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Una celda que el reconciliador debe revisar: lo justo para fijar su empresa y saber su cola.</summary>
/// <param name="Id">Id de la celda.</param>
/// <param name="TenantId">Empresa de la celda; el reconciliador la fija antes de leer o escribir.</param>
/// <param name="EsExpress">Si su lote es Express: su trabajo va por <c>consultas.diagnostico-express</c> y no por la del lote.</param>
public sealed record CeldaHuerfana(Guid Id, Guid TenantId, bool EsExpress);

/// <summary>
/// Desde cuándo una celda con trabajo es candidata (ADR-0075, 6r-i, Dec. 6), siempre comparado con su <c>disponible_desde</c> (la
/// publicación de su trabajo o el vencimiento de su reintento).
/// </summary>
/// <param name="Limite">Publicada antes de esto (ahora − margen): lleva el margen sin resultado.</param>
/// <param name="Vencido">Publicada antes de esto (ahora − TTL − margen): su mensaje ya venció en el broker.</param>
/// <param name="PerdidasLote">Publicada antes de esto en la cola del lote (marca de agua − tolerancia): ya debió salir; null sin marca.</param>
/// <param name="PerdidasExpress">Lo mismo en la cola del Express.</param>
public sealed record UmbralesHuerfanas(DateTimeOffset Limite, DateTimeOffset Vencido, DateTimeOffset? PerdidasLote, DateTimeOffset? PerdidasExpress);

/// <summary>
/// Búsqueda de las celdas huérfanas de TODAS las empresas para el reconciliador (ADR-0075, D3, 6r-i; enmienda la excepción A11
/// de ADR-0072: el dueño de <c>ix_cola</c> pasa a ser el reconciliador, ya no la toma con lease). Desde ADR-0075 la cola es el
/// broker: aquí solo se buscan, por <c>ix_cola</c> (<c>estado IN ('PENDIENTE', 'EN_CURSO')</c> ordenado por
/// <c>disponible_desde</c>), las celdas que quedaron sin resultado (<see cref="UmbralesHuerfanas"/>): con <c>trabajo_id</c>,
/// publicadas (o con su reintento programado) antes del límite del margen y, además, perdidas según la marca de agua FIFO de SU
/// cola (lote o Express, por la cabecera del lote) o vencidas por el TTL del broker; o sin <c>trabajo_id</c> y ya disponibles (las
/// de antes del corte, que nunca se publicaron). Un lote grande que espera su turno no entra, así que no le quita el cupo de la
/// vuelta (<c>maximo</c>) a una celda perdida de la otra cola. Las diferidas (esperan a su RUNT) no entran: su
/// <c>disponible_desde</c> es el lejano.
/// <para>
/// Es la ÚNICA lectura del servicio que cruza empresas sin el filtro de EF, y por eso es SQL crudo parametrizado; no escribe nada y
/// solo devuelve ids y la empresa. <c>TomaEntreEmpresasSoloEnColaTests</c> falla si aparece SQL crudo en otro archivo. Lo demás del
/// trabajo se hace con la empresa de cada celda fijada en el contexto. El reloj es el inyectado, no el de la base.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var huerfanas = await cola.BuscarHuerfanasAsync(new UmbralesHuerfanas(ahora - margen, ahora - ttl - margen, marcaLote - tolerancia, null), ahora, 100, ct);
/// </code>
/// </example>
internal sealed class ColaConsultas(DiagnosticoDb db)
{
    private static readonly string Pendiente = CodigosDeEnum.Codigo(EstadoCelda.Pendiente);
    private static readonly string EnCurso = CodigosDeEnum.Codigo(EstadoCelda.EnCurso);

    // Los códigos de estado van como literales (constantes del enum, no entrada de usuario) para que el planificador pueda
    // usar el índice parcial ix_cola, cuyo predicado es estado IN ('PENDIENTE', 'EN_CURSO').
    private static readonly string SqlHuerfanas = $"""
        SELECT c.id, c.tenant_id, d.es_express
        FROM diagnostico.consultas c
        JOIN diagnostico.diagnosticos d ON d.tenant_id = c.tenant_id AND d.id = c.diagnostico_id
        WHERE c.estado IN ('{Pendiente}', '{EnCurso}')
          AND c.disponible_desde <= @ahora
          AND (c.trabajo_id IS NULL
               OR (c.disponible_desde < @limite
                   AND (c.disponible_desde < @vencido
                        OR c.disponible_desde < (CASE WHEN d.es_express THEN @perdidas_express ELSE @perdidas_lote END))))
        ORDER BY c.disponible_desde, c.id
        LIMIT @maximo
        """;

    /// <summary>
    /// Hasta <paramref name="maximo"/> celdas huérfanas: con trabajo, sin resultado tras el margen y perdidas o vencidas según
    /// <paramref name="umbrales"/>, o sin trabajo y disponibles en <paramref name="ahora"/>.
    /// </summary>
    public async Task<IReadOnlyList<CeldaHuerfana>> BuscarHuerfanasAsync(UmbralesHuerfanas umbrales, DateTimeOffset ahora, int maximo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(umbrales);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
        var abierta = conexion.State == System.Data.ConnectionState.Open;
        if (!abierta)
            await conexion.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var consulta = new NpgsqlCommand(SqlHuerfanas, conexion);
            // Npgsql solo escribe timestamptz en UTC.
            consulta.Parameters.AddWithValue("limite", umbrales.Limite.ToUniversalTime());
            consulta.Parameters.AddWithValue("vencido", umbrales.Vencido.ToUniversalTime());
            // Sin marca de agua (null) la comparación da NULL: ninguna celda de esa cola cuenta como perdida.
            consulta.Parameters.Add(new NpgsqlParameter("perdidas_lote", NpgsqlDbType.TimestampTz) { Value = (object?)umbrales.PerdidasLote?.ToUniversalTime() ?? DBNull.Value });
            consulta.Parameters.Add(new NpgsqlParameter("perdidas_express", NpgsqlDbType.TimestampTz) { Value = (object?)umbrales.PerdidasExpress?.ToUniversalTime() ?? DBNull.Value });
            consulta.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
            consulta.Parameters.AddWithValue("maximo", maximo);
            var huerfanas = new List<CeldaHuerfana>();
            await using var lector = await consulta.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await lector.ReadAsync(ct).ConfigureAwait(false))
                huerfanas.Add(new CeldaHuerfana(lector.GetGuid(0), lector.GetGuid(1), lector.GetBoolean(2)));
            return huerfanas;
        }
        finally
        {
            if (!abierta)
                await conexion.CloseAsync().ConfigureAwait(false);
        }
    }
}
