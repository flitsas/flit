using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>
/// Piezas de V2 que deben existir ANTES de migrar nada: el tipo de trámite, las entidades
/// de actor y el usuario al que se le atribuye la carga. Resolverlas una sola vez al arranque
/// evita repetir consultas y, sobre todo, hace que el proceso falle temprano y con un mensaje
/// claro si el entorno destino no está listo.
/// </summary>
public sealed class TargetEnvironment
{
    public required Guid ProcedureTypeId { get; init; }
    public required Guid OwnerEntityId { get; init; }
    public required Guid BuyerEntityId { get; init; }
    public required Guid SystemUserId { get; init; }

    /// <summary>
    /// Prerrequisitos que hubo que crear en V2 (usuario de sistema, tenants…). Se crean FUERA
    /// de la transacción de cada trámite, así que sobreviven a un <c>--dry-run</c>: por eso se
    /// reportan explícitamente en vez de dejarlos como efecto invisible.
    /// </summary>
    public List<string> Provisioned { get; } = [];

    public static async Task<TargetEnvironment> ResolveAsync(
        FlitDbContext db,
        string procedureTypeCode,
        string systemUserEmail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Estas tablas solo se LEEN para obtener un id, así que se consultan por SQL y no por
        // el modelo EF. Es deliberado: el modelo evoluciona con la app (p. ej. procedure_types
        // ganó 'gate_profile') y no queremos que el migrador se caiga por una columna nueva
        // en una tabla que ni siquiera escribe.
        var procedureTypeId = await ScalarIdAsync(
            db,
            "SELECT id AS \"Value\" FROM tramites.procedure_types WHERE code = {0}",
            procedureTypeCode,
            $"V2 no tiene el tipo de trámite '{procedureTypeCode}'. No se puede migrar.",
            cancellationToken);

        var ownerId = await ScalarIdAsync(
            db,
            "SELECT id AS \"Value\" FROM tramites.procedure_entities WHERE code = {0}",
            "OWNER",
            "V2 no tiene la entidad de actor OWNER en tramites.procedure_entities.",
            cancellationToken);

        var buyerId = await ScalarIdAsync(
            db,
            "SELECT id AS \"Value\" FROM tramites.procedure_entities WHERE code = {0}",
            "BUYER",
            "V2 no tiene la entidad de actor BUYER en tramites.procedure_entities.",
            cancellationToken);

        var existingUser = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == systemUserEmail, cancellationToken);

        var environment = new TargetEnvironment
        {
            ProcedureTypeId = procedureTypeId,
            OwnerEntityId = ownerId,
            BuyerEntityId = buyerId,
            SystemUserId = existingUser?.Id
                ?? await CreateSystemUserAsync(db, systemUserEmail, cancellationToken),
        };

        if (existingUser is null)
        {
            environment.Provisioned.Add($"usuario de sistema '{systemUserEmail}' en identity.users");
        }

        return environment;
    }

    private static async Task<Guid> ScalarIdAsync(
        FlitDbContext db,
        string sql,
        string parameter,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        var found = await db.Database.SqlQueryRaw<Guid>(sql, parameter).ToListAsync(cancellationToken);
        return found.Count > 0 ? found[0] : throw new InvalidOperationException(notFoundMessage);
    }

    /// <summary>
    /// Garantiza que exista el usuario de migración. <c>created_by_user_id</c> es NOT NULL con
    /// FK a <c>identity.users</c>, así que sin un usuario válido no se puede insertar ni un
    /// trámite. Usar uno dedicado (y no el de una persona real) deja claro en la auditoría
    /// que esos registros los creó el proceso, no un operador.
    /// </summary>
    private static async Task<Guid> CreateSystemUserAsync(
        FlitDbContext db,
        string email,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            DisplayName = "Migración V1",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        return user.Id;
    }
}
