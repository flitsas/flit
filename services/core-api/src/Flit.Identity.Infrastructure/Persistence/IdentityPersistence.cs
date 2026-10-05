using System.Reflection;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Ensamblado de las entidades y configuraciones de identidad (HU #13231). Lo aplican los dos contextos:
/// <c>FlitDbContext</c> (core-api, dueño de las migraciones) y el contexto de identidad (core-identity, sin migraciones).
/// </summary>
public static class IdentityPersistence
{
    public static Assembly Assembly => typeof(IdentityPersistence).Assembly;
}
