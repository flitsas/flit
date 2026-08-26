using System.Globalization;
using Flit.DataMigration.V1.Mapping;

namespace Flit.DataMigration.V1.Cli;

/// <summary>
/// Orden de trabajo del migrador. Es el CONTRATO con quien decide qué se migra
/// (líder técnico / CTO): ellos entregan una lista de ids de V1, nosotros la ejecutamos.
/// El migrador es deliberadamente indiferente al criterio de selección — no filtra por
/// estado, compañía ni nada: migra exactamente lo que se le pide.
/// </summary>
public sealed class MigrationArgs
{
    /// <summary>El <c>--tipo</c> tal cual lo escribió el operador, para el encabezado del reporte.</summary>
    public string Tipo { get; private init; } = "transfer";

    /// <summary>Tipo de trámite de V1: define tabla, estados, adjuntos y tipo destino en V2.</summary>
    public V1ProcedureKind Kind { get; private init; } = V1ProcedureKind.Transfer;

    /// <summary>Qué instancia del migrador se corre (data plana, adjuntos o documentos).</summary>
    public MigrationInstance Instance { get; private init; } = MigrationInstance.Data;

    /// <summary>Ids de V1 a migrar, en el orden en que se procesarán.</summary>
    public IReadOnlyList<long> Ids { get; private init; } = [];

    /// <summary>Simula la migración completa y hace ROLLBACK. No deja rastro en V2.</summary>
    public bool DryRun { get; private init; }

    /// <summary>Re-migra aunque ya exista en <c>migration_map</c> (borra y vuelve a crear).</summary>
    public bool Force { get; private init; }

    /// <summary>Máximo de trámites a procesar de la lista (0 = sin límite).</summary>
    public int Limit { get; private init; }

    /// <summary>
    /// Migra también las imágenes sueltas de la validación de identidad (<c>frontalCard.jpg</c>,
    /// <c>backCard.jpg</c>, <c>userSelfie.jpg</c>), que por defecto se omiten porque la carta selfie
    /// que materializa la instancia 3 ya las contiene. Ver <c>IdentityPolicy</c>.
    /// </summary>
    public bool KeepIdentityImages { get; private init; }

    public static MigrationArgs Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var tipo = "transfer";
        var ids = new List<long>();
        var dryRun = false;
        var force = false;
        var limit = 0;
        var keepIdentityImages = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--tipo":
                    tipo = RequireValue(args, ref i, "--tipo");
                    break;

                case "--ids":
                    ids.AddRange(ParseIdList(RequireValue(args, ref i, "--ids")));
                    break;

                case "--ids-file":
                    ids.AddRange(ParseIdList(File.ReadAllText(RequireValue(args, ref i, "--ids-file"))));
                    break;

                case "--limit":
                    limit = int.Parse(RequireValue(args, ref i, "--limit"), CultureInfo.InvariantCulture);
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--force":
                    force = true;
                    break;

                case "--conservar-jpg-identidad":
                    keepIdentityImages = true;
                    break;

                case "--help" or "-h":
                    throw new HelpRequestedException();

                default:
                    throw new ArgumentException($"Argumento no reconocido: {arg}. Use --help.", nameof(args));
            }
        }

        if (ids.Count == 0)
        {
            throw new ArgumentException("Debe indicar al menos un id: --ids 8617 o --ids-file lista.txt", nameof(args));
        }

        // Se preserva el orden recibido y se eliminan duplicados: migrar dos veces el mismo id
        // en un solo lote no aporta y ensucia el reporte.
        var unique = ids.Distinct().ToList();
        if (limit > 0 && unique.Count > limit)
        {
            unique = unique.Take(limit).ToList();
        }

        var (kind, instance) = ResolveTipo(tipo);

        return new MigrationArgs
        {
            Tipo = tipo,
            Kind = kind,
            Instance = instance,
            Ids = unique,
            DryRun = dryRun,
            Force = force,
            Limit = limit,
            KeepIdentityImages = keepIdentityImages,
        };
    }

    /// <summary>
    /// Traduce <c>--tipo</c> a (tipo de trámite, instancia). La rejilla es
    /// <c>{transfer|registration}</c> × <c>{"" | -attachments | -documents}</c>.
    ///
    /// <para>
    /// <b>Por qué el prefijo es obligatorio y no hay un <c>--tipo attachments</c> genérico:</b>
    /// 12.807 ids existen a la vez en <c>vehicle_transfer_master</c> (242–24.906) y en
    /// <c>vehicle_registration_master</c> (1–13.216). Un <c>--ids 7360</c> sin decir de qué tabla es
    /// no da error: encuentra otro trámite real y trabaja sobre él. El prefijo obliga a declararlo
    /// en cada corrida.
    /// </para>
    /// </summary>
    private static (V1ProcedureKind Kind, MigrationInstance Instance) ResolveTipo(string tipo)
    {
        foreach (var kind in V1ProcedureKind.All)
        {
            if (string.Equals(tipo, kind.CliName, StringComparison.OrdinalIgnoreCase))
            {
                return (kind, MigrationInstance.Data);
            }

            if (string.Equals(tipo, $"{kind.CliName}-attachments", StringComparison.OrdinalIgnoreCase))
            {
                return (kind, MigrationInstance.Attachments);
            }

            if (string.Equals(tipo, $"{kind.CliName}-documents", StringComparison.OrdinalIgnoreCase))
            {
                return (kind, MigrationInstance.Documents);
            }
        }

        var validos = V1ProcedureKind.All
            .SelectMany(k => new[] { k.CliName, $"{k.CliName}-attachments", $"{k.CliName}-documents" });
        throw new ArgumentException(
            $"--tipo '{tipo}' no reconocido. Válidos: {string.Join(", ", validos)}.", nameof(tipo));
    }

    /// <summary>
    /// Acepta ids separados por coma, espacio o salto de línea, e ignora líneas
    /// vacías y comentarios (<c>#</c>). Así el mismo parser sirve para <c>--ids</c>
    /// y para un archivo generado por quien decide qué migrar.
    /// </summary>
    private static IEnumerable<long> ParseIdList(string raw) =>
        raw.Split(['\n', '\r', ',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Where(token => !token.StartsWith('#'))
           .Select(token => long.Parse(token, CultureInfo.InvariantCulture));

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{flag} requiere un valor.", nameof(args));
        }

        return args[++i];
    }

    public static string HelpText =>
        """
        Migrador de trámites Flit V1 -> V2

        USO
          dotnet run -- --tipo transfer                 --ids 8617 [--dry-run]
          dotnet run -- --tipo transfer-attachments     --ids 8617 [--dry-run]
          dotnet run -- --tipo transfer-documents       --ids 8617 [--dry-run]
          dotnet run -- --tipo registration             --ids 7360 [--dry-run]
          dotnet run -- --tipo registration-attachments --ids 7360 [--dry-run]
          dotnet run -- --tipo registration-documents   --ids 7360 [--dry-run]
          dotnet run -- --tipo transfer --ids-file lista.txt --limit 50

        OPCIONES
          --tipo <trámite>[-<instancia>]
                                El TRÁMITE es obligatorio y define de qué tabla de V1 se lee:
                                 · transfer      → vehicle_transfer_master      (traspaso)
                                 · registration  → vehicle_registration_master  (matrícula inicial)
                                Y la INSTANCIA, qué se migra:
                                 · (sin sufijo)  = 1, data plana: campos, actores, historial, estado.
                                 · -attachments  = 2, adjuntos que el cliente cargó y V1 guardó.
                                   Requiere que la data plana ya exista en migration_map.
                                 · -documents    = 3, documentos que V1 genera en caliente y nunca
                                   guarda (portada, formulario, compraventa del sistema, cartas
                                   selfie, mandato, trámite virtual, ...). Se los pide a V1 y los
                                   persiste en V2. Requiere la data plana migrada.

                                OJO: 12.807 ids existen en las DOS tablas de V1. Equivocarse de
                                prefijo NO da error: migra otro trámite, real y distinto. El
                                encabezado del reporte siempre dice qué tabla se está leyendo.
          --ids <a,b,c>         Ids de V1 a migrar, separados por coma o espacio.
          --ids-file <ruta>     Archivo con los ids (uno por línea; '#' comenta).
          --limit <n>           Procesa como máximo n trámites de la lista.
          --dry-run             Simula todo y hace ROLLBACK. No escribe en V2 ni sube binarios.
          --force               Re-migra aunque ya esté en migration_map / migration_attachment_map.
          --conservar-jpg-identidad
                                Solo para las instancias -attachments. Migra también las imágenes
                                sueltas de la validación de identidad (frontalCard.jpg,
                                backCard.jpg, userSelfie.jpg). Por defecto NO se migran: la carta
                                selfie que trae la instancia -documents las contiene embebidas junto
                                con el nombre, el documento, el hash y la firma. Solo se omiten en
                                las partes que efectivamente tienen carta (identidad validada +
                                selfie); si una parte no la tiene, sus imágenes se migran igual.
                                En traspaso son dos partes (comprador/vendedor); en matrícula, una
                                (el titular).
          --help                Muestra esta ayuda.

        NOTA
          Este programa NO decide qué se migra. Recibe la lista y la ejecuta tal cual,
          sin importar el estado del trámite (borrador, aprobado, anulado, ...).
          Los adjuntos necesitan Source/TargetFileManager y Attachments:Mode en appsettings.
          Los documentos generados necesitan V1Snapshot y TargetFileManager. Esa llamada es de
          SOLO LECTURA: V1 arma los documentos y los devuelve, no escribe nada.

          Sin la instancia -documents un trámite migrado pierde para siempre los documentos que V1
          genera al vuelo, porque V2 no tiene generadores para casi ninguno de ellos.
        """;
}

/// <summary>Señal de que el usuario pidió <c>--help</c>; no es un error.</summary>
public sealed class HelpRequestedException : Exception
{
    public HelpRequestedException() { }
    public HelpRequestedException(string message) : base(message) { }
    public HelpRequestedException(string message, Exception innerException) : base(message, innerException) { }
}
