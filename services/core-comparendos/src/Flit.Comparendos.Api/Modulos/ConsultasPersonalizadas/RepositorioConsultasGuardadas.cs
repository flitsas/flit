using System.Text.Json;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Queries.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.ConsultasPersonalizadas;

/// <summary>
/// Consultas guardadas (<c>consulta_guardada</c>). Una consulta es de una empresa y de un usuario; la del SuperAdmin con
/// «todas las empresas» se guarda sin empresa. Las de fábrica (<c>de_fabrica</c>, sin empresa ni usuario) son de todos:
/// se listan primero, guardarlas las duplica y borrarlas da 409.
/// </summary>
public sealed class RepositorioConsultasGuardadas(ComparendosDb db, Reloj reloj)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SavedQueryDto>> ListarAsync(AlcanceConsulta alcance, Guid usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        var filas = await Propias(alcance, usuario).Concat(db.ConsultasGuardadas.Where(q => q.DeFabrica))
            .AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var orden = ConsultasFabrica.Todas.Select((q, i) => (q.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return
        [
            .. filas.Where(q => q.DeFabrica).OrderBy(q => orden.GetValueOrDefault(q.Id, int.MaxValue)).Select(q => Dto(q, alcance)),
            .. filas.Where(q => !q.DeFabrica).OrderBy(q => q.Nombre, StringComparer.OrdinalIgnoreCase).Select(q => Dto(q, alcance)),
        ];
    }

    /// <summary>Una de fábrica o una propia; null si no existe o es de otro.</summary>
    public async Task<SavedQueryDto?> ObtenerAsync(AlcanceConsulta alcance, Guid usuario, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        var q = await db.ConsultasGuardadas.AsNoTracking()
            .Where(x => x.Id == id && (x.DeFabrica || (x.TenantId == alcance.Tenant && x.UsuarioId == usuario)))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return q is null ? null : Dto(q, alcance);
    }

    public async Task<SavedQueryDto> GuardarAsync(AlcanceConsulta alcance, Guid usuario, Guid? id, SavedQueryInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        ArgumentNullException.ThrowIfNull(input);
        ConsultaGuardada? entidad = null;
        // Con el id de una de fábrica (o de otro) no se edita nada: se crea una propia.
        if (id is { } existente)
        {
            entidad = await Propias(alcance, usuario).FirstOrDefaultAsync(q => q.Id == existente, ct).ConfigureAwait(false);
        }

        if (entidad is null)
        {
            if (await Propias(alcance, usuario).CountAsync(ct).ConfigureAwait(false) >= QueryLimits.MaxConsultasGuardadas)
                throw new ReglaException("LIMITE_CONSULTAS", new SavedQueryLimitException(QueryLimits.MaxConsultasGuardadas).Message, StatusCodes.Status409Conflict);
            entidad = new ConsultaGuardada { Id = Guid.CreateVersion7(), TenantId = alcance.Tenant, UsuarioId = usuario, CreadoEn = reloj.Ahora };
            db.ConsultasGuardadas.Add(entidad);
        }
        else
        {
            entidad.ActualizadoEn = reloj.Ahora;
        }

        var nombre = input.Nombre;
        // Son 50 como mucho: se comparan en memoria, sin depender de la intercalación de la base.
        var otras = await Propias(alcance, usuario).Where(q => q.Id != entidad.Id).Select(q => q.Nombre).ToListAsync(ct).ConfigureAwait(false);
        if (otras.Contains(nombre, StringComparer.OrdinalIgnoreCase))
            throw new ReglaException("NOMBRE_REPETIDO", new SavedQueryNameTakenException(nombre).Message, StatusCodes.Status409Conflict);

        entidad.Nombre = nombre;
        entidad.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion) ? null : input.Descripcion.Trim();
        entidad.Definicion = JsonSerializer.Serialize(input.Definition, Json);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Dto(entidad, alcance);
    }

    /// <summary>Borra una propia. 409 <c>CONSULTA_DE_FABRICA</c> si es de fábrica; 404 si no existe o es de otro.</summary>
    public async Task BorrarAsync(AlcanceConsulta alcance, Guid usuario, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        if (await db.ConsultasGuardadas.AnyAsync(q => q.Id == id && q.DeFabrica, ct).ConfigureAwait(false))
            throw new ReglaException("CONSULTA_DE_FABRICA", "Las consultas de fábrica no se borran. Puedes guardar una copia y editarla.", StatusCodes.Status409Conflict);
        var entidad = await Propias(alcance, usuario).FirstOrDefaultAsync(q => q.Id == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La consulta");
        db.ConsultasGuardadas.Remove(entidad);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private IQueryable<ConsultaGuardada> Propias(AlcanceConsulta alcance, Guid usuario) =>
        db.ConsultasGuardadas.Where(q => !q.DeFabrica && q.TenantId == alcance.Tenant && q.UsuarioId == usuario);

    private static SavedQueryDto Dto(ConsultaGuardada q, AlcanceConsulta alcance)
    {
        QueryDefinition? definicion;
        try
        {
            definicion = JsonSerializer.Deserialize<QueryDefinition>(q.Definicion, Json);
        }
        catch (JsonException)
        {
            // Una definición que ya no encaja se abre vacía en vez de tumbar la lista entera.
            definicion = null;
        }

        return new SavedQueryDto(q.Id, q.Nombre, q.Descripcion, q.DeFabrica, QueryNormalizer.Normalize(alcance.Catalogo, definicion), q.CreadoEn, q.ActualizadoEn);
    }
}
