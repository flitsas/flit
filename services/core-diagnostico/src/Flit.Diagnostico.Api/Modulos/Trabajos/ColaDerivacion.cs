using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Un ítem de derivación colgado: solo su id y su empresa (nada del vehículo).</summary>
internal sealed record ItemColgado(Guid Id, Guid TenantId);

/// <summary>
/// El SQL crudo de la derivación masiva (Punto 4, incremento 3, ADR-0084 y ADR-0072; autorizado en
/// <c>TomaEntreEmpresasSoloEnColaTests</c>), parametrizado y sin concatenar valores:
/// <list type="bullet">
/// <item><see cref="BloquearCompaniaAsync"/>: el candado del crédito de UNA compañía, <c>pg_advisory_xact_lock</c> sobre
/// <c>hashtextextended('derivacion:' || empresa, 0)</c> en la transacción en curso (se suelta al confirmar o revertir). Dos
/// encolados o cierres de la misma compañía se ordenan; los de compañías distintas no se esperan. No lee filas.</item>
/// <item><see cref="BuscarColgadosAsync"/>: la única lectura entre compañías del módulo, para el reconciliador: ítems ENVIADO desde
/// antes del margen y sin una espera de reintento futura; devuelve solo id y empresa (cada uno se trabaja luego con su empresa
/// fijada).</item>
/// </list>
/// </summary>
internal static class ColaDerivacion
{
    private const string SqlBloquear = "SELECT pg_advisory_xact_lock(hashtextextended(@clave, 0))";

    private const string SqlColgados = """
        SELECT id, tenant_id
          FROM diagnostico.derivaciones
         WHERE estado = 'ENVIADO'
           AND enviado_en < @limite
           AND (disponible_desde IS NULL OR disponible_desde <= @ahora)
         ORDER BY enviado_en
         LIMIT @maximo
        """;

    /// <summary>Clave del candado de la compañía.</summary>
    public static string Clave(Guid empresa) => $"derivacion:{empresa:D}";

    /// <summary>Toma el candado del crédito de <paramref name="empresa"/> en la transacción en curso (obligatoria).</summary>
    public static async Task BloquearCompaniaAsync(DiagnosticoDb db, Guid empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var transaccion = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("El candado del crédito exige una transacción en curso (se suelta con ella).");
        var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var comando = new NpgsqlCommand(SqlBloquear, conexion, (NpgsqlTransaction)transaccion.GetDbTransaction());
        comando.Parameters.AddWithValue("clave", Clave(empresa));
        await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Hasta <paramref name="maximo"/> ítems ENVIADO antes de <paramref name="limite"/> sin espera futura, de todas las empresas.</summary>
    public static async Task<IReadOnlyList<ItemColgado>> BuscarColgadosAsync(DiagnosticoDb db, DateTimeOffset limite, DateTimeOffset ahora, int maximo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
        var abierta = conexion.State == System.Data.ConnectionState.Open;
        if (!abierta)
            await conexion.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var consulta = new NpgsqlCommand(SqlColgados, conexion);
            // Npgsql solo escribe timestamptz en UTC.
            consulta.Parameters.AddWithValue("limite", limite.ToUniversalTime());
            consulta.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
            consulta.Parameters.AddWithValue("maximo", maximo);
            var colgados = new List<ItemColgado>();
            await using var lector = await consulta.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await lector.ReadAsync(ct).ConfigureAwait(false))
                colgados.Add(new ItemColgado(lector.GetGuid(0), lector.GetGuid(1)));
            return colgados;
        }
        finally
        {
            if (!abierta)
                await conexion.CloseAsync().ConfigureAwait(false);
        }
    }
}
