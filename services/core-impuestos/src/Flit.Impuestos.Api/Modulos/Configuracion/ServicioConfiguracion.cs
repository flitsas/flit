using System.Net.Mail;
using Flit.Impuestos.Api.Modulos.Catalogos;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Persistence.Siembra;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Configuracion;

/// <summary>
/// Configuración de Impuestos de una compañía en dos niveles (D-CONFIG, plan §5.1; CO-01 a CO-04):
/// <list type="bullet">
///   <item><see cref="GuardarOperativaAsync"/> (Administrador Impuestos): máximo por lote entre 1 y 5000 o null (vale el
///   contractual) y nunca mayor que el contractual (422 <c>TOPE_SUPERA_CONTRACTUAL</c> con <c>tope</c>); hasta 20 correos del
///   aviso «Lote terminado», recortados y sin repetir.</item>
///   <item><see cref="GuardarContractualAsync"/> (solo Super Admin; lo garantiza el endpoint): máximo entre 1 y 5000 y
///   departamentos habilitados ⊆ <c>GET /fuentes</c> (null = todos); exige la compañía con Impuestos (404) en Listo o Listo sin
///   webhook (409). Si el contractual queda por debajo del operativo, el operativo se ajusta.</item>
///   <item><see cref="CopiaAsync"/>: la copia para la cabecera del lote (<see cref="CopiaConfiguracion"/>).</item>
/// </list>
/// Sin fila se leen los valores de <see cref="ConfiguracionPorDefecto"/>; la primera escritura la siembra. Cada operación abre el
/// contexto de la compañía (<see cref="AlmacenCredenciales.ContextoDe"/>), así que filtra y valida la escritura como una
/// petición de esa compañía aunque quien actúe sea el Super Admin.
/// </summary>
/// <example>
/// <code>
/// var vista = await servicio.GuardarOperativaAsync(empresa, new ConfiguracionOperativaEntrada(300, ["avisos@empresa.co"]), actor, ct);
/// var copia = await servicio.CopiaAsync(empresa, concurrencia: 2, ct);
/// </code>
/// </example>
public sealed class ServicioConfiguracion(AlmacenCredenciales almacen, SincronizacionFuentes fuentes, TimeProvider reloj)
{
    public const int MaxCorreos = 20;
    public const int LargoMaximoCorreo = 254;

    private const string CampoMaximo = "maxRegistrosLote";
    private const string CampoCorreos = "correosAviso";
    private const string CampoDepartamentos = "departamentosHabilitados";

    public async Task<ConfiguracionDto> LeerAsync(Guid empresa, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var fila = await db.Set<ConfiguracionEmpresa>().AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false);
        return await VistaAsync(db, fila ?? ConfiguracionPorDefecto.Nueva(empresa, reloj.GetUtcNow()), ct).ConfigureAwait(false);
    }

    public async Task<ConfiguracionDto> GuardarOperativaAsync(Guid empresa, ConfiguracionOperativaEntrada entrada, Guid? actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.MaxRegistrosLote is { } maximo && !EnRango(maximo))
            throw ErroresConfiguracion.TopeInvalidoEn(CampoMaximo);
        var correos = Correos(entrada.CorreosAviso);

        await using var db = almacen.ContextoDe(empresa);
        var ahora = reloj.GetUtcNow();
        var fila = await ConfiguracionPorDefecto.SembrarAsync(db, empresa, ahora, ct).ConfigureAwait(false);
        if (entrada.MaxRegistrosLote > fila.MaxRegistrosLoteContractual)
            throw ErroresConfiguracion.SuperaContractual(fila.MaxRegistrosLoteContractual);

        fila.MaxRegistrosLoteOperativo = entrada.MaxRegistrosLote;
        fila.CorreosAviso = correos;
        return await GuardarAsync(db, fila, ahora, actor, ct).ConfigureAwait(false);
    }

    public async Task<ConfiguracionDto> GuardarContractualAsync(Guid empresa, ConfiguracionContractualEntrada entrada, Guid? actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.MaxRegistrosLote is not { } maximo || !EnRango(maximo))
            throw ErroresConfiguracion.TopeInvalidoEn(CampoMaximo);

        await using var db = almacen.ContextoDe(empresa);
        var estado = await SincronizacionFuentes.EstadoAsync(db, empresa, ct).ConfigureAwait(false);
        if (estado is null)
            throw ErroresConfiguracion.NoEncontrada();
        if (!SincronizacionFuentes.EstaLista(estado))
            throw ErroresConfiguracion.NoListo();

        string[]? departamentos = null;
        if (entrada.DepartamentosHabilitados is { } pedidos)
        {
            departamentos = [.. pedidos.Select(d => d?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            var disponibles = (await fuentes.ObtenerAsync(db, empresa, ct).ConfigureAwait(false))
                .Select(f => f.DepartamentoCodigo).ToHashSet(StringComparer.Ordinal);
            if (departamentos.Any(d => !disponibles.Contains(d)))
                throw ErroresConfiguracion.DepartamentoNoDisponibleEn(CampoDepartamentos);
        }

        var ahora = reloj.GetUtcNow();
        var fila = await ConfiguracionPorDefecto.SembrarAsync(db, empresa, ahora, ct).ConfigureAwait(false);
        fila.MaxRegistrosLoteContractual = maximo;
        fila.DepartamentosHabilitados = departamentos;
        if (fila.MaxRegistrosLoteOperativo > maximo)
            fila.MaxRegistrosLoteOperativo = maximo;
        return await GuardarAsync(db, fila, ahora, actor, ct).ConfigureAwait(false);
    }

    public async Task<CopiaConfiguracion> CopiaAsync(Guid empresa, int concurrencia, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var fila = await db.Set<ConfiguracionEmpresa>().AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false);
        return CopiaConfiguracion.De(fila, concurrencia);
    }

    private static bool EnRango(int maximo) =>
        maximo is >= ConfiguracionPorDefecto.MaxRegistrosLoteMinimo and <= ConfiguracionPorDefecto.MaxRegistrosLoteMaximo;

    /// <summary>Correos recortados y sin repetir (sin distinguir mayúsculas); 422 <c>CORREO_INVALIDO</c> si uno no es válido o son más de 20.</summary>
    private static string[] Correos(IReadOnlyList<string>? correos)
    {
        var lista = new List<string>();
        foreach (var crudo in correos ?? [])
        {
            var correo = crudo?.Trim() ?? string.Empty;
            if (!EsCorreo(correo))
                throw ErroresConfiguracion.CorreoInvalidoEn(CampoCorreos);
            if (!lista.Contains(correo, StringComparer.OrdinalIgnoreCase))
                lista.Add(correo);
        }

        return lista.Count > MaxCorreos ? throw ErroresConfiguracion.CorreoInvalidoEn(CampoCorreos) : [.. lista];
    }

    private static bool EsCorreo(string correo) =>
        correo.Length is > 0 and <= LargoMaximoCorreo
        && MailAddress.TryCreate(correo, out var direccion)
        && string.Equals(direccion.Address, correo, StringComparison.Ordinal)
        && direccion.Host.Contains('.', StringComparison.Ordinal);

    private static async Task<ConfiguracionDto> GuardarAsync(ImpuestosDb db, ConfiguracionEmpresa fila, DateTimeOffset ahora, Guid? actor, CancellationToken ct)
    {
        fila.Version++;
        fila.ActualizadoEn = ahora;
        fila.ActualizadoPor = actor;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await VistaAsync(db, fila, ct).ConfigureAwait(false);
    }

    private static async Task<ConfiguracionDto> VistaAsync(ImpuestosDb db, ConfiguracionEmpresa fila, CancellationToken ct)
    {
        var disponibles = await SincronizacionFuentes.CacheAsync(db, ct).ConfigureAwait(false);
        return new ConfiguracionDto(
            new ConfiguracionContractualDto(fila.MaxRegistrosLoteContractual, fila.DepartamentosHabilitados is { } d ? [.. d] : null),
            new ConfiguracionOperativaDto(fila.MaxRegistrosLoteOperativo, [.. fila.CorreosAviso]),
            [.. disponibles.Select(f => new DepartamentoDto(f.DepartamentoCodigo, f.Nombre, f.Activa))]);
    }
}
