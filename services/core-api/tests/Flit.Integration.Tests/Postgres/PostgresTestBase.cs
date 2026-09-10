using Flit.Infrastructure.Persistence;
using Xunit;

namespace Flit.Integration.Tests.Postgres;

/// <summary>
/// HU #12319 — base de toda prueba contra Postgres: pertenece a la colección
/// <see cref="PostgresCollectionDefinition"/> y, ANTES de cada prueba, deja la base efímera limpia
/// (<see cref="PostgresDatabaseFixture.ResetAsync"/>, AC2). Los métodos de prueba se marcan con
/// <see cref="PostgresFactAttribute"/> / <see cref="PostgresTheoryAttribute"/> (AC5).
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public abstract class PostgresTestBase(PostgresDatabaseFixture fixture) : IAsyncLifetime
{
    protected PostgresDatabaseFixture Fixture { get; } = fixture;

    /// <summary>
    /// Contexto real (Npgsql + snake_case) sobre la base efímera. Uno por operación: tras un
    /// <c>SaveChanges</c> rechazado por el motor, EF conserva la entidad fallida en el change tracker.
    /// </summary>
    protected FlitDbContext NewContext() => Fixture.CreateDbContext();

    public virtual async ValueTask InitializeAsync()
    {
        if (!PostgresAvailability.IsAvailable)
        {
            return; // fuera de CI el atributo ya omitió la prueba; en CI el fixture ya falló
        }

        await Fixture.ResetAsync();
    }

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
