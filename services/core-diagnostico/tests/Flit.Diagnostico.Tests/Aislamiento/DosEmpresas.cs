using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// Dos empresas ficticias sobre la misma base efímera de <see cref="ApiDePrueba"/> (HU #13722): cada una con su token y su
/// contexto de datos, que filtra y escribe como lo haría una petición con ese token.
/// </summary>
public sealed class DosEmpresas
{
    private DosEmpresas(ApiDePrueba api)
    {
        A = new EmpresaDePrueba(api, Guid.NewGuid());
        B = new EmpresaDePrueba(api, Guid.NewGuid());
    }

    public EmpresaDePrueba A { get; }

    public EmpresaDePrueba B { get; }

    public static DosEmpresas En(ApiDePrueba api) => new(api);

    /// <summary>Contexto de datos de la base dada con la empresa fija (null = token sin empresa).</summary>
    public static DiagnosticoDb Contexto(string baseDeDatos, Guid? empresa)
    {
        var options = new DbContextOptionsBuilder<DiagnosticoDb>();
        DiagnosticoDb.Configure(options, baseDeDatos);
        return new DiagnosticoDb(options.Options, new EmpresaFija(empresa));
    }
}

/// <summary>Una empresa de prueba: su id, su token de producto y su contexto de datos.</summary>
public sealed class EmpresaDePrueba(ApiDePrueba api, Guid id)
{
    private string? _token;

    public Guid Id { get; } = id;

    public string Token => _token ??= api.Token(empresa: Id);

    public HttpClient Cliente() => api.Cliente(Token);

    public DiagnosticoDb Db() => DosEmpresas.Contexto(api.BaseDeDatos, Id);
}

/// <summary>Empresa fija, como la resolvería el SDK desde el token (sin SuperAdmin ni servicio).</summary>
public sealed class EmpresaFija(Guid? empresa) : IPlatformTenantAccessor
{
    public PlatformTenantContext Current { get; } = new(empresa, IsSuperAdmin: false, IsService: false);
}
