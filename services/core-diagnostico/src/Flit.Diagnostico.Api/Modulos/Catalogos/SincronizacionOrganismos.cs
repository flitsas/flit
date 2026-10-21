using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Catalogos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>Un organismo de tránsito tal como lo tiene la plataforma (<c>catalogs.transit_offices</c> de core-api).</summary>
public sealed record OrganismoPlataforma(string Codigo, string Nombre, string? DepartamentoCodigo, string? Departamento);

/// <summary>El catálogo de organismos de la plataforma, o null si no hay con quién hablar.</summary>
public interface ICatalogoOrganismosPlataforma
{
    Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct);
}

/// <summary>Sin <c>Diagnostico:Tramites:Address</c>: no hay sincronización y la réplica queda como está.</summary>
public sealed class CatalogoOrganismosNoConectado : ICatalogoOrganismosPlataforma
{
    public Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganismoPlataforma>?>(null);
}

/// <summary>Resultado de una sincronización: total recibido, nuevos, actualizados, retirados y omitidos sin departamento.</summary>
public sealed record ResultadoSincronizacion(int Total, int Nuevos, int Actualizados, int Retirados, int SinDepartamento);

/// <summary>
/// Réplica de organismos de tránsito (<c>diagnostico.organismos</c>, HU #13733, patrón Comparendos HU #13459): la plataforma
/// es la dueña del catálogo y Diagnóstico guarda su copia global (sin empresa, excepción de ADR-0072) para derivar el
/// departamento del organismo que reporta el RUNT (<see cref="DerivacionDepartamento"/>). Por código: crea los nuevos,
/// actualiza nombre, nombre normalizado y departamento, y borra los que la plataforma ya no trae (la réplica no tiene datos
/// propios). Un organismo sin código de departamento no entra (la columna es obligatoria y sin departamento no sirve para
/// derivar). Una lista vacía no se aplica: sería borrar el catálogo. Sin adaptador conectado no hace nada y devuelve null.
/// </summary>
/// <example>
/// <code>
/// var resultado = await scope.ServiceProvider.GetRequiredService&lt;SincronizacionOrganismos&gt;().SincronizarAsync(ct);
/// </code>
/// </example>
public sealed partial class SincronizacionOrganismos(DiagnosticoDb db, ICatalogoOrganismosPlataforma plataforma, ILogger<SincronizacionOrganismos> logger)
{
    /// <summary>Largo de <c>organismos.codigo</c>; un código más largo no entra a la réplica.</summary>
    private const int LargoCodigo = 16;

    public async Task<ResultadoSincronizacion?> SincronizarAsync(CancellationToken ct)
    {
        var remotos = await plataforma.ListarAsync(ct).ConfigureAwait(false);
        if (remotos is null)
        {
            LogNoConectado(logger);
            return null;
        }

        if (remotos.Count == 0)
        {
            LogListaVacia(logger);
            return null;
        }

        var locales = await db.Set<Organismo>().ToDictionaryAsync(o => o.Codigo, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        int nuevos = 0, actualizados = 0, sinDepartamento = 0;
        foreach (var r in remotos)
        {
            var departamento = CodigoDepartamento(r.DepartamentoCodigo);
            if (departamento is null || r.Codigo.Length > LargoCodigo || !vistos.Add(r.Codigo))
            {
                sinDepartamento += departamento is null ? 1 : 0;
                continue;
            }

            var nombre = Recortar(r.Nombre, 200);
            var nombreNorm = Recortar(NombreNormalizado.Normalizar(r.Nombre), 200);
            var departamentoNombre = Recortar(r.Departamento ?? string.Empty, 100);
            if (locales.TryGetValue(r.Codigo, out var o))
            {
                if (o.Nombre != nombre || o.NombreNorm != nombreNorm || o.DepartamentoCodigo != departamento || o.DepartamentoNombre != departamentoNombre)
                    actualizados++;
                o.Nombre = nombre;
                o.NombreNorm = nombreNorm;
                o.DepartamentoCodigo = departamento;
                o.DepartamentoNombre = departamentoNombre;
                continue;
            }

            db.Add(new Organismo { Codigo = r.Codigo, Nombre = nombre, NombreNorm = nombreNorm, DepartamentoCodigo = departamento, DepartamentoNombre = departamentoNombre });
            nuevos++;
        }

        var retirados = locales.Values.Where(l => !vistos.Contains(l.Codigo)).ToList();
        db.RemoveRange(retirados);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogSincronizado(logger, remotos.Count, nuevos, actualizados, retirados.Count, sinDepartamento);
        return new ResultadoSincronizacion(remotos.Count, nuevos, actualizados, retirados.Count, sinDepartamento);
    }

    /// <summary>Código DIVIPOLA del departamento (2 cifras; un código de 1 cifra perdió el cero inicial); null si no hay o no cabe.</summary>
    private static string? CodigoDepartamento(string? codigo)
    {
        var limpio = codigo?.Trim();
        if (string.IsNullOrEmpty(limpio) || limpio.Length > 8)
            return null;
        return limpio.Length == 1 && char.IsAsciiDigit(limpio[0]) ? "0" + limpio : limpio;
    }

    private static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];

    [LoggerMessage(EventId = 137330, Level = LogLevel.Information, Message = "Sincronización de organismos omitida: no hay dirección del gRPC de catálogos (Diagnostico:Tramites:Address).")]
    private static partial void LogNoConectado(ILogger logger);

    [LoggerMessage(EventId = 137331, Level = LogLevel.Warning, Message = "La plataforma devolvió el catálogo de organismos vacío: no se aplica.")]
    private static partial void LogListaVacia(ILogger logger);

    [LoggerMessage(EventId = 137332, Level = LogLevel.Information, Message = "Organismos sincronizados: {Total} en la plataforma, {Nuevos} nuevos, {Actualizados} actualizados, {Retirados} retirados, {SinDepartamento} sin departamento.")]
    private static partial void LogSincronizado(ILogger logger, int total, int nuevos, int actualizados, int retirados, int sinDepartamento);
}
