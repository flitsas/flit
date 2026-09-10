using System.Globalization;
using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>Lo que el motor necesita para decidir. Todo son datos ya obtenidos: aquí no hay I/O.</summary>
/// <param name="ProcedureTypeCode">Código del tipo de trámite (<c>tramites.procedure_types.code</c>).</param>
/// <param name="Family">Familia del tipo: decide cómo se consultó.</param>
/// <param name="CutoffDate">Día (Bogotá) de envío al OT. Nada anterior confirma: el historial del RUNT es acumulativo.</param>
/// <param name="Primary">Consulta principal: por VIN (matrícula), con el documento del comprador (traspaso) o del propietario (otros).</param>
/// <param name="Seller">Traspaso: la consulta con el documento del vendedor. NULL en las demás familias.</param>
/// <param name="Baseline">Respuesta del RUNT guardada al radicar (snapshot). NULL si no hay.</param>
/// <param name="ExpectedPlate">Placa del expediente (preasignada o capturada), si la hay.</param>
/// <param name="TransitOfficeName">Nombre del organismo de tránsito del trámite, para el refuerzo por entidad.</param>
public sealed record RuntConfirmationInput(
    string ProcedureTypeCode,
    ProcedureFamily Family,
    DateOnly CutoffDate,
    RuntVehicleSnapshot? Primary,
    RuntVehicleSnapshot? Seller,
    RuntVehicleSnapshot? Baseline,
    string? ExpectedPlate,
    string? TransitOfficeName);

public sealed record RuntConfirmationDecision(RuntConfirmationVerdict Verdict, string Reason, string RuleVersion)
{
    public bool DejaFlag(out string? flag)
    {
        flag = Verdict switch
        {
            RuntConfirmationVerdict.Discrepancy => RuntConfirmationFlags.Discrepancia,
            RuntConfirmationVerdict.Unverifiable => RuntConfirmationFlags.NoVerificable,
            _ => null,
        };
        return flag is not null;
    }
}

/// <summary>Una regla por tipo de trámite. Se resuelve por <c>procedure_type.code</c> en <see cref="RuntConfirmationRules"/>.</summary>
public interface IRuntConfirmationRule
{
    RuntConfirmationDecision Evaluate(RuntConfirmationInput input);
}

/// <summary>
/// Motor de confirmación (HU #12308). Una regla canónica para las tres familias: de
/// <c>solicitudes[]</c>, las que contengan el trámite RUNT esperado con fecha ≥ radicación; la más
/// reciente decide (AUTORIZADA/APROBADA → Confirmado, REGISTRADA → Pendiente, RECHAZADA → Discrepancia,
/// ninguna → Pendiente). Lo que cambia por tipo es el trámite esperado y el refuerzo.
/// </summary>
public static class RuntConfirmationRules
{
    /// <summary>Versión de la regla. Cambia cuando cambia el criterio o una equivalencia; los intentos la guardan para poder re-evaluar.</summary>
    public const string Version = "confirmacion-v1";

    public static RuntConfirmationDecision Evaluate(RuntConfirmationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Resolve(input.ProcedureTypeCode).Evaluate(input);
    }

    public static IRuntConfirmationRule Resolve(string? procedureTypeCode)
    {
        var eq = RuntProcedureEquivalence.Find(procedureTypeCode);
        if (eq is null)
            return new SinEquivalenciaRule(procedureTypeCode);

        return eq.Reinforcement switch
        {
            RuntReinforcement.Matricula => new MatriculaRule(eq),
            RuntReinforcement.Traspaso => new TraspasoRule(eq),
            _ => new SolicitudRule(eq),
        };
    }

    internal static string Dia(DateOnly? d) =>
        d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "sin fecha";

    internal static RuntConfirmationDecision Decision(RuntConfirmationVerdict v, string reason) =>
        new(v, reason, Version);
}

/// <summary>Tipo sin trámite RUNT conocido: no se puede verificar y no se vuelve a consultar.</summary>
internal sealed class SinEquivalenciaRule(string? code) : IRuntConfirmationRule
{
    public RuntConfirmationDecision Evaluate(RuntConfirmationInput input) =>
        RuntConfirmationRules.Decision(
            RuntConfirmationVerdict.Unverifiable,
            $"El tipo de trámite {code ?? "(sin código)"} no tiene trámite equivalente definido en el RUNT: sin equivalente definido.");
}

/// <summary>
/// Regla base sobre el historial de solicitudes. Las reglas de matrícula y traspaso la extienden
/// solo para elegir qué respuesta leer y qué refuerzo narrar.
/// </summary>
internal class SolicitudRule(RuntProcedureEquivalence eq) : IRuntConfirmationRule
{
    protected RuntProcedureEquivalence Equivalence { get; } = eq;

    public virtual RuntConfirmationDecision Evaluate(RuntConfirmationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var snapshot = input.Primary;
        if (snapshot is null || snapshot.Outcome == RuntVehicleOutcome.Unreadable)
            return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Error, "La respuesta del proveedor no se pudo interpretar.");

        if (snapshot.Outcome == RuntVehicleOutcome.NotFound)
            return RuntConfirmationRules.Decision(
                RuntConfirmationVerdict.Pending,
                "El proveedor no encontró el vehículo con el documento del propietario del expediente; se reintenta en la siguiente corrida.");

        return EvaluarHistorial(input, snapshot, refuerzo: Refuerzo(input, snapshot));
    }

    /// <summary>Aplica la regla canónica sobre <paramref name="snapshot"/> y compone el motivo con el refuerzo.</summary>
    protected RuntConfirmationDecision EvaluarHistorial(RuntConfirmationInput input, RuntVehicleSnapshot snapshot, Refuerzo refuerzo)
    {
        if (!snapshot.ExponeSolicitudes)
            return RuntConfirmationRules.Decision(
                RuntConfirmationVerdict.Unverifiable,
                $"El RUNT no expone el historial de solicitudes de este vehículo (mostrarSolicitudes={snapshot.MostrarSolicitudes ?? "vacío"}): no verificable.{refuerzo.Sufijo}");

        var candidatas = snapshot.Solicitudes
            .Where(s => s.Contiene(Equivalence.RuntTramite) && s.Fecha is not null && s.Fecha >= input.CutoffDate)
            .OrderByDescending(s => s.Fecha)
            .ToList();

        if (candidatas.Count == 0)
        {
            var historicas = snapshot.Solicitudes.Count(s => s.Contiene(Equivalence.RuntTramite));
            var nota = historicas > 0
                ? $" Hay {historicas} solicitud(es) de {Equivalence.RuntTramite} anteriores a la radicación, que no cuentan."
                : string.Empty;
            return RuntConfirmationRules.Decision(
                RuntConfirmationVerdict.Pending,
                $"No hay solicitud de {Equivalence.RuntTramite} posterior a la radicación ({RuntConfirmationRules.Dia(input.CutoffDate)}).{nota}{refuerzo.Sufijo}");
        }

        // La más reciente decide: el RUNT no actualiza una solicitud, crea otra.
        var decide = candidatas[0];
        var estado = decide.EstadoNormalizado;
        var cita = $"Solicitud {decide.NoSolicitud ?? "s/n"} {Equivalence.RuntTramite} {estado} el {RuntConfirmationRules.Dia(decide.Fecha)} en {decide.Entidad ?? "entidad no informada"}";
        var entidad = ContrasteEntidad(input.TransitOfficeName, decide.Entidad);

        if (estado == RuntSolicitudEstados.Rechazada)
            return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Discrepancy, $"{cita}: el RUNT rechazó el trámite.{entidad}{refuerzo.Sufijo}");

        if (!RuntSolicitudEstados.EsPositivo(estado))
            return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Pending, $"{cita}: radicada en el RUNT pero aún sin autorizar.{entidad}{refuerzo.Sufijo}");

        if (refuerzo.Bloquea)
            return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Pending, $"{cita}, pero {refuerzo.MotivoBloqueo}{entidad}");

        return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Confirmed, $"{cita}.{entidad}{refuerzo.Sufijo}");
    }

    protected virtual Refuerzo Refuerzo(RuntConfirmationInput input, RuntVehicleSnapshot actual) =>
        Equivalence.Reinforcement switch
        {
            RuntReinforcement.CampoColor => Campo("color", input.Baseline?.Color, actual.Color, obligatorio: false),
            RuntReinforcement.CampoCarroceriaObligatorio => Campo("tipoCarroceria", input.Baseline?.TipoCarroceria, actual.TipoCarroceria, obligatorio: true),
            RuntReinforcement.CampoCombustibleObligatorio => Campo("tipoCombustible", input.Baseline?.TipoCombustible, actual.TipoCombustible, obligatorio: true),
            RuntReinforcement.GarantiaInscrita => Garantia(input, actual),
            _ => RuntConfirmation.Refuerzo.Ninguno,
        };

    private static Refuerzo Campo(string campo, string? antes, string? ahora, bool obligatorio)
    {
        if (antes is null)
            return new Refuerzo($" Refuerzo: sin contraste de campo ({campo}); no hay snapshot de radicación.", Bloquea: false, null);

        var cambio = !string.Equals(RuntText.Normalize(antes), RuntText.Normalize(ahora), StringComparison.Ordinal);
        if (cambio)
            return new Refuerzo($" Refuerzo: {campo} cambió {antes} → {ahora ?? "(vacío)"}.", Bloquea: false, null);

        return obligatorio
            ? new Refuerzo(string.Empty, Bloquea: true, $"{campo} sigue en {antes}: la TRANSFORMACIÓN autorizada puede ser de otro tipo; se reintenta.")
            : new Refuerzo($" Refuerzo: {campo} sigue en {antes}.", Bloquea: false, null);
    }

    private static Refuerzo Garantia(RuntConfirmationInput input, RuntVehicleSnapshot actual)
    {
        var inscrita = actual.Garantias
            .Where(g => g.FechaInscripcion is not null && g.FechaInscripcion >= input.CutoffDate)
            .OrderByDescending(g => g.FechaInscripcion)
            .FirstOrDefault();

        return inscrita is null
            ? new Refuerzo(" Refuerzo: no hay garantía inscrita posterior a la radicación.", Bloquea: false, null)
            : new Refuerzo($" Refuerzo: garantía inscrita el {RuntConfirmationRules.Dia(inscrita.FechaInscripcion)} a favor de {inscrita.Acreedor ?? "acreedor no informado"}.", Bloquea: false, null);
    }

    private static string ContrasteEntidad(string? ot, string? entidad)
    {
        if (string.IsNullOrWhiteSpace(ot) || string.IsNullOrWhiteSpace(entidad))
            return string.Empty;
        var a = RuntText.Normalize(ot);
        var b = RuntText.Normalize(entidad);
        return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)
            ? string.Empty
            : $" La entidad ({entidad}) no coincide con el organismo del trámite ({ot}).";
    }
}

/// <summary>MATRÍCULA: consulta por VIN. El «vehículo no encontrado» es Pendiente: puede que aún no exista en el RUNT.</summary>
internal sealed class MatriculaRule(RuntProcedureEquivalence eq) : SolicitudRule(eq)
{
    public override RuntConfirmationDecision Evaluate(RuntConfirmationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var snapshot = input.Primary;
        if (snapshot is null || snapshot.Outcome == RuntVehicleOutcome.Unreadable)
            return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Error, "La respuesta del proveedor no se pudo interpretar.");

        if (snapshot.Outcome == RuntVehicleOutcome.NotFound)
            return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Pending, "El RUNT aún no tiene el vehículo por VIN; se reintenta en la siguiente corrida.");

        return EvaluarHistorial(input, snapshot, Refuerzo(input, snapshot));
    }

    protected override Refuerzo Refuerzo(RuntConfirmationInput input, RuntVehicleSnapshot actual)
    {
        var partes = new List<string>();
        partes.Add(actual.Placa is null ? "sin placa asignada" : $"placa asignada {actual.Placa}");
        if (actual.EstadoAutomotor is not null)
            partes.Add($"estado {actual.EstadoAutomotor}");
        if (!string.IsNullOrWhiteSpace(input.ExpectedPlate) && actual.Placa is not null &&
            !string.Equals(RuntText.Normalize(input.ExpectedPlate), RuntText.Normalize(actual.Placa), StringComparison.Ordinal))
            partes.Add($"la placa no coincide con la del expediente ({input.ExpectedPlate})");

        return new Refuerzo($" Refuerzo: {string.Join(", ", partes)}.", Bloquea: false, null);
    }
}

/// <summary>
/// TRASPASO: dos consultas por placa, con el documento del vendedor y con el del comprador. Ambos
/// proveedores validan la pareja placa+documento, así que la consulta misma prueba propiedad. El
/// historial se lee de la que respondió (comprador primero); el par solo refuerza y delata anomalías.
/// v1 hacía UNA consulta e invertía el resultado, con lo que un error caía en «confirmado».
/// </summary>
internal sealed class TraspasoRule(RuntProcedureEquivalence eq) : SolicitudRule(eq)
{
    public override RuntConfirmationDecision Evaluate(RuntConfirmationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var comprador = input.Primary;
        var vendedor = input.Seller;

        if (EsIlegible(comprador) && EsIlegible(vendedor))
            return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Error, "Ninguna de las dos respuestas del proveedor se pudo interpretar.");

        var compradorOk = comprador?.Outcome == RuntVehicleOutcome.Found;
        var vendedorOk = vendedor?.Outcome == RuntVehicleOutcome.Found;

        var refuerzo = (compradorOk, vendedorOk) switch
        {
            (true, false) => new Refuerzo(" Refuerzo: propiedad transferida (el comprador responde y el vendedor ya no).", Bloquea: false, null),
            (true, true) => new Refuerzo(" Anomalía: vendedor y comprador responden como propietarios (posible copropiedad o el proveedor no valida el documento).", Bloquea: false, null),
            (false, true) => new Refuerzo(" El vendedor sigue figurando como propietario y el comprador no.", Bloquea: false, null),
            (false, false) => new Refuerzo(" Anomalía: ninguna de las dos consultas devolvió el vehículo (¿cambió de dueño a un tercero o hay error en los documentos del expediente?).", Bloquea: false, null),
        };

        var fuente = compradorOk ? comprador : vendedorOk ? vendedor : null;
        if (fuente is null)
            return RuntConfirmationRules.Decision(RuntConfirmationVerdict.Pending, $"Sin historial de solicitudes que leer.{refuerzo.Sufijo}");

        return EvaluarHistorial(input, fuente, refuerzo);
    }

    private static bool EsIlegible(RuntVehicleSnapshot? s) => s is null || s.Outcome == RuntVehicleOutcome.Unreadable;
}

/// <summary>Resultado de un refuerzo: texto para el motivo y, si es obligatorio y falla, el bloqueo.</summary>
internal sealed record Refuerzo(string Sufijo, bool Bloquea, string? MotivoBloqueo)
{
    public static readonly Refuerzo Ninguno = new(string.Empty, Bloquea: false, null);
}
