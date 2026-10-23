using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Flit.Infrastructure.Persistence.Configurations.Identity;

/// <summary>
/// HU #12990 (FLIT Suite A-05) — entidades de OpenIddict en <c>identity.oidc_*</c>. El esquema lo crea el DDL
/// <c>124-HU12990-oidc-stores.sql</c>, por eso quedan fuera de las migraciones generadas. Columnas en snake_case
/// por la convención del contexto, iguales a las que genera OpenIddict.
/// </summary>
internal static class OidcModel
{
    public const string Schema = "identity";

    public static void Map(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict<Guid>();

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>()
            .ToTable("oidc_applications", Schema, t => t.ExcludeFromMigrations());
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
            .ToTable("oidc_authorizations", Schema, t => t.ExcludeFromMigrations());
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>()
            .ToTable("oidc_scopes", Schema, t => t.ExcludeFromMigrations());
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>()
            .ToTable("oidc_tokens", Schema, t => t.ExcludeFromMigrations());
    }
}
