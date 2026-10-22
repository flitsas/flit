using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Contraventor;

/// <summary>
/// Cuándo se consulta el conductor en la API de contratos (HU #13712). La API de Renting solo sabe quién tenía un vehículo
/// de su propia flota: no sirve para los de otro propietario aunque la misma gestora los administre (Bancolombia), ni
/// para una empresa sin relación con Renting. Por eso hay dos interruptores, los dos del SuperAdmin: el general de la
/// empresa (integración «API Renting» con la consulta de contraventor encendida) y la casilla de cada NIT monitoreado.
/// Apagar el general no borra las casillas: al volver a encenderlo, cada NIT queda como estaba.
/// </summary>
public static class ConsultaPorApi
{
    public static bool EmpresaLaUsa(ConfiguracionEmpresa config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.TipoIntegracionConductor == TipoIntegracionConductor.ApiRenting && config.AllowsContravenerConsultation;
    }

    /// <summary>
    /// Números base con los que un documento puede coincidir con un NIT monitoreado. El dígito de verificación a veces viene
    /// pegado («8110117798»): si el último es el DV correcto, también vale sin él.
    /// </summary>
    public static IReadOnlyList<string> CandidatosNit(string? documento)
    {
        if (Nit.Separar(documento)?.Base is not { } numero)
            return [];
        return numero.Length > 1 && Nit.DigitoVerificacion(numero[..^1]) == numero[^1] - '0' ? [numero, numero[..^1]] : [numero];
    }

    /// <summary>
    /// El comparendo es de un NIT con la casilla marcada: el NIT que lo trajo o, si llegó por placa u OCR, el del
    /// propietario. Sin propietario conocido, no.
    /// </summary>
    public static bool DeNitMarcado(Comparendo c, IReadOnlyCollection<IdentificacionMonitoreada> marcados)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(marcados);
        if (c.IdentificacionMonitoreadaId is { } id)
            return marcados.Any(n => n.Id == id);
        var candidatos = CandidatosNit(c.NumeroDocumentoInfractor);
        return marcados.Any(n => n.Activo && candidatos.Contains(n.NumeroBase));
    }

    public static Task<List<IdentificacionMonitoreada>> MarcadosAsync(ComparendosDb db, Guid tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.IdentificacionesMonitoreadas.AsNoTracking().Where(n => n.TenantId == tenantId && n.ConsultaContratoApi).ToListAsync(ct);
    }

    public static async Task<bool> AplicaAsync(ComparendosDb db, ConfiguracionEmpresa config, Comparendo c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        return EmpresaLaUsa(config) && DeNitMarcado(c, await MarcadosAsync(db, c.TenantId, ct).ConfigureAwait(false));
    }

    /// <summary>Estado de la consulta con que nace un comparendo.</summary>
    public static async Task<EstadoConsultaContraventor> EstadoInicialAsync(ComparendosDb db, ConfiguracionEmpresa config, Comparendo c, CancellationToken ct) =>
        await AplicaAsync(db, config, c, ct).ConfigureAwait(false) ? EstadoConsultaContraventor.Pendiente : EstadoConsultaContraventor.NoAplica;

    /// <summary>
    /// Tras cambiar un interruptor: los comparendos activos sin conductor a los que ahora aplica quedan pendientes de
    /// consulta, y los que estaban pendientes o en error y ya no aplica pasan a asignación manual. Lo ya encontrado o no
    /// encontrado no se toca.
    /// </summary>
    public static async Task RecalcularAsync(ComparendosDb db, Guid tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var config = await db.ConfiguracionesEmpresa.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct).ConfigureAwait(false);
        var marcados = config is not null && EmpresaLaUsa(config) ? await MarcadosAsync(db, tenantId, ct).ConfigureAwait(false) : [];
        var candidatos = await db.Comparendos.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Activo && c.Contraventor == null && (c.EstadoConsultaContraventor == EstadoConsultaContraventor.NoAplica
                || c.EstadoConsultaContraventor == EstadoConsultaContraventor.Pendiente || c.EstadoConsultaContraventor == EstadoConsultaContraventor.Error))
            .Select(c => new Comparendo
            {
                Id = c.Id, TenantId = c.TenantId, IdentificacionMonitoreadaId = c.IdentificacionMonitoreadaId,
                NumeroDocumentoInfractor = c.NumeroDocumentoInfractor, EstadoConsultaContraventor = c.EstadoConsultaContraventor,
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var aPendiente = candidatos.Where(c => c.EstadoConsultaContraventor == EstadoConsultaContraventor.NoAplica && DeNitMarcado(c, marcados)).Select(c => c.Id).ToList();
        var aManual = candidatos.Where(c => c.EstadoConsultaContraventor != EstadoConsultaContraventor.NoAplica && !DeNitMarcado(c, marcados)).Select(c => c.Id).ToList();
        if (aPendiente.Count > 0)
            await db.Comparendos.Where(c => aPendiente.Contains(c.Id))
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.EstadoConsultaContraventor, EstadoConsultaContraventor.Pendiente), ct).ConfigureAwait(false);
        if (aManual.Count > 0)
            await db.Comparendos.Where(c => aManual.Contains(c.Id))
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.EstadoConsultaContraventor, EstadoConsultaContraventor.NoAplica), ct).ConfigureAwait(false);
    }
}
