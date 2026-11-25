using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Credenciales;

/// <summary>
/// Credenciales del API de una compañía en claro, solo en memoria. <see cref="ToString"/> nunca imprime los valores (EP-CF3).
/// </summary>
public sealed record CredencialesEnClaro(string ApiKey, string? WebhookSecret, string? EmpresaApiId = null)
{
    public override string ToString() =>
        $"CredencialesEnClaro {{ ApiKey = [REDACTADO], WebhookSecret = {(WebhookSecret is null ? "null" : "[REDACTADO]")}, EmpresaApiId = {EmpresaApiId} }}";
}

/// <summary>
/// Guarda y lee <c>impuestos.credenciales_empresa</c> (DA-2=b, plan §4.2): la api_key y el webhook_secret van cifrados con
/// <see cref="IProteccionImpuestos.ProtegerCredencial"/>, un propósito por compañía y por código, así que la fila de una compañía
/// no se descifra con el propósito de otra ni con el de otro código. Sin <c>IgnoreQueryFilters</c>: quien actúa sobre una
/// compañía que no es la del token (backoffice, consumidores) abre <see cref="ContextoDe"/>, un contexto con esa compañía fijada
/// que filtra y valida la escritura como una petición de esa compañía. <see cref="Escribir"/> solo prepara la fila, para que
/// quien la llame la guarde en la misma transacción que el estado del aprovisionamiento.
/// </summary>
/// <example>
/// <code>
/// await using var db = almacen.ContextoDe(empresa);
/// var actual = await almacen.FilaAsync(db, empresa, ct);
/// almacen.Escribir(db, actual, empresa, new CredencialesEnClaro(apiKey, secreto), OrigenCredencial.CargaManual, actor);
/// await db.SaveChangesAsync(ct);
/// var enClaro = await almacen.LeerAsync(empresa, ct);
/// </code>
/// </example>
public sealed class AlmacenCredenciales(DbContextOptions<ImpuestosDb> opciones, IProteccionImpuestos proteccion, TimeProvider reloj)
{
    /// <summary>Caracteres de la api_key que se guardan en claro para soporte (los mismos que <c>api_key_prefix</c> del API).</summary>
    public const int LargoPrefijo = 8;

    /// <summary>Por debajo de este largo no se guarda prefijo: sería casi toda la llave.</summary>
    public const int LargoMinimoParaPrefijo = 16;

    /// <summary>Contexto con <paramref name="empresa"/> fijada: filtra y valida la escritura como una petición de esa compañía.</summary>
    public ImpuestosDb ContextoDe(Guid empresa)
    {
        if (empresa == Guid.Empty)
            throw new ArgumentException("Las credenciales exigen una compañía.", nameof(empresa));
        return new ImpuestosDb(opciones, new EmpresaFijada(empresa));
    }

    /// <summary>La fila cifrada de la compañía (con seguimiento, para actualizarla); null si no tiene o si el contexto es de otra.</summary>
    public Task<CredencialEmpresa?> FilaAsync(ImpuestosDb db, Guid empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Set<CredencialEmpresa>().FirstOrDefaultAsync(c => c.TenantId == empresa, ct);
    }

    /// <summary>Las credenciales de la compañía en claro, con un contexto propio; null si no tiene.</summary>
    public async Task<CredencialesEnClaro?> LeerAsync(Guid empresa, CancellationToken ct)
    {
        await using var db = ContextoDe(empresa);
        return await LeerAsync(db, empresa, ct).ConfigureAwait(false);
    }

    /// <summary>Las credenciales de la compañía en claro con el contexto dado; null si no tiene o si el contexto es de otra.</summary>
    public async Task<CredencialesEnClaro?> LeerAsync(ImpuestosDb db, Guid empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var fila = await db.Set<CredencialEmpresa>().AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false);
        if (fila is null)
            return null;
        return new CredencialesEnClaro(
            proteccion.DesprotegerCredencial(empresa, CodigosCredencial.ApiKey, fila.ApiKeyCifrada),
            fila.WebhookSecretCifrado is null ? null : proteccion.DesprotegerCredencial(empresa, CodigosCredencial.WebhookSecret, fila.WebhookSecretCifrado),
            fila.EmpresaApiId);
    }

    /// <summary>
    /// Prepara (sin guardar) la fila de la compañía con las credenciales nuevas cifradas: crea la fila si <paramref name="actual"/>
    /// es null; si no, la reemplaza y sube su versión. Sin webhook_secret, el anterior se borra (la carga reemplaza el par). El id
    /// de empresa del API se conserva si no viene uno nuevo.
    /// </summary>
    public CredencialEmpresa Escribir(ImpuestosDb db, CredencialEmpresa? actual, Guid empresa, CredencialesEnClaro nuevas, OrigenCredencial origen, Guid? actor)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(nuevas);
        ArgumentException.ThrowIfNullOrWhiteSpace(nuevas.ApiKey);
        if (empresa == Guid.Empty)
            throw new ArgumentException("Las credenciales exigen una compañía.", nameof(empresa));
        if (actual is not null && actual.TenantId != empresa)
            throw new ArgumentException("La fila actual es de otra compañía.", nameof(actual));

        var fila = actual ?? new CredencialEmpresa { TenantId = empresa, Version = 0 };
        fila.ApiKeyCifrada = proteccion.ProtegerCredencial(empresa, CodigosCredencial.ApiKey, nuevas.ApiKey);
        fila.ApiKeyPrefijo = Prefijo(nuevas.ApiKey);
        fila.WebhookSecretCifrado = nuevas.WebhookSecret is null
            ? null
            : proteccion.ProtegerCredencial(empresa, CodigosCredencial.WebhookSecret, nuevas.WebhookSecret);
        fila.EmpresaApiId = nuevas.EmpresaApiId ?? fila.EmpresaApiId;
        fila.Origen = origen;
        fila.Version++;
        fila.ActualizadoEn = reloj.GetUtcNow();
        fila.ActualizadoPor = actor;
        if (actual is null)
            db.Add(fila);
        return fila;
    }

    /// <summary>Los primeros <see cref="LargoPrefijo"/> caracteres; null si la llave es más corta que <see cref="LargoMinimoParaPrefijo"/>.</summary>
    public static string? Prefijo(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        return apiKey.Length < LargoMinimoParaPrefijo ? null : apiKey[..LargoPrefijo];
    }

    /// <summary>La compañía de la operación: el contexto filtra y valida con ella, como una petición con su token.</summary>
    private sealed class EmpresaFijada(Guid empresa) : IPlatformTenantAccessor
    {
        public PlatformTenantContext Current { get; } = new(empresa, IsSuperAdmin: false, IsService: true);
    }
}
