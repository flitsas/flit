using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
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

    /// <summary>
    /// Token de un usuario del producto con los permisos de lectura de Diagnóstico (sin ellos no hay sesión, HU #13726); cada
    /// GET exige el suyo (<c>diagnosticos.ver</c>, <c>configuracion.ver</c> desde la HU #13741, <c>diagnosticos.crear</c>
    /// para <c>/tipos-tramite</c> desde la HU #13742, <c>reportes.descargar</c> para el PDF del vehículo desde la HU #13848).
    /// </summary>
    public string Token => _token ??= api.Token(empresa: Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionVer, Permisos.DiagnosticosCrear, Permisos.ReportesDescargar]);

    public HttpClient Cliente() => api.Cliente(Token);

    public DiagnosticoDb Db() => DosEmpresas.Contexto(api.BaseDeDatos, Id);
}

/// <summary>Empresa fija, como la resolvería el SDK desde el token (sin SuperAdmin ni servicio).</summary>
public sealed class EmpresaFija(Guid? empresa) : IPlatformTenantAccessor
{
    public PlatformTenantContext Current { get; } = new(empresa, IsSuperAdmin: false, IsService: false);
}
