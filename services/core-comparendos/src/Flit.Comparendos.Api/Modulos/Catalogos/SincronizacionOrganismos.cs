using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Catalogos;

/// <summary>Un organismo de tránsito tal como lo tiene la plataforma (<c>catalogs.transit_offices</c>).</summary>
public sealed record OrganismoPlataforma(Guid Id, string Codigo, string Nombre, string? MunicipioCodigo, string? Municipio, string? Departamento, bool Activo);

/// <summary>El catálogo de organismos de la plataforma, o null si no hay con quién hablar.</summary>
public interface ICatalogoOrganismosPlataforma
{
    Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct);
}

/// <summary>Por el gRPC de catálogos de core-api (<c>flit.tramites.v1.CatalogosService</c>, ADR-0070).</summary>
public sealed class CatalogoOrganismosGrpc(Flit.Tramites.Grpc.V1.CatalogosService.CatalogosServiceClient cliente) : ICatalogoOrganismosPlataforma
{
    public async Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct)
    {
        var r = await cliente.ListarOrganismosTransitoAsync(new Flit.Tramites.Grpc.V1.ListarOrganismosTransitoRequest(), cancellationToken: ct).ConfigureAwait(false);
        static string? Texto(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return [.. r.Organismos.Where(o => Guid.TryParse(o.Id, out _) && o.Codigo.Length > 0)
            .Select(o => new OrganismoPlataforma(Guid.Parse(o.Id), o.Codigo.Trim(), o.Nombre.Trim(), Texto(o.MunicipioCodigo), Texto(o.Municipio), Texto(o.Departamento), o.Activo))];
    }
}

/// <summary>Sin <c>Comparendos:Tramites:Address</c>: no hay sincronización y queda la semilla.</summary>
public sealed class CatalogoOrganismosNoConectado : ICatalogoOrganismosPlataforma
{
    public Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganismoPlataforma>?>(null);
}

/// <summary>
/// Réplica diaria del catálogo de organismos de tránsito (S-66, HU #13459): la plataforma es la dueña del catálogo y
/// Comparendos guarda su copia (ADR-0064). Por código: crea los nuevos con el id de la plataforma, y en los existentes
/// actualiza nombre, municipio, departamento y si está activo. Lo que es solo de Comparendos (correos de radicación,
/// permiso de cambio de contraventor y alias) no se toca. Un organismo que la plataforma ya no trae queda inactivo; uno
/// que vuelve a aparecer, activo otra vez según la plataforma. Una lista vacía no se aplica: sería borrar el catálogo.
/// </summary>
public sealed partial class SincronizacionOrganismos(ComparendosDb db, ICatalogoOrganismosPlataforma plataforma, Reloj reloj, ILogger<SincronizacionOrganismos> logger) : IEjecutorTrabajo
{
    public bool Atiende(TipoTrabajo tipo) => tipo == TipoTrabajo.SincronizacionOrganismos;

    public async Task EjecutarAsync(SolicitudTrabajo solicitud, CancellationToken ct) => await SincronizarAsync(ct).ConfigureAwait(false);

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

        var ahora = reloj.Ahora;
        var locales = await db.OrganismosTransito.ToListAsync(ct).ConfigureAwait(false);
        var porCodigo = locales.ToDictionary(o => Normalizar(o.Codigo), StringComparer.Ordinal);
        int nuevos = 0, actualizados = 0;
        var vistos = new HashSet<Guid>();
        foreach (var r in remotos)
        {
            if (porCodigo.TryGetValue(Normalizar(r.Codigo), out var o))
            {
                vistos.Add(o.Id);
                var cambio = o.Nombre != r.Nombre || o.Municipio != r.Municipio || o.Departamento != r.Departamento
                    || o.CodigoDivipola != r.MunicipioCodigo || o.ActivoPlataforma != r.Activo;
                o.Nombre = r.Nombre;
                o.Municipio = r.Municipio;
                o.Departamento = r.Departamento;
                o.CodigoDivipola = r.MunicipioCodigo;
                o.ActivoPlataforma = r.Activo;
                o.SincronizadoEn = ahora;
                if (cambio)
                    actualizados++;
                continue;
            }

            if (locales.Any(l => l.Id == r.Id))
                continue; // mismo id con otro código: no se pisa a ciegas.
            db.OrganismosTransito.Add(new OrganismoTransito
            {
                Id = r.Id, Codigo = r.Codigo, Nombre = r.Nombre, CodigoDivipola = r.MunicipioCodigo, Municipio = r.Municipio,
                Departamento = r.Departamento, ActivoPlataforma = r.Activo, SincronizadoEn = ahora,
            });
            nuevos++;
        }

        var retirados = 0;
        foreach (var o in locales.Where(l => !vistos.Contains(l.Id) && l.ActivoPlataforma))
        {
            o.ActivoPlataforma = false;
            o.SincronizadoEn = ahora;
            retirados++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogSincronizado(logger, remotos.Count, nuevos, actualizados, retirados);
        return new ResultadoSincronizacion(remotos.Count, nuevos, actualizados, retirados);
    }

    /// <summary>Los códigos de 7 cifras perdieron el cero inicial (Medellín 5001000 = 05001000).</summary>
    private static string Normalizar(string codigo) =>
        codigo.Length is > 0 and < 8 && codigo.All(char.IsAsciiDigit) ? codigo.PadLeft(8, '0') : codigo;

    [LoggerMessage(Level = LogLevel.Information, Message = "Sincronización de organismos omitida: no hay dirección del gRPC de catálogos (Comparendos:Tramites:Address).")]
    private static partial void LogNoConectado(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La plataforma devolvió el catálogo de organismos vacío: no se aplica.")]
    private static partial void LogListaVacia(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organismos sincronizados: {Total} en la plataforma, {Nuevos} nuevos, {Actualizados} actualizados, {Retirados} retirados.")]
    private static partial void LogSincronizado(ILogger logger, int total, int nuevos, int actualizados, int retirados);
}

public sealed record ResultadoSincronizacion(int Total, int Nuevos, int Actualizados, int Retirados);
