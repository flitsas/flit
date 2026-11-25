using Flit.Impuestos.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Catalogos;

/// <summary>Un organismo de tránsito tal como lo tiene la plataforma (<c>catalogs.transit_offices</c> de core-api).</summary>
public sealed record OrganismoPlataforma(string Codigo, string Nombre, string? DepartamentoCodigo, string? Departamento, string? Municipio);

/// <summary>El catálogo de organismos de la plataforma, o null si no hay con quién hablar.</summary>
public interface ICatalogoOrganismosPlataforma
{
    Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct);
}

/// <summary>Sin <see cref="CatalogosImpuestosExtensions.ClaveDireccion"/>: no hay sincronización y la réplica queda como está.</summary>
public sealed class CatalogoOrganismosNoConectado : ICatalogoOrganismosPlataforma
{
    public Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganismoPlataforma>?>(null);
}

/// <summary>Resultado de una sincronización: total recibido, nuevos, actualizados, retirados y omitidos (sin código, largos o repetidos).</summary>
public sealed record ResultadoSincronizacionOrganismos(int Total, int Nuevos, int Actualizados, int Retirados, int Omitidos);

/// <summary>
/// Réplica diaria de los organismos de tránsito en <c>impuestos.organismos</c> (plan §4.1, F2a-CF3): la plataforma es la dueña del
/// catálogo (<c>flit.tramites.v1.CatalogosService.ListarOrganismosTransito</c>) e Impuestos guarda su copia global, sin empresa,
/// para <see cref="HomologacionSecretaria"/>. Por código (recortado): crea los nuevos, actualiza nombre, nombre normalizado,
/// departamento y municipio, y borra los que la plataforma ya no trae. Un código vacío, de más de 16 caracteres o repetido se
/// omite. Un departamento de una cifra perdió el cero inicial y se completa; uno que no queda en 2 cifras se guarda null (el
/// organismo no homologa). Una lista vacía no se aplica (sería borrar el catálogo). Sin adaptador conectado devuelve null.
/// </summary>
/// <example>
/// <code>
/// var resultado = await scope.ServiceProvider.GetRequiredService&lt;SincronizacionOrganismos&gt;().SincronizarAsync(ct);
/// </code>
/// </example>
public sealed partial class SincronizacionOrganismos(
    ImpuestosDb db, ICatalogoOrganismosPlataforma plataforma, TimeProvider reloj, ILogger<SincronizacionOrganismos> log)
{
    private const int LargoCodigo = 16;
    private const int LargoNombre = 200;
    private const int LargoTexto = 80;

    public async Task<ResultadoSincronizacionOrganismos?> SincronizarAsync(CancellationToken ct)
    {
        var remotos = await plataforma.ListarAsync(ct).ConfigureAwait(false);
        if (remotos is null)
        {
            NoConectado(log);
            return null;
        }

        if (remotos.Count == 0)
        {
            ListaVacia(log);
            return null;
        }

        var ahora = reloj.GetUtcNow();
        var locales = await db.Set<Organismo>().ToDictionaryAsync(o => o.Codigo, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        int nuevos = 0, actualizados = 0, omitidos = 0;
        foreach (var r in remotos)
        {
            var codigo = r.Codigo?.Trim() ?? string.Empty;
            if (codigo.Length == 0 || codigo.Length > LargoCodigo || !vistos.Add(codigo))
            {
                omitidos++;
                continue;
            }

            var nombre = Recortar(r.Nombre?.Trim() ?? string.Empty, LargoNombre);
            var normalizado = Recortar(HomologacionSecretaria.Normalizar(nombre), LargoNombre);
            var departamentoCodigo = CodigoDepartamento(r.DepartamentoCodigo);
            var departamento = Texto(r.Departamento);
            var municipio = Texto(r.Municipio);
            if (!locales.TryGetValue(codigo, out var o))
            {
                db.Add(new Organismo
                {
                    Codigo = codigo,
                    Nombre = nombre,
                    NombreNormalizado = normalizado,
                    DepartamentoCodigo = departamentoCodigo,
                    Departamento = departamento,
                    Municipio = municipio,
                    SincronizadoEn = ahora,
                });
                nuevos++;
                continue;
            }

            if (o.Nombre != nombre || o.NombreNormalizado != normalizado || o.DepartamentoCodigo != departamentoCodigo
                || o.Departamento != departamento || o.Municipio != municipio)
                actualizados++;
            o.Nombre = nombre;
            o.NombreNormalizado = normalizado;
            o.DepartamentoCodigo = departamentoCodigo;
            o.Departamento = departamento;
            o.Municipio = municipio;
            o.SincronizadoEn = ahora;
        }

        var retirados = locales.Values.Where(l => !vistos.Contains(l.Codigo)).ToList();
        db.RemoveRange(retirados);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Sincronizado(log, remotos.Count, nuevos, actualizados, retirados.Count, omitidos);
        return new ResultadoSincronizacionOrganismos(remotos.Count, nuevos, actualizados, retirados.Count, omitidos);
    }

    /// <summary>Código DANE del departamento en 2 cifras (una cifra se completa con 0); null si no hay o no queda en 2 cifras.</summary>
    internal static string? CodigoDepartamento(string? codigo)
    {
        var limpio = codigo?.Trim();
        if (string.IsNullOrEmpty(limpio) || !limpio.All(char.IsAsciiDigit))
            return null;
        if (limpio.Length == 1)
            limpio = "0" + limpio;
        return limpio.Length == 2 ? limpio : null;
    }

    private static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];

    private static string? Texto(string? texto)
    {
        var limpio = texto?.Trim();
        return string.IsNullOrEmpty(limpio) ? null : Recortar(limpio, LargoTexto);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sincronización de organismos omitida: no hay dirección del gRPC de catálogos (Impuestos:Tramites:Address).")]
    private static partial void NoConectado(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La plataforma devolvió el catálogo de organismos vacío: no se aplica.")]
    private static partial void ListaVacia(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organismos sincronizados: {Total} en la plataforma, {Nuevos} nuevos, {Actualizados} actualizados, {Retirados} retirados, {Omitidos} omitidos.")]
    private static partial void Sincronizado(ILogger logger, int total, int nuevos, int actualizados, int retirados, int omitidos);
}
