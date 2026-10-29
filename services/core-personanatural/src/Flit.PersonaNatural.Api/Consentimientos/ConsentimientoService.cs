using Flit.PersonaNatural.Api.Persistence;

namespace Flit.PersonaNatural.Api.Consentimientos;

/// <summary>
/// Versiones vigentes del texto de consentimiento de tratamiento de datos (Ley 1581). Se leen de <c>Consentimiento:Versiones</c>
/// (lista indexada o CSV; de la más antigua a la más reciente) y por defecto es solo <c>v1</c>. El servidor decide qué versión
/// vale: el cliente no puede inventar una. Una versión en blanco toma la más reciente.
/// </summary>
public sealed class ConsentimientoVersiones
{
    public const string SectionName = "Consentimiento:Versiones";
    public const string CodigoVersionDesconocida = "consentimiento_version_desconocida";

    private readonly string[] _vigentes;

    public ConsentimientoVersiones(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Se lee a mano: el binder de arreglos suma a los valores por defecto en vez de reemplazarlos.
        var seccion = configuration.GetSection(SectionName);
        var hijos = seccion.GetChildren().Select(h => h.Value).ToList();
        IEnumerable<string?> valores = hijos.Count > 0 ? hijos : [seccion.Value];
        _vigentes = [.. valores.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(v => v.Length is > 0 and <= 40).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (_vigentes.Length == 0)
            _vigentes = ["v1"];
    }

    public IReadOnlyList<string> Vigentes => _vigentes;

    /// <summary>La versión más reciente: la que vale cuando el cliente no manda ninguna.</summary>
    public string PorDefecto => _vigentes[^1];

    /// <summary>La versión canónica configurada, o <c>null</c> si la enviada no es una vigente.</summary>
    public string? Normalizar(string? version) => string.IsNullOrWhiteSpace(version)
        ? PorDefecto
        : _vigentes.FirstOrDefault(v => string.Equals(v, version.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Constancia mínima del consentimiento de las consultas públicas (HU #14041, Ley 1581): sin solicitud todavía, solo la huella
/// del documento (o del VIN) consultado, la versión del texto aceptado, el momento y la huella de la IP. Nunca el dato en claro.
/// </summary>
public sealed class ConsentimientoService(PersonaNaturalDb db, TimeProvider reloj)
{
    public async Task RegistrarConsultaAsync(byte[] documentoHash, string version, byte[]? ipHash, CancellationToken ct)
    {
        db.Add(new Consentimiento
        {
            Id = Guid.CreateVersion7(),
            SolicitudId = null,
            DocumentoHash = documentoHash,
            TextoVersion = version,
            AceptadoEn = reloj.GetUtcNow(),
            IpHash = ipHash,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public static class ConsentimientosExtensions
{
    /// <summary>Versiones vigentes del texto y constancia de las consultas públicas.</summary>
    public static IServiceCollection AddConsentimientosPortal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ConsentimientoVersiones>();
        services.AddScoped<ConsentimientoService>();
        return services;
    }
}
