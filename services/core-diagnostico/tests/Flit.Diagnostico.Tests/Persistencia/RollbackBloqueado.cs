using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Tests.Persistencia;

/// <summary>
/// Estado del historial de migraciones tras un Down que la guarda de <c>protegida</c> bloqueó. EF baja una por una, cada una en su
/// transacción, desde la última: las posteriores a la protegida sin guarda (o con la guarda satisfecha) quedan pendientes, y la
/// protegida y todas las anteriores siguen aplicadas. No depende de cuántas migraciones se agreguen después.
/// </summary>
/// <example>
/// <code>
/// await RollbackBloqueado.DebeDetenerseEnAsync(db, "20261113050619_R7_AjusteDictamen", ct);
/// </code>
/// </example>
internal static class RollbackBloqueado
{
    public static async Task DebeDetenerseEnAsync(DbContext db, string protegida, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var todas = db.Database.GetMigrations().ToList();
        var indice = todas.IndexOf(protegida);
        indice.Should().BeGreaterThanOrEqualTo(0, $"{protegida} debe ser una migración del ensamblado");

        (await db.Database.GetAppliedMigrationsAsync(ct)).Should().Equal(
            todas.Take(indice + 1), "la migración protegida y todas las anteriores siguen aplicadas");
        (await db.Database.GetPendingMigrationsAsync(ct)).Should().Equal(
            todas.Skip(indice + 1), "solo puede quedar pendiente lo posterior a la migración protegida");
    }
}
