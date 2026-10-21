using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13722 (F00.1-H2), escenario 6 — en CI las pruebas de integración no se omiten: sin <c>ConnectionStrings__Core</c>
/// o con un Postgres inalcanzable, el fixture falla. En local, sin Postgres, se omite. El entorno se inyecta para no tocar
/// las variables del proceso.
/// </summary>
public sealed class ApiDePruebaTests
{
    [Fact]
    public async Task Sin_postgres_en_CI_falla_no_omite()
    {
        var arrancar = () => ApiDePrueba.ConBaseAsync(entorno: n => n == "CI" ? "true" : null);

        await arrancar.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ConnectionStrings__Core*");
    }

    [Fact]
    public async Task Con_postgres_inalcanzable_en_CI_falla_no_omite()
    {
        var arrancar = () => ApiDePrueba.ConBaseAsync(entorno: n => n switch
        {
            "CI" => "true",
            "ConnectionStrings__Core" => ApiDePrueba.BaseInalcanzable,
            _ => null,
        });

        await arrancar.Should().ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public async Task Sin_postgres_en_local_se_omite()
    {
        await using var api = await ApiDePrueba.ConBaseAsync(entorno: n => n == "ConnectionStrings__Core" ? ApiDePrueba.BaseInalcanzable : null);

        api.Omitir.Should().StartWith("Postgres no alcanzable");
    }
}
