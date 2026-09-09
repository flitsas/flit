using System.Text.RegularExpressions;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;

/// <summary>
/// Un hallazgo de validación. <b>Código, campo y mensaje; NUNCA el valor capturado</b> (CF-09 y
/// requisito de PII): reflejar «la placa ABC123 es inválida» en un 422 pone un dato del ciudadano en
/// logs de acceso, trazas de error y capturas de pantalla de soporte.
/// </summary>
public sealed record TransferValidationIssue(string Code, string Field, string Message);

/// <summary>
/// Resultado de la política: lo que BLOQUEA y lo que solo AVISA. Son dos listas y no una con
/// severidad porque el handler trata cada una de forma distinta —una corta la generación, la otra
/// viaja en el 200— y una severidad mal leída convertiría un aviso en un bloqueo.
/// </summary>
public sealed record TransferValidationOutcome(
    IReadOnlyList<TransferValidationIssue> Blocking,
    IReadOnlyList<TransferValidationIssue> Advisories)
{
    public bool IsBlocked => Blocking.Count > 0;
}

/// <summary>Códigos del anexo normativo §6. No se inventan códigos fuera de esa tabla.</summary>
public static class TransferValidationCodes
{
    // §6.1 — comunes a los tres escenarios.
    public const string MatriculaVigente = "VB-01";              // advisory
    public const string PlacaFormato = "VB-02";                  // bloqueante
    public const string TransferenteEnRunt = "VB-03";            // advisory
    public const string TransferentePjEnRues = "VB-04";          // advisory
    public const string EscenarioUnico = "VB-05";                // bloqueante
    public const string PartesDistintas = "VB-06";               // bloqueante
    public const string RegimenAplicable = "VB-07";              // bloqueante — HU-06, NO en esta HU

    // §6.2 — escenario A.
    public const string AdquirenteEnRunt = "VB-A-01";            // advisory
    public const string AdquirentePjEnRues = "VB-A-02";          // advisory
    public const string SinMedidasJudiciales = "VB-A-03";        // advisory
    public const string GravamenConLevantamiento = "VB-A-04";    // bloqueante
    public const string SoatVigente = "VB-A-05";                 // advisory
    public const string PrecioCompraventa = "VB-A-06";           // bloqueante
    public const string TituloJuridicoDeclarado = "VB-A-07";     // bloqueante
    public const string RetencionEnLaFuente = "VB-A-08";         // advisory
    public const string DerechosDeTramite = "VB-A-09";           // advisory
    public const string ImpuestoVehiculo = "VB-A-10";            // advisory
}

/// <summary>
/// Validaciones del anexo normativo §6 para el escenario A y las comunes a los tres
/// (<c>docs/plantilla-transferencia-dominio.md</c>).
///
/// <para><b>Dos naturalezas, no dos severidades de la misma cosa.</b> Las <b>VB bloqueantes</b> son
/// las que FLIT puede comprobar con lo que hay en el formulario: formato de placa, unicidad del
/// escenario, partes distintas, título declarado, precio de la compraventa y coherencia del
/// gravamen declarado. Las <b>VA advisory</b> dependen de RUNT, RUES, SOAT o SIMIT: FLIT no puede
/// confirmarlas y quien las verifica es el Organismo de Tránsito al recibir el trámite, así que se
/// emiten SIEMPRE como aviso y jamás impiden generar (§6, párrafo introductorio).</para>
///
/// <para><b>VB-07 no está aquí.</b> El control de régimen aplicable con las once condiciones de los
/// arts. 5.3.2.3 a 5.3.2.13 es alcance de HU-06; el código se declara arriba para que HU-06 lo
/// añada a esta misma política sin renombrar nada.</para>
/// </summary>
public static class TransferValidationPolicy
{
    /// <summary>
    /// Placa: 5 a 7 alfanuméricos tras quitar guiones, puntos y espacios. No se codifica el patrón
    /// <c>AAA000</c> de automóvil porque dejaría fuera remolques y semirremolques —que también se
    /// traspasan por el art. 5.3.2.1— y motocicletas <c>AAA00A</c>.
    /// </summary>
    private static readonly Regex PlacaPattern = new(
        "^[A-Z0-9]{5,7}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static TransferValidationOutcome Evaluate(GenerateTransferenciaCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var blocking = new List<TransferValidationIssue>();
        var advisories = new List<TransferValidationIssue>();

        EvaluateEscenario(command, blocking);
        EvaluatePlaca(command, blocking);
        EvaluatePartes(command, blocking);
        EvaluateTituloYPrecio(command, blocking);
        EvaluateGravamen(command, blocking);
        CollectAdvisories(command, advisories);

        return new TransferValidationOutcome(blocking, advisories);
    }

    /// <summary>
    /// VB-05 — el escenario es obligatorio y ÚNICO. Cero escenarios y tres escenarios fallan con el
    /// mismo código: en ambos casos el generador no puede determinar qué reglas aplicar (§11).
    /// </summary>
    private static void EvaluateEscenario(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        var declarados = command.Scenarios?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];

        if (declarados.Count != 1 || !TransferScenario.IsKnown(declarados[0]))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.EscenarioUnico,
                "escenario",
                "Debe seleccionarse exactamente un escenario de transferencia (A, B o C)."));
        }
    }

    /// <summary>VB-02 — formato de placa verificable en el formulario (art. 5.1.8).</summary>
    private static void EvaluatePlaca(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        var placa = Normalize(command.Vehiculo?.Placa);

        if (placa is null || !PlacaPattern.IsMatch(placa))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.PlacaFormato,
                "vehiculo.placa",
                "La placa no tiene un formato válido: se esperan entre 5 y 7 caracteres alfanuméricos."));
        }
    }

    /// <summary>
    /// VB-06 — transferente y adquirente no pueden compartir número de documento: nadie se
    /// transfiere el dominio a sí mismo (§10 regla #2).
    /// </summary>
    private static void EvaluatePartes(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        var transferente = Normalize(command.Transferente?.NumeroDoc);
        var adquirente = Normalize(command.Adquirente?.NumeroDoc);

        if (transferente is not null && transferente == adquirente)
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.PartesDistintas,
                "adquirente.numeroDoc",
                "El transferente y el adquirente no pueden tener el mismo número de documento."));
        }
    }

    /// <summary>
    /// VB-A-07 y VB-A-06 — título jurídico declarado y, si es COMPRAVENTA, precio en letras Y en
    /// números. Un título fuera del catálogo se trata como «ambiguo» y cae también en VB-A-07: el
    /// art. 5.3.2.1 num. 1.º exige que del documento CONSTE la transferencia, y un título que el
    /// generador no sabe redactar no consta.
    /// </summary>
    private static void EvaluateTituloYPrecio(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        var titulo = command.Negocio?.TituloJuridico?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(titulo) || !TransferJuridicalTitle.IsKnown(titulo))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.TituloJuridicoDeclarado,
                "negocio.tituloJuridico",
                "El título jurídico del negocio traslaticio es obligatorio y debe corresponder al catálogo."));
            return;
        }

        // OTRO sin descripción es igual de ambiguo que no declarar el título (anexo §5.4).
        if (titulo == TransferJuridicalTitle.Otro
            && string.IsNullOrWhiteSpace(command.Negocio?.DescripcionTitulo))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.TituloJuridicoDeclarado,
                "negocio.descripcionTitulo",
                "Con título jurídico OTRO debe describirse el negocio traslaticio."));
        }

        if (!TransferJuridicalTitle.RequiresPrice(titulo))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(command.Negocio?.PrecioLetras))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.PrecioCompraventa,
                "negocio.precioLetras",
                "En una compraventa el precio debe declararse en letras."));
        }

        if (string.IsNullOrWhiteSpace(command.Negocio?.PrecioNumeros))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.PrecioCompraventa,
                "negocio.precioNumeros",
                "En una compraventa el precio debe declararse en números."));
        }
    }

    /// <summary>
    /// VB-A-04 — gravamen activo declarado sin levantamiento ni autorización del beneficiario. La
    /// declaración es del usuario; el OT la verifica (art. 5.3.2.1 numeral 3.º).
    /// </summary>
    private static void EvaluateGravamen(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        if (command.Gravamen is { GravamenActivo: true, TieneLevantamientoOAutorizacion: false })
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.GravamenConLevantamiento,
                "gravamen.tieneLevantamientoOAutorizacion",
                "Con gravamen activo debe declararse el levantamiento previo o la autorización del "
                + "beneficiario para continuar con el nuevo propietario (art. 5.3.2.1 numeral 3.º)."));
        }
    }

    /// <summary>
    /// Prevalidaciones VA (§6.1 y §6.2). Se emiten SIEMPRE: este módulo no tiene interoperabilidad
    /// en vivo con RUNT, RUES, SOAT ni SIMIT para el documento de transferencia, así que el estado
    /// honesto de todas es «pendiente de verificación por el Organismo de Tránsito».
    /// </summary>
    private static void CollectAdvisories(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> advisories)
    {
        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.MatriculaVigente,
            "vehiculo.placa",
            "La vigencia de la matrícula en el RUNT la verifica el Organismo de Tránsito."));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.TransferenteEnRunt,
            "transferente.numeroDoc",
            "La inscripción del transferente en el RUNT la verifica el Organismo de Tránsito."));

        if (EsPersonaJuridica(command.Transferente))
        {
            advisories.Add(new TransferValidationIssue(
                TransferValidationCodes.TransferentePjEnRues,
                "transferente.numeroDoc",
                "La inscripción del transferente en el RUES la consulta directamente el Organismo de "
                + "Tránsito; no se exige certificado físico (art. 5.1.5)."));
        }

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.AdquirenteEnRunt,
            "adquirente.numeroDoc",
            "La inscripción del adquirente en el RUNT la verifica el Organismo de Tránsito."));

        if (EsPersonaJuridica(command.Adquirente))
        {
            advisories.Add(new TransferValidationIssue(
                TransferValidationCodes.AdquirentePjEnRues,
                "adquirente.numeroDoc",
                "La inscripción del adquirente en el RUES la consulta directamente el Organismo de "
                + "Tránsito; no se exige certificado físico (art. 5.1.5)."));
        }

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.SinMedidasJudiciales,
            "vehiculo.placa",
            "La ausencia de medidas judiciales que impidan el traspaso la verifica el Organismo de "
            + "Tránsito en el RUNT (art. 5.3.2.1 numeral 3.º)."));

        // VB-A-05 — para remolques y semirremolques el art. 5.3.2.1 NO consagra exención textual de
        // SOAT: su no exigibilidad la resuelve el OT. NO se cita la Ley 488/1998 aquí, que exime
        // impuesto y no SOAT en este artículo (anexo §4.1 y §6.2).
        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.SoatVigente,
            "vehiculo.placa",
            EsRemolque(command.Vehiculo?.ClaseVehiculo)
                ? "En remolques y semirremolques la exigibilidad del SOAT la resuelve el Organismo de "
                  + "Tránsito: el art. 5.3.2.1 no consagra exención textual."
                : "La vigencia del SOAT la verifica el Organismo de Tránsito."));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.RetencionEnLaFuente,
            "negocio.asumeRetencionFuente",
            "El pago de la retención en la fuente por la enajenación lo acredita el interesado ante el "
            + "Organismo de Tránsito con copia de los recibos; FLIT no lo liquida ni lo verifica "
            + "(art. 5.3.2.1 numeral 5.º)."));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.DerechosDeTramite,
            "negocio.asumeDerechosTramite",
            "El pago de los derechos del trámite —Ministerio de Transporte, tarifa RUNT y derechos del "
            + "Organismo de Tránsito— lo valida el Organismo de Tránsito en el RUNT "
            + "(art. 5.3.2.1 numeral 5.º)."));

        // VB-A-10 no aplica a remolques ni semirremolques: están exentos del impuesto sobre
        // vehículos (Ley 488/1998, art. 5.3.2.1 num. 5.º inciso final).
        if (!EsRemolque(command.Vehiculo?.ClaseVehiculo))
        {
            advisories.Add(new TransferValidationIssue(
                TransferValidationCodes.ImpuestoVehiculo,
                "negocio.asumeImpuestoVehiculo",
                "El pago del impuesto sobre vehículos automotores lo verifica el Organismo de Tránsito "
                + "ante la entidad territorial (art. 5.3.2.1 numeral 5.º)."));
        }
    }

    /// <summary>Remolque o semirremolque por la clase declarada. «SEMIRREMOLQUE» contiene «REMOLQUE».</summary>
    public static bool EsRemolque(string? claseVehiculo) =>
        claseVehiculo?.Contains("REMOLQUE", StringComparison.OrdinalIgnoreCase) == true;

    private static bool EsPersonaJuridica(TransferPartyInput? parte) =>
        string.Equals(parte?.TipoPersona, "PJ", StringComparison.OrdinalIgnoreCase)
        || string.Equals(parte?.TipoDoc, "NIT", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Valor comparable: sin espacios, puntos ni guiones y en mayúsculas. Para VB-06 esto es
    /// sustantivo — <c>900.123.456</c> y <c>900123456</c> son la misma persona, y una comparación
    /// literal dejaría pasar la auto-transferencia con solo teclear un punto.
    /// </summary>
    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string([.. value.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '.')]);
        return cleaned.Length == 0 ? null : cleaned.ToUpperInvariant();
    }
}
