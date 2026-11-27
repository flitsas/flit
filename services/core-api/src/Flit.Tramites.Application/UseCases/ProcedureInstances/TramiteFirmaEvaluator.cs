using Flit.Queries.Domain.Time;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Épica #14666 / HU #14698 (A1), ADR-0092 — criterio ÚNICO de «parte firmada» y cálculo por trámite:
/// ¿firmaron todas las partes que deben firmar? y, si no, ¿quién falta (y si fue rechazada)?
/// Fachada PÚBLICA sobre el resolvedor del listado (<c>IdentityApprovalResolver</c>, internal) para que
/// Infrastructure (Consultas) no duplique reglas ni necesite <c>InternalsVisibleTo</c>.
/// </summary>
/// <remarks>
/// Uso: <c>var ctx = await evaluator.CargarContextoAsync(tramites, now, ct);</c> y luego, por fila,
/// <c>evaluator.Evaluar(tramite, ctx, now)</c> ⇒ <c>FirmadoPorTodas</c> + <c>Pendientes</c>.
/// Sin llamadas a proveedores externos: solo lecturas en lote de la base propia.
/// </remarks>
public interface ITramiteFirmaEvaluator
{
    /// <summary>
    /// Lecturas en lote (sin N+1) que alimentan el criterio del listado: identidades vigentes, firmas de baúl,
    /// identidades protegidas por vencimiento manual, salidas de borrador y baúl copiado al radicar.
    /// </summary>
    Task<TramiteFirmaContexto> CargarContextoAsync(
        IReadOnlyCollection<ProcedureInstance> tramites, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Evalúa un trámite (grafo cargado: tipo, actores y validaciones). Puro y sin E/S. No lanza: un fallo al
    /// calcular una persona la deja pendiente y se registra.
    /// </summary>
    TramiteFirmaResultado Evaluar(ProcedureInstance tramite, TramiteFirmaContexto contexto, DateTimeOffset now);
}

/// <summary>Claves precomputadas en lote, las mismas que usa el listado de trámites.</summary>
public sealed record TramiteFirmaContexto(
    IReadOnlySet<string> IdentidadKeys,
    IReadOnlyDictionary<string, bool> FirmaBaul,
    IReadOnlyDictionary<string, IReadOnlyList<IdentidadVencidaManual>> ProtegidasKeys,
    IReadOnlyDictionary<Guid, DateTimeOffset> SalidasDeBorrador,
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, bool>> BaulPorTramite)
{
    /// <summary>Contexto sin ninguna acreditación previa (solo filas propias del trámite).</summary>
    public static TramiteFirmaContexto Vacio { get; } = new(
        new HashSet<string>(),
        new Dictionary<string, bool>(),
        new Dictionary<string, IReadOnlyList<IdentidadVencidaManual>>(),
        new Dictionary<Guid, DateTimeOffset>(),
        new Dictionary<Guid, IReadOnlyDictionary<string, bool>>());
}

/// <summary>Parte que aún no firma. <paramref name="Rol"/> es «Vendedor» o «Comprador».</summary>
public sealed record ParteFirmaPendiente(string Rol, string Nombre, bool Rechazada)
{
    /// <summary><c>Comprador: Ana Pérez</c> o <c>Comprador: Ana Pérez (rechazada)</c>.</summary>
    public string Texto => $"{Rol}: {Nombre}{(Rechazada ? " (rechazada)" : string.Empty)}";
}

/// <summary>Textos legibles del estado de firma (los usan el filtro y los Excel).</summary>
public static class FirmaTextos
{
    public const string Completas = "Firmas completas";
    public const string Faltan = "Faltan firmas";
    /// <summary>Trámite sin ninguna parte que firme: no se afirma «completas», porque nadie firmó.</summary>
    public const string SinPartes = "Sin partes que firmen";
    /// <summary>Valor del filtro «Firmas» para un trámite sin partes que firmen.</summary>
    public const string ValorSinPartes = "sin_partes";
    public const string Firmo = "Firmó";
    public const string Pendiente = "Pendiente";
    public const string Rechazada = "Rechazada";
    public const string Expirada = "Expirada";
    public const string SinValidar = "Sin validar";
}

/// <summary>
/// Una persona que debe firmar. <paramref name="Rol"/> es «Vendedor» o «Comprador»; <paramref name="Estado"/> es el
/// texto legible («Firmó», «Pendiente», «Rechazada», «Expirada», «Sin validar»). <paramref name="FechaFirma"/> solo si
/// existe una validación aprobada propia.
/// </summary>
public sealed record PersonaFirma(
    string Rol, int Ordinal, string Nombre, string? TipoDocumento, string? Documento,
    bool Firmo, string Estado, DateTimeOffset? FechaFirma);

/// <summary>Resultado por trámite: sin partes que deban firmar ⇒ <c>FirmadoPorTodas = false</c> (no es «completas») y lista vacía.</summary>
public sealed record TramiteFirmaResultado(
    bool FirmadoPorTodas,
    IReadOnlyList<ParteFirmaPendiente> Pendientes,
    IReadOnlyList<PersonaFirma>? Personas = null)
{
    /// <summary>Todas las personas que aplican (firmen o no), vendedores primero. Vacío si ninguna parte firma.</summary>
    public IReadOnlyList<PersonaFirma> Partes => Personas ?? [];

    /// <summary>Partes que deben firmar (M de «N de M»).</summary>
    public int TotalPartes => Partes.Count;

    /// <summary>Partes que ya firmaron (N de «N de M»).</summary>
    public int Firmaron => Partes.Count(p => p.Firmo);

    /// <summary>
    /// Valor del filtro «Firmas» (un solo valor por trámite): <c>sin_partes</c>, <c>true</c> (completas) o
    /// <c>false</c> (faltan firmas). Épica #14666 / HU #14698: sin partes NO es «completas» ni «faltan».
    /// </summary>
    public string ValorFiltro => TotalPartes == 0
        ? FirmaTextos.ValorSinPartes
        : FirmadoPorTodas ? "true" : "false";

    /// <summary>«2 de 3 · Faltan firmas» / «3 de 3 · Firmas completas»; sin partes que firmen: «Sin partes que firmen».</summary>
    public string TextoResumen => TotalPartes == 0
        ? FirmaTextos.SinPartes
        : $"{Firmaron} de {TotalPartes} · {(FirmadoPorTodas ? FirmaTextos.Completas : FirmaTextos.Faltan)}";

    /// <summary><c>Vendedor: A · Comprador: B</c>; vacío si no falta nadie.</summary>
    public string TextoPendientes => string.Join(" · ", Pendientes.Select(p => p.Texto));
}

public sealed partial class TramiteFirmaEvaluator(
    IProcedureInstanceRepository repo,
    ILogger<TramiteFirmaEvaluator>? logger = null) : ITramiteFirmaEvaluator
{
    public async Task<TramiteFirmaContexto> CargarContextoAsync(
        IReadOnlyCollection<ProcedureInstance> tramites, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tramites);
        if (tramites.Count == 0)
            return TramiteFirmaContexto.Vacio;

        var tenantIds = tramites.Select(t => t.TenantId).Distinct().ToList();
        var identidadKeys = await repo.ListVigenteApprovedIdentityKeysAsync(tenantIds, now, ct).ConfigureAwait(false)
            ?? new HashSet<string>();
        var protegidas = await repo.ListProtegidasPorVencimientoManualKeysAsync(tenantIds, now, ct).ConfigureAwait(false)
            ?? ListProcedureInstancesHandler.EmptyProtegidas;
        var salidas = await ListProcedureInstancesHandler
            .ResolverSalidasDeBorradorAsync(repo, tramites, protegidas, ct).ConfigureAwait(false);
        var hoy = DateOnly.FromDateTime(now.ToOffset(ColombiaTime.Offset).DateTime);
        var firmaBaul = await repo.ListFirmaBaulVigenciaKeysAsync(tenantIds, hoy, ct).ConfigureAwait(false)
            ?? new Dictionary<string, bool>();
        var baulPorTramite = await ListProcedureInstancesHandler
            .ResolverBaulPorTramiteAsync(repo, tramites, ct).ConfigureAwait(false);

        return new TramiteFirmaContexto(identidadKeys, firmaBaul, protegidas, salidas, baulPorTramite);
    }

    public TramiteFirmaResultado Evaluar(ProcedureInstance tramite, TramiteFirmaContexto contexto, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tramite);
        ArgumentNullException.ThrowIfNull(contexto);

        // Roles que firman según el tipo (misma fuente que el gate: FirmaGate → PartesDeclaradas.Identidad).
        // Orden vendedor → comprador (formato «Vendedor: … · Comprador: …»).
        var roles = FirmaGate.PartesQueFirman(tramite);
        var partes = new[] { BiometricRules.ParteVendedor, BiometricRules.ParteComprador }
            .Where(p => PartesDeclaradas.Incluye(roles, p))
            .ToList();

        var baul = contexto.BaulPorTramite.TryGetValue(tramite.Id, out var copiado) ? copiado : contexto.FirmaBaul;
        DateTimeOffset? salida = contexto.SalidasDeBorrador.TryGetValue(tramite.Id, out var s) ? s : null;

        IReadOnlySet<ProcedureInstanceActor>? cubiertos = null;
        try
        {
            cubiertos = IdentityApprovalResolver.ApprovedActorsFromKeys(
                tramite, contexto.IdentidadKeys, now, baul, contexto.ProtegidasKeys, salida);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Se aísla persona por persona más abajo: el fallo de una no tumba al resto.
            if (logger is not null)
                FirmaLog.TramiteFallo(logger, tramite.Id, ex.GetType().Name);
        }

        var pendientes = new List<ParteFirmaPendiente>();
        var personas = new List<PersonaFirma>();
        foreach (var parte in partes)
        {
            var delLado = tramite.Actors
                .Where(a => string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Ordinal);
            foreach (var actor in delLado)
            {
                bool firmado;
                try
                {
                    firmado = cubiertos is not null
                        ? cubiertos.Contains(actor)
                        : IdentityApprovalResolver.ActorCubierto(
                            tramite, parte, actor, contexto.IdentidadKeys, now, baul, contexto.ProtegidasKeys, salida);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Sin PII: solo ids y tipo de excepción.
                    if (logger is not null)
                        FirmaLog.ActorFallo(logger, actor.Id, tramite.Id, ex.GetType().Name);
                    firmado = false;
                }

                var ultima = UltimaPropia(tramite, parte, actor);
                var rechazada = ultima is not null && ultima.Status == BiometricEstados.Rechazado;
                if (!firmado)
                    pendientes.Add(new ParteFirmaPendiente(
                        Etiqueta(parte), actor.FullName?.Trim() ?? string.Empty, rechazada));

                personas.Add(new PersonaFirma(
                    Etiqueta(parte), actor.Ordinal, actor.FullName?.Trim() ?? string.Empty,
                    actor.DocumentType, actor.DocumentNumber, firmado,
                    EstadoTexto(firmado, ultima), firmado ? FechaDeFirma(tramite, parte, actor) : null));
            }
        }

        // Sin ninguna parte que firme no es «Firmas completas»: nadie firmó. Épica #14666 / HU #14698.
        return new TramiteFirmaResultado(personas.Count > 0 && pendientes.Count == 0, pendientes, personas);
    }

    private static string Etiqueta(string parte) =>
        string.Equals(parte, BiometricRules.ParteVendedor, StringComparison.OrdinalIgnoreCase) ? "Vendedor" : "Comprador";

    /// <summary>
    /// Validación PROPIA más reciente de la persona para esa parte. Las claves en lote solo traen aprobadas, así que
    /// el rechazo y el estado solo se leen de las filas del trámite (ADR-0092). Sin PII; no lanza.
    /// </summary>
    private static ProcedureInstanceBiometricValidation? UltimaPropia(
        ProcedureInstance tramite, string parte, ProcedureInstanceActor actor)
    {
        try
        {
            var subject = IdentitySubjectResolver.For(actor);
            return tramite.BiometricValidations
                .Where(v => string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
                            && BiometricRules.DocumentoCoincide(v, subject.TipoDocumento, subject.NumeroDocumento))
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static DateTimeOffset? FechaDeFirma(ProcedureInstance tramite, string parte, ProcedureInstanceActor actor)
    {
        try
        {
            var subject = IdentitySubjectResolver.For(actor);
            var aprobada = tramite.BiometricValidations
                .Where(v => string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
                            && v.Status == BiometricEstados.Aprobado
                            && BiometricRules.DocumentoCoincide(v, subject.TipoDocumento, subject.NumeroDocumento))
                .OrderByDescending(v => v.UpdatedAt ?? v.CreatedAt)
                .FirstOrDefault();
            return aprobada is null ? null : aprobada.UpdatedAt ?? aprobada.CreatedAt;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string EstadoTexto(bool firmado, ProcedureInstanceBiometricValidation? ultima)
    {
        if (firmado)
            return FirmaTextos.Firmo;
        if (ultima is null)
            return FirmaTextos.SinValidar;
        return ultima.Status switch
        {
            BiometricEstados.Rechazado => FirmaTextos.Rechazada,
            BiometricEstados.Expirado => FirmaTextos.Expirada,
            _ => FirmaTextos.Pendiente,
        };
    }

    private static partial class FirmaLog
    {
        [LoggerMessage(Level = LogLevel.Error,
            Message = "Firma: fallo al evaluar el trámite {TramiteId} ({Tipo}); se evalúa actor por actor.")]
        public static partial void TramiteFallo(ILogger logger, Guid tramiteId, string tipo);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Firma: fallo al evaluar el actor {ActorId} del trámite {TramiteId} ({Tipo}); queda pendiente.")]
        public static partial void ActorFallo(ILogger logger, Guid actorId, Guid tramiteId, string tipo);
    }
}
