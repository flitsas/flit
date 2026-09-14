using Xunit;

namespace Flit.Integration.Tests.Postgres;

/// <summary>
/// HU #12319 — todas las pruebas contra Postgres comparten UNA base efímera (una migración
/// completa por ejecución, no una por clase) y corren en serie: el reset entre pruebas (AC2) es
/// global a la base, así que dos clases en paralelo se pisarían.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresDatabaseFixture>
{
    public const string Name = "Postgres";
}
