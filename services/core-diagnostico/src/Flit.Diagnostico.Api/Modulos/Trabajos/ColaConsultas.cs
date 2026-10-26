using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Una celda que el reconciliador debe revisar: lo justo para fijar su empresa.</summary>
/// <param name="Id">Id de la celda.</param>
/// <param name="TenantId">Empresa de la celda; el reconciliador la fija antes de leer o escribir.</param>
public sealed record CeldaHuerfana(Guid Id, Guid TenantId);

/// <summary>
/// Búsqueda de las celdas huérfanas de TODAS las empresas para el reconciliador (ADR-0075, D3, 6r-i; enmienda la excepción A11
/// de ADR-0072: el dueño de <c>ix_cola</c> pasa a ser el reconciliador, ya no la toma con lease). Desde ADR-0075 la cola es el
/// broker: aquí solo se buscan, por <c>ix_cola</c> (<c>estado IN ('PENDIENTE', 'EN_CURSO')</c> ordenado por
/// <c>disponible_desde</c>), las celdas que quedaron sin resultado: con <c>trabajo_id</c> y publicadas (o con su reintento
/// programado) antes de <c>limite</c>, o sin <c>trabajo_id</c> y ya disponibles (las de antes del corte, que nunca se publicaron).
/// Las diferidas (esperan a su RUNT) no entran: su <c>disponible_desde</c> es el lejano.
/// <para>
/// Es la ÚNICA lectura del servicio que cruza empresas sin el filtro de EF, y por eso es SQL crudo parametrizado; no escribe nada y
/// solo devuelve ids y la empresa. <c>TomaEntreEmpresasSoloEnColaTests</c> falla si aparece SQL crudo en otro archivo. Lo demás del
/// trabajo se hace con la empresa de cada celda fijada en el contexto. El reloj es el inyectado, no el de la base.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var huerfanas = await cola.BuscarHuerfanasAsync(ahora - margen, ahora, 100, ct);
/// </code>
/// </example>
internal sealed class ColaConsultas(DiagnosticoDb db)
{
    private static readonly string Pendiente = CodigosDeEnum.Codigo(EstadoCelda.Pendiente);
    private static readonly string EnCurso = CodigosDeEnum.Codigo(EstadoCelda.EnCurso);

    // Los códigos de estado van como literales (constantes del enum, no entrada de usuario) para que el planificador pueda
    // usar el índice parcial ix_cola, cuyo predicado es estado IN ('PENDIENTE', 'EN_CURSO').
    private static readonly string SqlHuerfanas = $"""
        SELECT id, tenant_id
        FROM diagnostico.consultas
        WHERE estado IN ('{Pendiente}', '{EnCurso}')
          AND disponible_desde <= @ahora
          AND ((trabajo_id IS NOT NULL AND disponible_desde < @limite) OR trabajo_id IS NULL)
        ORDER BY disponible_desde, id
        LIMIT @maximo
        """;

    /// <summary>
    /// Hasta <paramref name="maximo"/> celdas huérfanas: con trabajo y sin resultado desde antes de <paramref name="limite"/>, o sin
    /// trabajo y disponibles en <paramref name="ahora"/>.
    /// </summary>
    public async Task<IReadOnlyList<CeldaHuerfana>> BuscarHuerfanasAsync(DateTimeOffset limite, DateTimeOffset ahora, int maximo, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
        var abierta = conexion.State == System.Data.ConnectionState.Open;
        if (!abierta)
            await conexion.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var consulta = new NpgsqlCommand(SqlHuerfanas, conexion);
            // Npgsql solo escribe timestamptz en UTC.
            consulta.Parameters.AddWithValue("limite", limite.ToUniversalTime());
            consulta.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
            consulta.Parameters.AddWithValue("maximo", maximo);
            var huerfanas = new List<CeldaHuerfana>();
            await using var lector = await consulta.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await lector.ReadAsync(ct).ConfigureAwait(false))
                huerfanas.Add(new CeldaHuerfana(lector.GetGuid(0), lector.GetGuid(1)));
            return huerfanas;
        }
        finally
        {
            if (!abierta)
                await conexion.CloseAsync().ConfigureAwait(false);
        }
    }
}
