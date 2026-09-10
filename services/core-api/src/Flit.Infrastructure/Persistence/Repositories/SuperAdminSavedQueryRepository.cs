using System.Text.Json;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Queries.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consultas guardadas de SuperAdmin: sin tenant ni usuario de alcance, compartidas entre todo el
/// equipo — ver <see cref="ISuperAdminSavedQueryRepository"/> y el DDL en
/// <c>72-superadmin-consultas-guardadas.sql</c> para el porqué de la tabla propia.
/// </summary>
internal sealed class SuperAdminSavedQueryRepository : ISuperAdminSavedQueryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FlitDbContext _context;

    public SuperAdminSavedQueryRepository(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<SavedQueryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var todas = await _context.SuperAdminSavedQueries
            .AsNoTracking()
            .OrderBy(q => q.Nombre)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Las de fábrica de la empresa gestora sirven igual de punto de partida aquí: son preguntas
        // válidas sobre trámites, sin más — solo que ahora corren sobre todas las compañías a la vez.
        return
        [
            .. todas.Select(ToSavedDto),
            .. CompanyFactoryQueries.Queries,
        ];
    }

    public async Task<SavedQueryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (CompanyFactoryQueries.IsFactory(id))
            return CompanyFactoryQueries.Queries.FirstOrDefault(q => q.Id == id);

        var entity = await _context.SuperAdminSavedQueries
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : ToSavedDto(entity);
    }

    public async Task<SavedQueryDto> SaveAsync(
        Guid userId,
        Guid? id,
        SavedQueryInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var definition = CompanyQueryFieldCatalog.Normalize(input.Definition);
        var nombre = input.Nombre.Trim();
        var json = JsonSerializer.Serialize(definition, JsonOptions);

        SuperAdminSavedQueryEntity? entity = null;

        // Guardar sobre una de fábrica es duplicarla, no editarla: las de fábrica no viven en la
        // base y tienen que seguir estando ahí para el siguiente que abra la consola.
        if (id is Guid existingId && !CompanyFactoryQueries.IsFactory(existingId))
        {
            entity = await _context.SuperAdminSavedQueries
                .FirstOrDefaultAsync(q => q.Id == existingId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (entity is null)
        {
            var cuantas = await _context.SuperAdminSavedQueries
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);

            // El tope es del EQUIPO, no de una persona: es el mismo límite de cordura que en las
            // demás consultas guardadas, aplicado a una lista que ahora comparten varios SuperAdmin.
            if (cuantas >= QueryLimits.MaxConsultasGuardadas)
            {
                throw new SavedQueryLimitException(QueryLimits.MaxConsultasGuardadas);
            }

            entity = new SuperAdminSavedQueryEntity
            {
                Id = Guid.CreateVersion7(),
                CreatedByUserId = userId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            _context.SuperAdminSavedQueries.Add(entity);
        }
        else
        {
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // El nombre único lo garantiza un índice, pero llegar hasta él devolvería un error de base
        // de datos por algo que es una decisión de producto: dos consultas iguales de nombre harían
        // la lista inservible para cualquiera del equipo.
        var repetido = await _context.SuperAdminSavedQueries
            .AnyAsync(
                q => q.Id != entity.Id && q.Nombre.ToLower() == nombre.ToLower(),
                cancellationToken)
            .ConfigureAwait(false);

        if (repetido)
        {
            throw new SavedQueryNameTakenException(nombre);
        }

        entity.Nombre = nombre;
        entity.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion)
            ? null
            : input.Descripcion.Trim();
        entity.Definicion = json;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSavedDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.SuperAdminSavedQueries
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        _context.SuperAdminSavedQueries.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static SavedQueryDto ToSavedDto(SuperAdminSavedQueryEntity entity)
    {
        QueryDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<QueryDefinition>(entity.Definicion, JsonOptions);
        }
        catch (JsonException)
        {
            // Una consulta guardada con un JSON que ya no encaja se abre vacía en vez de tumbar la
            // lista entera: se puede volver a armar, pero solo si llega a verse.
            definition = null;
        }

        return new SavedQueryDto(
            entity.Id,
            entity.Nombre,
            entity.Descripcion,
            DeFabrica: false,
            CompanyQueryFieldCatalog.Normalize(definition),
            entity.CreatedAt,
            entity.UpdatedAt);
    }
}
