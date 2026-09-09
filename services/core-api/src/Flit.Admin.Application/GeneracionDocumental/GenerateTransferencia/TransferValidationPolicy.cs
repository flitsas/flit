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
    public const string RegimenAplicable = "VB-07";              // bloqueante — gate previo (§4.0)

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

    // §6.3 — escenario B (transferencia unilateral de leasing, art. 5.3.2.2).
    public const string TransferenteEsEntidadFinanciera = "VB-B-01";   // bloqueante
    public const string ContratoDeLeasingDeclarado = "VB-B-02";       // bloqueante
    public const string TipoOpcionDeCompraDeclarado = "VB-B-03";      // bloqueante
    public const string LocatarioDestinatarioDeclarado = "VB-B-04";   // bloqueante
    public const string SinPrecioEnEscenarioB = "VB-B-05";            // bloqueante
    public const string CargasFiscalesEnLeasing = "VB-B-06";          // advisory

    // §6.4 — escenario C (entidad financiera a tercero, art. 5.3.2.1 SIN exenciones).
    public const string AdquirenteNoEsElLocatario = "VB-C-01";        // bloqueante
    public const string AdquirenteEnRuntC = "VB-C-02";                // advisory
    public const string SoatVigenteC = "VB-C-03";                     // advisory
    public const string RtmVigenteC = "VB-C-04";                      // advisory
    public const string SinMedidasJudicialesC = "VB-C-05";            // advisory
    public const string QrGuarismosImprontas = "VB-C-06";             // advisory

    /// <summary>
    /// VB-C-07 — se comprueba sobre la PLANTILLA del escenario C, no sobre el formulario: el
    /// documento no puede invocar exenciones del art. 5.3.2.2 porque el tercero no las hereda. Lo
    /// verifica el generador (<c>TransferEscenarioC.VerificarSinExenciones</c>) antes de emitir.
    /// </summary>
    public const string SinExencionesDelArticulo5322 = "VB-C-07";     // bloqueante — de plantilla

    public const string RetencionEnLaFuenteC = "VB-C-08";             // advisory
    public const string DerechosDeTramiteC = "VB-C-09";               // advisory
    public const string ImpuestoVehiculoC = "VB-C-10";                // advisory
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
/// <para><b>VB-07 es un gate previo, no una validación más.</b> El anexo §4.0 lo coloca ANTES de
/// elegir escenario: mientras el usuario no declare que ninguna de las once condiciones especiales
/// de los arts. 5.3.2.3 a 5.3.2.13 aplica, no hay documento. Por eso el silencio bloquea igual que
/// la declaración afirmativa: «no respondió» no es «no aplica».</para>

/// <para><b>Las VB propias de cada escenario se evalúan solo en su escenario.</b> Exigirle a un
/// payload de escenario B el título jurídico del art. 5.3.2.1 sería inventar un requisito que la
/// norma no impone al acto unilateral, y exigirle a A el contrato de leasing, otro tanto.</para>
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
        EvaluateRegimenAplicable(command, blocking);

        var escenario = command.SingleScenario;

        switch (escenario)
        {
            case TransferScenario.TraspasoOrdinario:
                EvaluateTituloYPrecio(command, blocking);
                EvaluateGravamen(command, blocking);
                break;

            case TransferScenario.UnilateralLeasing:
                EvaluateLeasingUnilateral(command, blocking);
                break;

            case TransferScenario.FinancieraATercero:
                EvaluateTituloYPrecio(command, blocking);
                EvaluateGravamen(command, blocking);
                EvaluateTerceroNoEsLocatario(command, blocking);
                break;

            default:
                // Sin escenario único no hay reglas aplicables: VB-05 ya bloqueó y añadir errores de
                // un escenario que el usuario no eligió solo ensuciaría el 422.
                break;
        }

        CollectAdvisories(command, escenario, advisories);

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
    /// VB-07 — <b>gate de régimen aplicable</b> (anexo §4.0, §11 y §13.1). Bloquea en dos casos, y
    /// los dos son deliberados:
    ///
    /// <para>(1) El usuario declaró al menos una de las <b>once</b> condiciones especiales de los
    /// arts. 5.3.2.3 a 5.3.2.13: la operación no se rige por el art. 5.3.2.1 y exige soportes o
    /// exenciones propios de su artículo que este documento no captura, no declara y no puede
    /// acreditar. Se emite un error por condición declarada, <b>citando su artículo</b>: un mensaje
    /// genérico obligaría al usuario a adivinar por cuál de las once se le rechazó.</para>
    ///
    /// <para>(2) El usuario no respondió el control. El anexo lo pone antes de elegir escenario y el
    /// checklist §13.1 exige que la declaración «fue respondida»; tratar el silencio como un «no
    /// aplica» convertiría el gate en una casilla decorativa que el cliente podría omitir.</para>
    ///
    /// <para><b>El art. 5.3.2.14 no participa de esta matriz</b> (expedición de la nueva licencia de
    /// tránsito): es el paso final común a todo traspaso, no una condición especial. No está en
    /// <c>TransferSpecialRegime.All</c> y por eso un código que lo nombre no bloquea por sí mismo.</para>
    /// </summary>
    private static void EvaluateRegimenAplicable(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        var declaracion = command.RegimenAplicable;

        var condiciones = (declaracion?.CondicionesDeclaradas ?? [])
            .Select(TransferSpecialRegime.Find)
            .Where(c => c is not null)
            .Select(c => c!)
            .DistinctBy(c => c.Codigo)
            .ToList();

        if (condiciones.Count > 0)
        {
            foreach (var condicion in condiciones)
            {
                blocking.Add(new TransferValidationIssue(
                    TransferValidationCodes.RegimenAplicable,
                    "regimenAplicable",
                    $"La operación declarada corresponde a un traspaso especial del {condicion.Articulo} "
                    + $"({condicion.Titulo}): ese trámite exige requisitos y soportes adicionales que "
                    + "este módulo no produce ni acredita. Adelántelo por la vía especial de ese "
                    + "artículo, con los soportes propios de la norma."));
            }

            return;
        }

        if (declaracion?.NingunaAplica != true)
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.RegimenAplicable,
                "regimenAplicable",
                "Debe declararse el régimen aplicable a la operación antes de generar: si ninguna de "
                + "las once condiciones especiales de traspaso de los arts. 5.3.2.3 a 5.3.2.13 aplica, "
                + "debe indicarse expresamente."));
        }
    }

    /// <summary>
    /// VB-B-01 a VB-B-05 — escenario B (anexo §6.3). El acto del art. 5.3.2.2 solo puede otorgarlo
    /// una entidad financiera propietaria registrada, sobre un contrato de leasing identificado, con
    /// una causal del catálogo y a favor de un destinatario declarado.
    ///
    /// <para><b>VB-B-05 no es una validación de rango, es una prohibición de campo.</b> El acto es
    /// unilateral y no hay precio entre las partes del instrumento (§10 regla #3): el formulario no
    /// ofrece el campo y, si el cuerpo lo trae de todos modos, se rechaza en vez de ignorarlo —un
    /// precio silenciosamente descartado dejaría al usuario creyendo que quedó en el documento—.</para>
    /// </summary>
    private static void EvaluateLeasingUnilateral(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        var leasing = command.Leasing;

        if (leasing?.TransferenteEsEntidadFinanciera != true)
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.TransferenteEsEntidadFinanciera,
                "leasing.transferenteEsEntidadFinanciera",
                "La transferencia unilateral del art. 5.3.2.2 solo puede otorgarla un establecimiento "
                + "bancario, una compañía de financiamiento o una compañía de leasing: debe declararse "
                + "esa calidad del transferente."));
        }

        if (string.IsNullOrWhiteSpace(leasing?.NoContratoLeasing))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.ContratoDeLeasingDeclarado,
                "leasing.noContratoLeasing",
                "Debe declararse el número del contrato de leasing que soporta la transferencia "
                + "(art. 5.3.2.2 párrafo 1.º)."));
        }

        if (!TransferPurchaseOption.IsKnown(leasing?.TipoOpcionCompra?.Trim().ToUpperInvariant()))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.TipoOpcionDeCompraDeclarado,
                "leasing.tipoOpcionCompra",
                "El tipo de causal debe ser uno del catálogo: opción de compra EJERCIDA, AUTOMATICA o "
                + "TERMINACION_CONTRATO. De él dependen los soportes que exige el art. 5.3.2.2, "
                + "Parágrafo 1.º."));
        }

        if (string.IsNullOrWhiteSpace(leasing?.LocatarioNombre))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.LocatarioDestinatarioDeclarado,
                "leasing.locatarioNombre",
                "Debe declararse el nombre o la razón social del locatario destinatario de la "
                + "transferencia unilateral."));
        }

        if (string.IsNullOrWhiteSpace(leasing?.LocatarioNoDoc))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.LocatarioDestinatarioDeclarado,
                "leasing.locatarioNoDoc",
                "Debe declararse el número de documento del locatario destinatario de la "
                + "transferencia unilateral."));
        }

        // §10 regla #1 — el locatario es el destinatario, jamás el origen del acto unilateral.
        var transferente = Normalize(command.Transferente?.NumeroDoc);
        var locatario = Normalize(leasing?.LocatarioNoDoc);

        if (transferente is not null && transferente == locatario)
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.LocatarioDestinatarioDeclarado,
                "leasing.locatarioNoDoc",
                "El locatario destinatario no puede ser la misma entidad transferente: en el "
                + "art. 5.3.2.2 el locatario recibe el dominio, no lo transfiere."));
        }

        EvaluatePrecioProhibido(command, blocking);
    }

    /// <summary>VB-B-05 — ningún campo de precio ni de contraprestación viaja en el escenario B.</summary>
    private static void EvaluatePrecioProhibido(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        (string Campo, string? Valor)[] prohibidos =
        [
            ("negocio.precioLetras", command.Negocio?.PrecioLetras),
            ("negocio.precioNumeros", command.Negocio?.PrecioNumeros),
            ("negocio.contraprestacionDescripcion", command.Negocio?.ContraprestacionDescripcion),
        ];

        foreach (var (campo, _) in prohibidos.Where(p => !string.IsNullOrWhiteSpace(p.Valor)))
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.SinPrecioEnEscenarioB,
                campo,
                "La transferencia unilateral del art. 5.3.2.2 es un acto unilateral y no declara "
                + "precio ni contraprestación entre las partes del instrumento: este campo no "
                + "corresponde al escenario B."));
        }
    }

    /// <summary>
    /// VB-C-01 — el adquirente del escenario C no puede ser el locatario histórico. Si lo es, la
    /// operación es la del art. 5.3.2.2 y debe reclasificarse al escenario B (anexo §11): emitirla
    /// como C le negaría al locatario las exenciones que la norma sí le concede.
    ///
    /// <para>Solo se comprueba cuando el documento del locatario histórico se declaró: FLIT no
    /// conoce el contrato de leasing y no puede inventarse la comparación.</para>
    /// </summary>
    private static void EvaluateTerceroNoEsLocatario(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> blocking)
    {
        var locatario = Normalize(command.Leasing?.LocatarioNoDoc);
        var adquirente = Normalize(command.Adquirente?.NumeroDoc);

        if (locatario is not null && locatario == adquirente)
        {
            blocking.Add(new TransferValidationIssue(
                TransferValidationCodes.AdquirenteNoEsElLocatario,
                "adquirente.numeroDoc",
                "El adquirente coincide con el locatario histórico declarado: la operación es la "
                + "transferencia unilateral del art. 5.3.2.2 y debe emitirse como escenario B."));
        }
    }

    /// <summary>
    /// Prevalidaciones VA (§6.1, §6.2, §6.3 y §6.4). Se emiten SIEMPRE: este módulo no tiene
    /// interoperabilidad en vivo con RUNT, RUES, SOAT ni SIMIT para el documento de transferencia,
    /// así que el estado honesto de todas es «pendiente de verificación por el Organismo de
    /// Tránsito».
    ///
    /// <para><b>Los avisos son los del escenario elegido.</b> El escenario B no lleva los de RTM ni
    /// QR/improntas —el art. 5.3.2.2 los exime— pero sí los fiscales (VB-B-06): sus cinco exenciones
    /// son RTM, QR/improntas, paz y salvo, presentación del locatario y firma del FUR, y ninguna
    /// toca el numeral 5.º del art. 5.3.2.1. El escenario C no hereda ninguna exención.</para>
    /// </summary>
    private static void CollectAdvisories(
        GenerateTransferenciaCommand command,
        string? escenario,
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

        switch (escenario)
        {
            case TransferScenario.TraspasoOrdinario:
                CollectAdvisoriesEscenarioA(command, advisories);
                break;

            case TransferScenario.UnilateralLeasing:
                CollectAdvisoriesEscenarioB(advisories);
                break;

            case TransferScenario.FinancieraATercero:
                CollectAdvisoriesEscenarioC(command, advisories);
                break;

            default:
                break;
        }
    }

    private static void CollectAdvisoriesEscenarioA(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> advisories)
    {
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
            SoatMensaje(command)));

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

    /// <summary>
    /// VB-B-06 — el hallazgo del dictamen: <b>el art. 5.3.2.2 NO exime lo fiscal</b>. Sus cinco
    /// exenciones son RTM, QR/improntas, paz y salvo, presentación del locatario y firma del FUR; el
    /// numeral 5.º del art. 5.3.2.1 —retención en la fuente, impuesto y derechos del trámite— sigue
    /// verificándose. Por eso el escenario B emite este aviso y no una lista vacía.
    /// </summary>
    private static void CollectAdvisoriesEscenarioB(List<TransferValidationIssue> advisories)
    {
        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.CargasFiscalesEnLeasing,
            "vehiculo.placa",
            "El art. 5.3.2.2 no exime el numeral 5.º del art. 5.3.2.1: el Organismo de Tránsito "
            + "verifica el pago de la retención en la fuente, del impuesto sobre vehículos y de los "
            + "derechos del trámite. Sus cinco exenciones son revisión técnico-mecánica, "
            + "QR/certificación/improntas, paz y salvo de infracciones, presentación del locatario y "
            + "firma del Formato Único."));
    }

    private static void CollectAdvisoriesEscenarioC(
        GenerateTransferenciaCommand command,
        List<TransferValidationIssue> advisories)
    {
        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.AdquirenteEnRuntC,
            "adquirente.numeroDoc",
            "La inscripción del adquirente en el RUNT la verifica el Organismo de Tránsito."));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.SoatVigenteC,
            "vehiculo.placa",
            SoatMensaje(command)));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.RtmVigenteC,
            "vehiculo.placa",
            "La revisión técnico-mecánica debe estar vigente para el tipo de vehículo: el adquirente "
            + "no es el locatario y NO hereda la exención del art. 5.3.2.2. La verifica el Organismo "
            + "de Tránsito."));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.SinMedidasJudicialesC,
            "vehiculo.placa",
            "La ausencia de medidas judiciales que impidan el traspaso la verifica el Organismo de "
            + "Tránsito en el RUNT (art. 5.3.2.1 numeral 3.º)."));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.QrGuarismosImprontas,
            "vehiculo.noChasis",
            "La imagen del código QR, la certificación de guarismos o las improntas se exigen sin "
            + "exención (art. 5.3.2.1 numeral 1.º): las verifica el Organismo de Tránsito."));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.RetencionEnLaFuenteC,
            "negocio.asumeRetencionFuente",
            "El pago de la retención en la fuente por la enajenación lo acredita el interesado ante el "
            + "Organismo de Tránsito con copia de los recibos; FLIT no lo liquida ni lo verifica "
            + "(art. 5.3.2.1 numeral 5.º)."));

        advisories.Add(new TransferValidationIssue(
            TransferValidationCodes.DerechosDeTramiteC,
            "negocio.asumeDerechosTramite",
            "El pago de los derechos del trámite —Ministerio de Transporte, tarifa RUNT y derechos del "
            + "Organismo de Tránsito— lo valida el Organismo de Tránsito en el RUNT "
            + "(art. 5.3.2.1 numeral 5.º)."));

        if (!EsRemolque(command.Vehiculo?.ClaseVehiculo))
        {
            advisories.Add(new TransferValidationIssue(
                TransferValidationCodes.ImpuestoVehiculoC,
                "negocio.asumeImpuestoVehiculo",
                "El pago del impuesto sobre vehículos automotores lo verifica el Organismo de Tránsito "
                + "ante la entidad territorial (art. 5.3.2.1 numeral 5.º)."));
        }
    }

    /// <summary>
    /// Mensaje del aviso de SOAT. En remolques y semirremolques la no exigibilidad la resuelve el OT
    /// y <b>no</b> se invoca la Ley 488/1998, que en el art. 5.3.2.1 exime impuesto y no SOAT.
    /// </summary>
    private static string SoatMensaje(GenerateTransferenciaCommand command) =>
        EsRemolque(command.Vehiculo?.ClaseVehiculo)
            ? "En remolques y semirremolques la exigibilidad del SOAT la resuelve el Organismo de "
              + "Tránsito: el art. 5.3.2.1 no consagra exención textual."
            : "La vigencia del SOAT la verifica el Organismo de Tránsito.";

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
