using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10991 (Feature #10972) — <b>guardia de contrato entre los documentos del expediente y
/// <c>field_values</c></b>.
///
/// <para><b>Por qué existe.</b> El Feature nació de OCHO llaves que los generadores leían y que
/// ningún productor escribía: el certificado de vigencia SOAT y RTM salía con 12 de 13 celdas en
/// blanco, y llevaba meses así sin que nadie lo notara. El barrido posterior encontró DOS más en el
/// propio FUR. Nada en el build lo detectaba, porque leer una llave inexistente no es un error de
/// compilación ni de ejecución: simplemente devuelve null y el PDF sale vacío.</para>
///
/// <para><b>Cómo funciona.</b> Se lee el código fuente de <c>FurCommand</c> — el punto único donde se
/// ensamblan los datos de TODOS los documentos del expediente — se extraen las llaves que consume, y
/// se exige que cada una tenga un productor <b>declarado</b> en <see cref="Productores"/>. Además se
/// verifica que ese productor realmente escriba la llave, abriendo su fichero.</para>
///
/// <para><b>Por qué declarativo y no inferido.</b> Durante el barrido, <c>vehicle_color_runt</c>
/// pareció huérfana en una búsqueda por literal, pero sí tiene productor: <c>PreflightCommand</c> la
/// escribe como <c>field.FieldKey + RuntSnapshotSuffix</c>. Un grep habría dado un falso positivo
/// ahí y, en el caso simétrico, un falso negativo. Por eso el registro se declara a mano y admite
/// productores dinámicos con su marcador de construcción.</para>
///
/// <para><b>Límite honesto.</b> La guardia obliga a DECLARAR el productor de cada llave nueva y
/// comprueba que el fichero declarado la mencione; no puede demostrar que la escritura ocurra en
/// tiempo de ejecución. Su valor es que añadir un consumidor sin productor deja de ser silencioso y
/// pasa a ser una decisión explícita y revisable.</para>
/// </summary>
public sealed class FieldValueContractGuardTests
{
    /// <summary>Cómo produce la llave el fichero declarado.</summary>
    private enum Modo
    {
        /// <summary>El fichero contiene la llave como literal.</summary>
        Literal,

        /// <summary>La llave se construye en ejecución; se verifica el marcador documentado.</summary>
        Dinamico,
    }

    /// <summary>
    /// Quién escribe una llave. <paramref name="Ficheros"/> admite VARIOS porque una llave puede tener
    /// productor primario y respaldo — el caso real: el RUNT entrega la póliza del SOAT y el OCR del PDF
    /// la aporta cuando el proveedor no la trae (HU #11134). Declarar solo uno escondía media verdad, y
    /// el valor de este registro está en que describa la realidad completa.
    /// </summary>
    private sealed record Productor(string[] Ficheros, Modo Modo, string? Marcador = null)
    {
        public Productor(string fichero, Modo modo, string? marcador = null)
            : this([fichero], modo, marcador) { }
    }

    // ── Rutas relativas a la raíz del repositorio ────────────────────────────
    private const string Src = "services/core-api/src/";
    private const string App = Src + "Flit.Tramites.Application/";
    private const string Infra = Src + "Flit.Infrastructure/";

    private const string Verifik = App + "UseCases/Consultations/VerifikResultMapper.cs";
    private const string Kyverum = App + "UseCases/Consultations/KyverumRuntVehicleResultMapper.cs";
    private const string RunConsulta = App + "UseCases/Consultations/RunConsultationCommand.cs";
    private const string Ocr = App + "UseCases/ProcedureInstances/OcrFieldsCommand.cs";
    private const string Preflight = App + "UseCases/ProcedureInstances/PreflightCommand.cs";
    private const string RuesProvider = Infra + "Consultations/VerifikRuesConsultationProvider.cs";
    private const string RuesLookup = App + "UseCases/Consultations/RuesPersonLookupHandler.cs";
    private const string WizardFur = "frontend/components/operacion/FirmaFurStep.tsx";
    /// <summary>P6 del wizard — observaciones del trámite (antes vivían en FirmaFurStep).</summary>
    private const string WizardTramite = "frontend/components/operacion/TramiteWizard.tsx";

    /// <summary>Fichero que se lee para extraer las llaves CONSUMIDAS por los documentos.</summary>
    private const string Consumidor = App + "UseCases/ProcedureInstances/FurCommand.cs";

    /// <summary>
    /// Registro declarativo: llave de <c>field_values</c> → quién la escribe.
    /// <b>Si añades un consumidor nuevo en FurCommand, debes declarar aquí su productor.</b>
    /// </summary>
    private static readonly Dictionary<string, Productor> Productores = new(StringComparer.Ordinal)
    {
        // Vehículo — hidratado por los mappers de la consulta RUNT.
        ["plate"] = new(Verifik, Modo.Literal),
        ["vin"] = new(Verifik, Modo.Literal),
        ["vehicle_brand"] = new(Verifik, Modo.Literal),
        ["vehicle_line"] = new(Verifik, Modo.Literal),
        ["vehicle_year"] = new(Verifik, Modo.Literal),
        ["vehicle_color"] = new(Verifik, Modo.Literal),
        ["vehicle_class"] = new(Verifik, Modo.Literal),
        ["vehicle_fuel"] = new(Verifik, Modo.Literal),
        ["vehicle_engine_displacement"] = new(Verifik, Modo.Literal),
        ["vehicle_body_type"] = new(Verifik, Modo.Literal),
        ["vehicle_chassis"] = new(Verifik, Modo.Literal),
        ["vehicle_engine_number"] = new(Verifik, Modo.Literal),
        ["vehicle_series"] = new(Verifik, Modo.Literal),
        ["vehicle_service"] = new(Verifik, Modo.Literal),
        ["vehicle_passengers"] = new(Verifik, Modo.Literal),
        ["vehicle_weight"] = new(Verifik, Modo.Literal),
        ["vehicle_axles"] = new(Verifik, Modo.Literal),
        ["transit_office_name"] = new(Verifik, Modo.Literal),

        // Snapshot RUNT de las transformaciones (ADR-0029): llave CONSTRUIDA en ejecución como
        // `field.FieldKey + RuntSnapshotSuffix`. Es el caso que un grep reportaría como huérfano.
        ["vehicle_color_runt"] = new(Preflight, Modo.Dinamico, "RuntSnapshotSuffix"),
        ["vehicle_fuel_runt"] = new(Preflight, Modo.Dinamico, "RuntSnapshotSuffix"),
        // HU #10673 (A4/B4) — carrocería se suma al mismo patrón de transformación.
        ["vehicle_body_type_runt"] = new(Preflight, Modo.Dinamico, "RuntSnapshotSuffix"),

        // HU #11136 — fecha de matrícula del vehículo: insumo de la regla de antigüedad de la RTM.
        // OJO: solo la reporta Verifik. Las respuestas capturadas de Kyverum no traen fecha alguna de
        // matrícula, así que con ese proveedor la regla cae al lado seguro (mostrar la tabla).
        ["vehicle_registration_date"] = new(Verifik, Modo.Literal),

        // SOAT y RTM — parte del RUNT, parte del OCR del documento (HU #10975/#10976/#10977).
        ["soat_vencimiento"] = new(Verifik, Modo.Literal),
        ["soat_aseguradora"] = new(Verifik, Modo.Literal),
        ["soat_estado"] = new(Kyverum, Modo.Dinamico, "SoatGate.FieldKey"),
        ["rtm_vencimiento"] = new(Verifik, Modo.Literal),
        ["rtm_estado"] = new(Verifik, Modo.Literal),
        ["rtm_entidad"] = new(Verifik, Modo.Literal),
        // HU #11134 / #11135 — doble productor: el RUNT es el primario y el OCR del PDF el respaldo
        // (PersistOcrFieldsHandler nunca pisa un valor de consulta). Ambos deben seguir existiendo.
        ["soat_poliza"] = new([Verifik, Ocr], Modo.Literal),
        ["soat_vigencia"] = new([Verifik, Ocr], Modo.Literal),
        ["soat_expedicion"] = new([Verifik, Ocr], Modo.Literal),
        ["rtm_numero"] = new([Verifik, Ocr], Modo.Literal),
        ["rtm_vigencia"] = new([Verifik, Ocr], Modo.Literal),
        ["rtm_expedicion"] = new([Verifik, Ocr], Modo.Literal),

        // Fecha de la consulta al RUNT (HU #10974): dato de la EJECUCIÓN, no de la respuesta.
        ["runt_consulta_fecha"] = new(RunConsulta, Modo.Literal),

        // Organismo de tránsito y campos del FUR — los escribe el wizard vía PatchFieldValues.
        ["transit_office_code"] = new(WizardFur, Modo.Literal),
        ["transit_office_city"] = new(WizardFur, Modo.Literal),
        ["fur_observations"] = new(WizardTramite, Modo.Literal), // HU #10987 — productor en P6 del wizard
        ["fur_processing_date"] = new(WizardFur, Modo.Literal), // HU #10988

        // RUES — provider de consulta; el resolutor por actor (HU #10990) devuelve estas mismas llaves.
        ["rues_nit"] = new(RuesProvider, Modo.Literal),
        ["rues_razon_social"] = new(RuesProvider, Modo.Literal),
        ["rues_estado"] = new(RuesProvider, Modo.Literal),
        ["rues_matricula_mercantil"] = new(RuesProvider, Modo.Literal),
        ["rues_camara_comercio"] = new(RuesProvider, Modo.Literal),
        ["rues_sigla"] = new(RuesProvider, Modo.Literal),
        ["rues_fecha_matricula"] = new(RuesProvider, Modo.Literal),
        ["rues_ultimo_ano_renovado"] = new(RuesProvider, Modo.Literal),
        ["rues_fecha_renovacion"] = new(RuesProvider, Modo.Literal),
        ["rues_direccion"] = new(RuesProvider, Modo.Literal),
        ["rues_municipio"] = new(RuesProvider, Modo.Literal),
        ["rues_categoria"] = new(RuesProvider, Modo.Literal),
        ["rues_actividad_economica"] = new(RuesProvider, Modo.Literal),
        ["rues_tipo_organizacion"] = new(RuesProvider, Modo.Literal),
        ["rues_tipo_compania"] = new(RuesProvider, Modo.Literal),
        ["rues_email"] = new(RuesProvider, Modo.Literal),
        ["rues_id_rm"] = new(RuesProvider, Modo.Literal),
        ["rues_fecha_actualizacion"] = new(RuesProvider, Modo.Literal),
        ["rues_razon_cancelacion"] = new(RuesProvider, Modo.Literal),
        ["rues_representacion_legal"] = new(RuesProvider, Modo.Literal),
        ["rues_actividades_json"] = new(RuesProvider, Modo.Literal),
        // HU #11132 — jurisdicción de la cámara de comercio.
        ["rues_camara_ciudad"] = new(RuesProvider, Modo.Literal),
        ["rues_camara_departamento"] = new(RuesProvider, Modo.Literal),
        // HU #11133 — snapshot congelado por NIT; lo escribe el lookup del asistente a través de la
        // constante RuesSnapshots.FieldKey, no de un literal.
        ["rues_snapshots_json"] = new(RuesLookup, Modo.Dinamico, "RuesSnapshots.FieldKey"),
    };

    // ── Infraestructura de la guardia ────────────────────────────────────────

    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "services", "core-api", "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("No se encontró la raíz del repositorio desde el directorio de test.");
    }

    private static string Leer(string rutaRelativa)
    {
        var full = Path.Combine(RepoRoot, rutaRelativa.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(full).Should().BeTrue($"el productor declarado '{rutaRelativa}' debe existir");
        return File.ReadAllText(full);
    }

    /// <summary>
    /// Llaves consumidas a través de una CONSTANTE en vez de un literal: el regex no puede verlas.
    /// Se declaran con la expresión que debe aparecer en <c>FurCommand</c>, de modo que si alguien
    /// renombra la constante la guardia lo note en vez de dar la llave por no consumida.
    /// </summary>
    private static readonly Dictionary<string, string> ConsumidasPorConstante = new(StringComparer.Ordinal)
    {
        // FurCommand lee el estado del SOAT por la constante del gate, no por el literal, porque esa
        // llave es a la vez dato del certificado y gate de aprobación del OT (HU #10973).
        ["soat_estado"] = "Get(fv, SoatGate.FieldKey)",
        // HU #11133 — el snapshot del RUES se lee por la constante que lo define, no por el literal.
        ["rues_snapshots_json"] = "Get(fv, RuesSnapshots.FieldKey)",
        // HU #11136 — la fecha de matrícula alimenta la regla de antigüedad de la RTM, y se lee por la
        // constante del servicio de dominio que la define.
        ["vehicle_registration_date"] = "Get(fv, RtmCertificado.FieldKeyFechaMatricula)",
    };

    /// <summary>
    /// Llaves que los documentos CONSUMEN: los literales de <c>Get(fv, "…")</c> (datos del trámite) y
    /// de <c>Val(datos, "…")</c> (datos del RUES resueltos por actor) en <c>FurCommand</c>, más las
    /// declaradas en <see cref="ConsumidasPorConstante"/>.
    /// </summary>
    private static HashSet<string> LlavesConsumidas()
    {
        var fuente = Leer(Consumidor);
        var regex = new Regex("(?:Get\\(fv|Val\\(datos),\\s*\"([a-z0-9_]+)\"", RegexOptions.CultureInvariant);
        var llaves = regex.Matches(fuente).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        foreach (var (llave, expresion) in ConsumidasPorConstante)
        {
            fuente.Should().Contain(expresion,
                "la llave '{0}' se declara consumida por constante; si la expresión cambió, actualiza el registro", llave);
            llaves.Add(llave);
        }

        return llaves;
    }

    /// <summary>
    /// ¿El fichero del productor menciona la llave como literal? Acepta comillas dobles (C#) y simples
    /// (TypeScript): parte de los productores son del wizard, no del backend.
    /// </summary>
    private static bool ContieneLiteral(string fuente, string llave) =>
        fuente.Contains($"\"{llave}\"", StringComparison.Ordinal)
        || fuente.Contains($"'{llave}'", StringComparison.Ordinal);

    // ── La guardia ───────────────────────────────────────────────────────────

    [Fact]
    public void NingunDocumentoLeeUnaLlaveSinProductorDeclarado()
    {
        var huerfanas = LlavesConsumidas().Where(k => !Productores.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        huerfanas.Should().BeEmpty(
            "los documentos del expediente leen estas llaves de field_values y NINGÚN productor las "
            + "escribe, así que saldrían en blanco: {0}. Declara su productor en FieldValueContractGuardTests.Productores "
            + "o retira la lectura muerta.",
            string.Join(", ", huerfanas));
    }

    [Fact]
    public void CadaProductorDeclaradoEscribeRealmenteSuLlave()
    {
        var incumplen = new List<string>();

        foreach (var (llave, productor) in Productores)
        {
            foreach (var fichero in productor.Ficheros)
            {
                var fuente = Leer(fichero);
                var ok = productor.Modo == Modo.Literal
                    ? ContieneLiteral(fuente, llave)
                    : fuente.Contains(productor.Marcador!, StringComparison.Ordinal);

                if (!ok)
                {
                    var esperado = productor.Modo == Modo.Literal ? $"el literal {llave}" : $"el marcador '{productor.Marcador}'";
                    incumplen.Add($"{llave} → {fichero} (se esperaba {esperado})");
                }
            }
        }

        incumplen.Should().BeEmpty(
            "el registro declara productores que ya no escriben su llave: {0}",
            string.Join(" | ", incumplen));
    }

    [Fact]
    public void ElRegistroNoAcumulaDeclaracionesObsoletas()
    {
        // Evita que el registro se pudra: una llave declarada que ya nadie consume es ruido que
        // esconde el siguiente hueco real.
        var consumidas = LlavesConsumidas();
        var sobrantes = Productores.Keys.Where(k => !consumidas.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        sobrantes.Should().BeEmpty(
            "estas llaves están declaradas en el registro pero ningún documento las consume ya: {0}. "
            + "Retíralas del registro.",
            string.Join(", ", sobrantes));
    }

    [Fact]
    public void LaGuardiaDetectaLasDosHuerfanasQueOriginaronElBarrido()
    {
        // Caso de prueba de la propia guardia: si se retirara el productor de fur_observations o de
        // fur_processing_date, deben volver a aparecer como huérfanas. Se simula quitándolas del
        // registro, sin tocar el código de producción.
        var registroSinFur = Productores.Keys
            .Where(k => k is not ("fur_observations" or "fur_processing_date"))
            .ToHashSet(StringComparer.Ordinal);

        var detectadas = LlavesConsumidas().Where(k => !registroSinFur.Contains(k)).ToList();

        detectadas.Should().Contain("fur_observations");
        detectadas.Should().Contain("fur_processing_date");
    }

    [Fact]
    public void LasLlavesConstruidasEnEjecucionNoDanFalsoPositivo()
    {
        // H5 del barrido: vehicle_color_runt / vehicle_fuel_runt se escriben por concatenación de
        // sufijo, así que una búsqueda por literal las reportaría como huérfanas.
        var consumidas = LlavesConsumidas();

        consumidas.Should().Contain("vehicle_color_runt");
        Productores["vehicle_color_runt"].Modo.Should().Be(Modo.Dinamico);
        ContieneLiteral(Leer(Productores["vehicle_color_runt"].Ficheros[0]), "vehicle_color_runt")
            .Should().BeFalse("si apareciera como literal, el modo dinámico sobraría");
    }

    // ── HU #11138 — invariantes entre los tres proveedores del RUNT ──────────

    /// <summary>Los tres mappers que traducen una consulta de vehículo del RUNT a <c>field_values</c>.</summary>
    private static readonly string[] MappersDeRunt = [Verifik, Kyverum, Intempo];

    private const string Intempo = App + "UseCases/Consultations/IntempoVehicleResultMapper.cs";

    [Fact]
    public void NingunMapperEscribeElEstadoDelSoatSinNormalizar()
    {
        // `soat_estado` es a la vez celda del certificado y GATE de aprobación del organismo: el
        // frontend lo compara estricto contra "vigente" en minúscula, así que escribir el crudo del
        // RUNT ("VIGENTE") bloquea la aprobación de trámites con SOAT vigente. Ya pasó una vez
        // (HU #10973, en dos proveedores a la vez) y volvió a aparecer al sumar el tercero.
        var sinNormalizar = new List<string>();

        foreach (var mapper in MappersDeRunt)
        {
            var fuente = Leer(mapper);
            if (!fuente.Contains("SoatGate.FieldKey", StringComparison.Ordinal))
                continue; // ese mapper no escribe la llave

            if (!fuente.Contains("SoatGate.Normalize", StringComparison.Ordinal))
                sinNormalizar.Add(mapper);
        }

        sinNormalizar.Should().BeEmpty(
            "estos mappers escriben soat_estado sin pasarlo por SoatGate.Normalize, lo que bloquearía "
            + "la aprobación del organismo de tránsito: {0}",
            string.Join(", ", sinNormalizar));
    }

    [Fact]
    public void LosModelosDeRespuestaDelSoatDeclaranLoQueSuMapperConsume()
    {
        // La forma del fallo que originó el Feature: el modelo declara menos de lo que el servicio
        // envía, y lo que falta se descarta en silencio al deserializar. Aquí se comprueba lo contrario
        // —que nada del modelo quede sin usar— porque una propiedad declarada y nunca leída es la
        // señal de que alguien amplió el modelo y olvidó el mapper.
        var modelo = Leer(App + "UseCases/Consultations/IntempoVehicleResponse.cs");
        var mapper = Leer(Intempo);

        foreach (var propiedad in new[] { "NoPoliza", "FechaExpedicion", "FechaVigencia", "FechaVencimiento", "EntidadExpideSoat" })
        {
            modelo.Should().Contain($"public string? {propiedad}", "el modelo de Intempo declara {0}", propiedad);
            mapper.Should().Contain($".{propiedad}", "y su mapper debe consumirlo, no dejarlo muerto");
        }
    }
}
