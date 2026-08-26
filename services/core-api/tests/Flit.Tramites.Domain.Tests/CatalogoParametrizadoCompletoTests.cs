using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// ADR-0050 — cobertura del catálogo canónico: todo tipo del catálogo debe tener un recorrido
/// parametrizado, porque de eso depende que el wizard se conforme desde el tipo en lugar de caer al
/// recorrido estático heredado.
/// <para>Se valida parseando los seeds, igual que <see cref="F08SeedConfigTests"/>: el DDL vive como
/// recurso embebido y la suite unitaria no tiene base de datos.</para>
/// </summary>
public sealed class CatalogoParametrizadoCompletoTests
{
    /// <summary>Los 21 codes del catálogo canónico (40-catalogo-tipos-tramite-canonico.sql).</summary>
    private static readonly string[] CatalogoCanonico =
    [
        "MATRICULA_NUEVA", "MATRICULA_LEASING", "CANCELACION_MATRICULA", "REMATRICULA",
        "TRASPASO_STANDARD", "TRASPASO_UNILATERAL", "TRASPASO_TRANSFERENCIA_DE_DOMINIO",
        "CAMBIO_LOCATARIO", "CAMBIO_CARROCERIA", "BLINDAJE", "CAMBIO_COLOR", "DUPLICADO_PLACA",
        "DUPLICADO_TARJETA", "LEVANTAMIENTO_PRENDA", "PRENDA_INSCRIPCION", "RADICADO_CUENTA",
        "CONVERSION_COMBUSTIBLE", "TRASLADO_CUENTA", "REGRABAR_MOTOR_CHASIS",
        "LEVANTAR_INSCRIBIR_PRENDA", "CAMBIO_ACREEDOR",
    ];

    private static string Ddl(string file)
    {
        var rel = Path.Combine("src", "Flit.Infrastructure", "Persistence", "Sql", "Ddl", file);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, rel);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException($"No se encontró {rel} subiendo desde {AppContext.BaseDirectory}");
    }

    /// <summary>Codes con recorrido en alguno de los tres seeds que parametrizan el catálogo.</summary>
    private static HashSet<string> CodesParametrizados()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        // 81 — los dos tipos operativos.
        foreach (Match m in Regex.Matches(Ddl("81-parametrizacion-tipos-operativos.sql"), @"\('([A-Z_]+)',\s*'[a-z_]+',"))
            codes.Add(m.Groups[1].Value);

        // 82 — el resto del catálogo (tabla de asignación a recorrido).
        var asignacion = Ddl("82-parametrizacion-catalogo-completo.sql");
        var bloque = asignacion[asignacion.IndexOf("INSERT INTO _asignacion", StringComparison.Ordinal)..];
        bloque = bloque[..bloque.IndexOf(';', StringComparison.Ordinal)];
        foreach (Match m in Regex.Matches(bloque, @"\('([A-Z_]+)',"))
            codes.Add(m.Groups[1].Value);

        // 38 — los dos tipos de referencia de FEATURE-08.
        foreach (Match m in Regex.Matches(Ddl("38-F08-seeds-tipos-configurados.sql"),
                     @"VALUES \(uuidv7\(\), '(PRENDA_INSCRIPCION|CAMBIO_LOCATARIO)'"))
            codes.Add(m.Groups[1].Value);

        return codes;
    }

    [Fact]
    public void TodoTipoDelCatalogoCanonicoTieneRecorrido()
    {
        var parametrizados = CodesParametrizados();

        var sinRecorrido = CatalogoCanonico.Where(c => !parametrizados.Contains(c)).ToList();

        sinRecorrido.Should().BeEmpty(
            "un tipo sin pasos cae al recorrido estático heredado, que es justo lo que ADR-0050 retira");
    }

    [Fact]
    public void LosSectionTypeDelSeedPertenecenAlCatalogoCerrado()
    {
        // El CHECK del DDL los rechazaría al arrancar; aquí falla en CI, que es mucho más barato.
        var invalidos = Regex.Matches(Ddl("82-parametrizacion-catalogo-completo.sql"), @"'([a-z_]+)'\)[,;]")
            .Select(m => m.Groups[1].Value)
            .Where(v => v.Contains('_') && !ProcedureSectionTypes.IsValid(v))
            .Where(v => v is not ("single" or "consulta_vin"))
            .Distinct()
            .ToList();

        invalidos.Should().BeEmpty();
    }

    [Fact]
    public void LosPerfilesDelSeedSonJsonValidoYConEntryModeCorrecto()
    {
        var seed = Ddl("82-parametrizacion-catalogo-completo.sql");

        var perfiles = Regex.Matches(seed, @"'(\{""entryMode"".*?\})'")
            .Select(m => ProcedureTypeGateProfile.FromJson(m.Groups[1].Value))
            .ToList();

        perfiles.Should().HaveCountGreaterThan(15, "hay 17 tipos parametrizados en este seed");
        perfiles.Should().OnlyContain(p => ProcedureTypeGateProfile.IsValidEntryMode(p.EntryMode));
    }

    [Fact]
    public void LosTiposConGravamenDeclaranElGateDePrenda()
    {
        var seed = Ddl("82-parametrizacion-catalogo-completo.sql");

        foreach (var code in new[] { "LEVANTAMIENTO_PRENDA", "LEVANTAR_INSCRIBIR_PRENDA", "CAMBIO_ACREEDOR" })
        {
            var linea = seed.Split('\n').FirstOrDefault(l => l.Contains($"('{code}'", StringComparison.Ordinal)
                                                             && l.Contains("entryMode", StringComparison.Ordinal));
            linea.Should().NotBeNull($"{code} debe tener perfil en el seed");
            linea!.Should().Contain("\"hasPrendaGate\":true",
                $"{code} constituye o levanta gravamen y sin el gate la decisión de prenda no bloquearía");
        }
    }

    [Fact]
    public void NingunTipoSeHabilitaAlWizardEnElSeed()
    {
        // La barrera es una decisión de negocio: exige matriz documental, causales y homologación
        // Quipux/ICT. Un seed no puede encenderla por su cuenta.
        // Se miran solo las sentencias, no los comentarios: la cabecera del DDL menciona la frase
        // justamente para explicar que ningún tipo se enciende.
        var sql = string.Join('\n', Ddl("82-parametrizacion-catalogo-completo.sql")
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

        sql.Should().NotContain("wizard_enabled", "el seed no toca la barrera de operación");
    }

    [Fact]
    public void LaFamiliaOtrosPideUnSoloActor_ElTitular()
    {
        // Regla de negocio: en OTROS interviene un único actor —el dueño, que no vende ni compra— y
        // se persiste con el ActorType 'comprador' porque el modelo no tiene rol 'propietario'.
        // El perfil debe declararlo, o el paso de actores no exigiría nada y quedaría siempre
        // completo (que es justo el defecto que tenía la primera versión de este seed).
        var seed = Ddl("82-parametrizacion-catalogo-completo.sql");

        foreach (var code in new[] { "BLINDAJE", "CAMBIO_COLOR", "CONVERSION_COMBUSTIBLE", "DUPLICADO_TARJETA" })
        {
            var linea = seed.Split('\n').FirstOrDefault(l =>
                l.Contains($"('{code}'", StringComparison.Ordinal) && l.Contains("entryMode", StringComparison.Ordinal));

            linea.Should().NotBeNull($"{code} debe tener perfil");
            linea!.Should().Contain("\"requiresBuyer\":true", $"{code} exige el titular del vehículo");
            linea.Should().NotContain("\"requiresSeller\":true", $"{code} no tiene parte vendedora");
            linea.Should().Contain("\"entryMode\":\"PLATE\"", $"{code} entra por placa: el vehículo ya está matriculado");
        }
    }

    [Fact]
    public void EnOtrosSePidePlacaYDuenoAntesQueLosDocumentos()
    {
        // El orden lo fijó negocio: primero la placa y el dueño, después los documentos del trámite.
        var seed = Ddl("82-parametrizacion-catalogo-completo.sql");
        var recorrido = seed.Split('\n')
            .Where(l => l.TrimStart().StartsWith("('NOVEDAD'", StringComparison.Ordinal))
            .ToList();

        recorrido.Should().HaveCount(5, "consulta, propietario, documentos, identidad y FUR");

        var orden = recorrido.Select(l => l.Split('\'').ElementAt(3)).ToList();
        orden.Should().ContainInOrder("consulta", "propietario", "documentos", "identidad", "fur");
    }

    [Fact]
    public void LaSeccionDelTitularSeCodificaComoCompradorYSeTitulaPropietario()
    {
        // El código de sección es lo que el motor usa para saber qué actor exigir; el título es lo
        // que lee el operador. En OTROS divergen a propósito.
        var linea = Ddl("82-parametrizacion-catalogo-completo.sql").Split('\n')
            .FirstOrDefault(l => l.Contains("('NOVEDAD'", StringComparison.Ordinal)
                                 && l.Contains("'propietario'", StringComparison.Ordinal));

        linea.Should().NotBeNull();
        linea!.Should().Contain("'Propietario'", "es la etiqueta que ve el operador");
        linea.Should().Contain("'COMPRADOR'", "es el rol con el que se persiste el titular");
    }

    [Fact]
    public void LaMatrizDocumentalUsaSoloCodigosDelCatalogo()
    {
        // Inventar un code de documento haría que el JOIN del seed no encuentre fila y el requisito
        // se pierda en silencio.
        var conocidos = new HashSet<string>(StringComparer.Ordinal)
        {
            "tarjeta_propiedad", "doc_identidad_propietario", "soat", "paz_salvo", "factura_carroceria",
            "impronta", "certificado_ambiental", "otro", "cert_tradicion", "oficio_judicial",
            "paz_salvo_prenda", "inscripcion_prenda", "limitacion_propiedad", "contrato_leasing",
            "factura", "doc_identidad_comprador", "aduana", "compraventa", "doc_identidad_vendedor",
            "transferencia_dominio",
        };

        var seed = Ddl("82-parametrizacion-catalogo-completo.sql");
        var bloque = seed[seed.IndexOf("CREATE TEMP TABLE _requisitos", StringComparison.Ordinal)..];

        var usados = Regex.Matches(bloque, @"'([a-z_]{4,40})',\s*(?:true|false)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        usados.Should().NotBeEmpty();
        usados.Should().OnlyContain(c => conocidos.Contains(c));
    }
}
