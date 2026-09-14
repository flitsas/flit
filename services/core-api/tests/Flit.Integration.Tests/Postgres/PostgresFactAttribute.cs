using Xunit;

namespace Flit.Integration.Tests.Postgres;

/// <summary>
/// HU #12319 (AC5) — <c>[Fact]</c> que se omite dinámicamente con el mensaje
/// <see cref="PostgresAvailability.SkipReason"/> cuando no hay PostgreSQL alcanzable y NO se está en
/// CI. En CI (<c>GITHUB_ACTIONS</c>/<c>CI</c>) nunca se omite: el fixture falla con el detalle.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        Skip = PostgresAvailability.SkipReason;
        SkipType = typeof(PostgresAvailability);
        SkipUnless = nameof(PostgresAvailability.ShouldRun);
    }
}

/// <summary>Variante <c>[Theory]</c> de <see cref="PostgresFactAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        Skip = PostgresAvailability.SkipReason;
        SkipType = typeof(PostgresAvailability);
        SkipUnless = nameof(PostgresAvailability.ShouldRun);
    }
}
