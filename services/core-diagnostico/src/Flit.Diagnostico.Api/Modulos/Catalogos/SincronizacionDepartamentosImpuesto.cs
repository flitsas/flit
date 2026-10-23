using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>Una vuelta de la réplica de departamentos con impuesto (HU #13861); null si no hubo respuesta OK.</summary>
public interface ISincronizacionDepartamentosImpuesto
{
    Task<ResultadoSincronizacionImpuesto?> SincronizarAsync(CancellationToken ct);
}

/// <summary>
/// Resultado de una sincronización con respuesta OK: códigos recibidos, soportados tras normalizar, filas nuevas, retirados
/// (pasaron de soportado a no soportado) y descartados (vacíos, de más de 8 caracteres o repetidos).
/// </summary>
public sealed record ResultadoSincronizacionImpuesto(int Recibidos, int Soportados, int Nuevos, int Retirados, int Descartados);

/// <summary>
/// Réplica de <c>diagnostico.departamentos_impuesto</c> (HU #13861, F09-C2; tabla de HU #13723, global y sin empresa por excepción
/// de ADR-0072) desde la capacidad de core-consultas (<see cref="ICapacidadImpuestoPlataforma"/>). Con null (no conectado o el rpc
/// falló: UNIMPLEMENTED, red, timeout) no cambia nada. Con una lista, normaliza los códigos como
/// <see cref="SincronizacionOrganismos"/> (recorta, antepone 0 a un dígito, descarta vacíos, de más de 8 caracteres y repetidos),
/// deja <c>soportado=true</c> en los que llegan y <c>soportado=false</c> en los que estaban soportados y ya no llegan, con
/// <c>actualizado_en</c> del <see cref="TimeProvider"/> en cada fila que confirma o cambia. Nunca borra filas (Q3=a); una lista
/// vacía con OK deja todos en false (cierre seguro, Q2=a). Siempre termina recargando <see cref="EstadoCapacidadImpuesto"/>.
/// </summary>
/// <example>
/// <code>
/// var resultado = await scope.ServiceProvider.GetRequiredService&lt;ISincronizacionDepartamentosImpuesto&gt;().SincronizarAsync(ct);
/// </code>
/// </example>
public sealed partial class SincronizacionDepartamentosImpuesto(
    DiagnosticoDb db, ICapacidadImpuestoPlataforma capacidad, EstadoCapacidadImpuesto estado, TimeProvider reloj,
    ILogger<SincronizacionDepartamentosImpuesto> logger) : ISincronizacionDepartamentosImpuesto
{
    /// <summary>Largo de <c>departamentos_impuesto.departamento_codigo</c>.</summary>
    private const int LargoCodigo = 8;

    public async Task<ResultadoSincronizacionImpuesto?> SincronizarAsync(CancellationToken ct)
    {
        var remotos = await capacidad.ListarAsync(ct).ConfigureAwait(false);
        if (remotos is null)
        {
            LogSinRespuesta(logger);
            await estado.RecargarAsync(db, ct).ConfigureAwait(false);
            return null;
        }

        var soportados = new HashSet<string>(StringComparer.Ordinal);
        var descartados = 0;
        foreach (var r in remotos)
        {
            var codigo = CodigoDepartamento(r);
            if (codigo is null || !soportados.Add(codigo))
                descartados++;
        }

        var ahora = reloj.GetUtcNow();
        var locales = await db.Set<DepartamentoImpuesto>().ToDictionaryAsync(d => d.DepartamentoCodigo, StringComparer.Ordinal, ct).ConfigureAwait(false);
        int nuevos = 0, retirados = 0;
        foreach (var codigo in soportados)
        {
            if (locales.TryGetValue(codigo, out var local))
            {
                local.Soportado = true;
                local.ActualizadoEn = ahora;
                continue;
            }

            db.Add(new DepartamentoImpuesto { DepartamentoCodigo = codigo, Soportado = true, ActualizadoEn = ahora });
            nuevos++;
        }

        foreach (var local in locales.Values.Where(l => l.Soportado && !soportados.Contains(l.DepartamentoCodigo)))
        {
            local.Soportado = false;
            local.ActualizadoEn = ahora;
            retirados++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await estado.RecargarAsync(db, ct).ConfigureAwait(false);
        LogSincronizado(logger, remotos.Count, soportados.Count, nuevos, retirados, descartados);
        return new ResultadoSincronizacionImpuesto(remotos.Count, soportados.Count, nuevos, retirados, descartados);
    }

    /// <summary>Código DIVIPOLA del departamento (un código de 1 cifra perdió el cero inicial); null si no hay o no cabe.</summary>
    private static string? CodigoDepartamento(string? codigo)
    {
        var limpio = codigo?.Trim();
        if (string.IsNullOrEmpty(limpio) || limpio.Length > LargoCodigo)
            return null;
        return limpio.Length == 1 && char.IsAsciiDigit(limpio[0]) ? "0" + limpio : limpio;
    }

    [LoggerMessage(EventId = 138610, Level = LogLevel.Information, Message = "Réplica de departamentos con impuesto sin cambios: la capacidad de core-consultas no está conectada o no respondió OK.")]
    private static partial void LogSinRespuesta(ILogger logger);

    [LoggerMessage(EventId = 138611, Level = LogLevel.Information, Message = "Departamentos con impuesto sincronizados: {Recibidos} recibidos, {Soportados} soportados, {Nuevos} nuevos, {Retirados} retirados, {Descartados} descartados.")]
    private static partial void LogSincronizado(ILogger logger, int recibidos, int soportados, int nuevos, int retirados, int descartados);
}
