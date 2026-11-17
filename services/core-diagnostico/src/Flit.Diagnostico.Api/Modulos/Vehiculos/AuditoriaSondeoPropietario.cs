using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Vehiculos;

/// <summary>
/// Auditoría de sondeo del oráculo de <c>cambio_propietario</c> (HU #14582, contrato-api §1.5, SP-5): al confirmar vehículos con
/// propietario declarado (lote o Express), cuenta en la compañía cuántos vehículos con declarado tiene cada una de esas placas en los
/// diagnósticos creados hoy (hora Colombia, <see cref="VigenciaDelDia"/>). Si alguna supera <see cref="ClaveUmbral"/> (por defecto
/// <see cref="UmbralPorDefecto"/>), deja un Warning con <see cref="Marca"/>, el diagnóstico, la compañía y el conteo: nunca la placa en
/// claro ni el documento. No bloquea: el freno es el límite de solicitudes (<see cref="Plataforma.LimitesSolicitudes"/>). Lee solo con
/// el contexto de la compañía (filtro global) y no descifra nada.
/// </summary>
/// <example>
/// <code>
/// await auditoria.RevisarAsync(cabecera, vehiculosGuardados, ct); // dentro de la transacción de la confirmación
/// </code>
/// </example>
internal sealed partial class AuditoriaSondeoPropietario(
    DiagnosticoDb db, IConfiguration configuracion, TimeProvider reloj, ILogger<AuditoriaSondeoPropietario> log)
{
    public const string Marca = "SONDEO_PROPIETARIO";
    public const string ClaveUmbral = "Diagnostico:Alertas:DeclaracionesPorPlacaDia";
    public const int UmbralPorDefecto = 5;

    public async Task RevisarAsync(CabeceraDiagnostico cabecera, IReadOnlyList<VehiculoDiagnostico> vehiculos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(vehiculos);
        var placas = vehiculos.Where(v => v.PropietarioDeclaradoDocCifrado is not null).Select(v => v.IdentificadorNorm).Distinct().ToList();
        if (placas.Count == 0)
            return;

        var umbral = Math.Max(1, configuracion.GetValue(ClaveUmbral, UmbralPorDefecto));
        var ahora = reloj.GetUtcNow();
        var desde = VigenciaDelDia.InicioDelDia(ahora).ToUniversalTime();
        var hasta = VigenciaDelDia.FinDelDia(ahora).ToUniversalTime();
        var tenant = cabecera.TenantId;
        var conteos = await (
                from v in db.Set<VehiculoDiagnostico>().AsNoTracking()
                join c in db.Set<CabeceraDiagnostico>().AsNoTracking() on new { v.TenantId, Id = v.DiagnosticoId } equals new { c.TenantId, c.Id }
                where v.TenantId == tenant && c.CreadoEn >= desde && c.CreadoEn < hasta
                    && v.PropietarioDeclaradoDocCifrado != null && placas.Contains(v.IdentificadorNorm)
                group v by v.IdentificadorNorm into g
                select g.Count())
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var conteo in conteos.Where(c => c > umbral))
            Sondeo(log, Marca, cabecera.Id, tenant, conteo, umbral);
    }

    [LoggerMessage(EventId = 14583, Level = LogLevel.Warning,
        Message = "{Marca}: una placa del diagnóstico {DiagnosticoId} lleva {Conteo} declaraciones de propietario hoy en la compañía {Compania} (umbral {Umbral})")]
    private static partial void Sondeo(ILogger logger, string marca, Guid diagnosticoId, Guid compania, int conteo, int umbral);
}
