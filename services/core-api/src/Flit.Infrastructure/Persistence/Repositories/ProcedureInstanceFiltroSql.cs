using System.Linq.Expressions;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Los predicados con los que se filtra <c>procedure_instances</c> desde la gramática de Consultas,
/// escritos UNA vez para las dos superficies que preguntan por la misma tabla: el listado del gestor
/// (HU #12106) y la bandeja del organismo de tránsito (HU #12217).
///
/// <para><b>Por qué existe este archivo.</b> Las dos superficies tienen vocabularios distintos —el
/// gestor pregunta por «organismo de tránsito» y por el estado en SU empresa; el organismo pregunta
/// por «empresa cliente» y por el estado leído desde SU bandeja— pero cuando preguntan lo mismo
/// tienen que responder lo mismo. Una placa que casa en una pantalla y no en la otra no falla con un
/// error: hace que dos partes del producto se contradigan sobre el mismo trámite, y nadie sabe cuál
/// creer. Por eso lo que se comparte son los PREDICADOS, no el despacho: cada repositorio mantiene su
/// propio <c>switch</c> sobre sus propios identificadores de campo y los enruta aquí.</para>
///
/// <para><b>Lo que NO entra aquí</b> es lo que solo tiene sentido en una superficie —la fuente y la
/// firma de compraventa son del gestor; el sub-estado de placa es del organismo—. Compartir eso solo
/// añadiría indirección sin evitar ninguna divergencia.</para>
///
/// <para>Nunca se construye SQL con texto del cliente: el identificador de campo viene de una lista
/// cerrada (el catálogo) y los valores viajan como parámetros.</para>
/// </summary>
internal static class ProcedureInstanceFiltroSql
{
    public const string ActorTipoComprador = "comprador";
    public const string ActorTipoVendedor = "vendedor";

    /// <summary>
    /// Normaliza un valor de IDENTIFICADOR igual que <c>QueryEngine.SinSeparadores</c>: mayúsculas y
    /// sin guiones, puntos ni espacios.
    ///
    /// <para><b>Esta regla está escrita DOS veces</b> —aquí y en el motor de Consultas— y las dos
    /// tienen que decir lo mismo. Si se cambia una hay que cambiar la otra: una placa que casa en
    /// Consultas y no en el listado no falla con un error, hace que dos pantallas del mismo producto
    /// se contradigan sobre el mismo trámite. La forma de las expresiones SQL de abajo
    /// (<c>ToUpper().Replace(...)</c>) replica exactamente estos tres reemplazos.</para>
    /// </summary>
    public static string NormalizarIdentificador(string valor) => valor
        .ToUpperInvariant()
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace(".", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Los valores de una condición, ya listos para comparar: sin vacíos, en mayúsculas y sin
    /// repetidos. Los identificadores además pierden los separadores.
    /// </summary>
    public static List<string> Normalizar(QueryCondition condicion, bool identificador) =>
        [.. condicion.Values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => identificador ? NormalizarIdentificador(v) : v.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// ¿Esta condición no acota nada? Un operador con valores que se quedan todos vacíos no es un
    /// filtro: filtrar por «nada» no debe vaciar el listado.
    /// </summary>
    public static bool EsInerte(string op, List<string> valores) =>
        !QueryOperator.IsUnary(op) && valores.Count == 0;

    // ── Identificadores ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// El radicado que emite FLIT: <c>FT1-0000012</c> (HU #12371). Nunca es nulo, así que «está
    /// vacío» se compara contra la cadena vacía y no contra <c>null</c>.
    ///
    /// <para><b>«Es alguno» lee lo que el usuario escribió como radicado</b> (<see cref="Radicado.TryLeer"/>):
    /// <c>12</c>, <c>0000012</c> y <c>FT1-0000012</c> encuentran el mismo trámite. Sin prefijo se
    /// compara el <see cref="ProcedureInstance.Consecutivo"/>, que es por lo que de verdad se
    /// pregunta; con prefijo se compara el texto canónico, así que <c>FT2-0000012</c> NO encuentra
    /// una matrícula: el usuario fue explícito. Lo que no se puede leer como radicado se compara
    /// como texto normalizado, igual que antes.</para>
    ///
    /// <para><b>«Contiene» sigue siendo contiene</b>, sobre el texto sin guion: <c>12</c> trae
    /// <c>FT1-0000012</c> y también <c>FT1-0000120</c>. Se deja así a propósito; el operador exacto
    /// es «es alguno».</para>
    /// </summary>
    public static IQueryable<ProcedureInstance> PorRadicado(
        IQueryable<ProcedureInstance> query, string op, List<string> valores)
    {
        switch (op)
        {
            case QueryOperator.EsAlguno:
            {
                var (consecutivos, textos) = LeerRadicados(valores);
                return query.Where(x => consecutivos.Contains(x.Consecutivo)
                    || textos.Contains(x.ReferenceNumber.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", "")));
            }
            case QueryOperator.NoEsNinguno:
            {
                var (consecutivos, textos) = LeerRadicados(valores);
                return query.Where(x => !consecutivos.Contains(x.Consecutivo)
                    && !textos.Contains(x.ReferenceNumber.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", "")));
            }
            case QueryOperator.Contiene:
                return query.Where(x => x.ReferenceNumber
                    .ToUpper().Replace("-", "").Replace(" ", "").Replace(".", "")
                    .Contains(valores[0]));
            case QueryOperator.EstaVacio:
                return query.Where(x => x.ReferenceNumber == "");
            case QueryOperator.NoEstaVacio:
                return query.Where(x => x.ReferenceNumber != "");
            default:
                return query;
        }
    }

    /// <summary>
    /// Reparte los valores ya normalizados en lo que se compara por número (sin prefijo) y lo que se
    /// compara por texto (con prefijo, en forma canónica; o ilegible como radicado, tal cual).
    /// </summary>
    private static (List<long> Consecutivos, List<string> Textos) LeerRadicados(IEnumerable<string> valores)
    {
        var consecutivos = new List<long>();
        var textos = new List<string>();
        foreach (var valor in valores)
        {
            if (!Radicado.TryLeer(valor, out var lectura))
                textos.Add(valor);
            else if (lectura.Value.CanonicoSinGuion is { } canonico)
                textos.Add(canonico);
            else
                consecutivos.Add(lectura.Value.Consecutivo);
        }
        return (consecutivos, textos);
    }

    /// <summary>
    /// El término de la barra de búsqueda leído como radicado (HU #12371 AC7), en la forma en que
    /// las dos búsquedas libres lo meten en su <c>Where</c>:
    /// <code>
    /// (consecutivo != null &amp;&amp; x.Consecutivo == consecutivo)
    /// || (canonico != null &amp;&amp; x.ReferenceNumber.ToUpper().Replace("-", "") == canonico)
    /// </code>
    /// Sin prefijo (<c>12</c>, <c>0000012</c>) se busca por número; con prefijo (<c>FT1-0000012</c>,
    /// <c>ft1 12</c>) por el texto canónico. Si el término no es un radicado, las dos salen nulas y
    /// el OR se apaga: el término se busca en los demás campos. Va aquí y no inline en cada
    /// repositorio para que el gestor y el organismo lean el mismo término igual.
    /// </summary>
    public static (long? Consecutivo, string? Canonico) LeerBusquedaRadicado(string termino)
    {
        if (!Radicado.TryLeer(termino, out var lectura))
            return (null, null);

        return lectura.Value.CanonicoSinGuion is { } canonico
            ? (null, canonico)
            : (lectura.Value.Consecutivo, null);
    }

    public static IQueryable<ProcedureInstance> PorPlaca(
        IQueryable<ProcedureInstance> query, string op, List<string> valores) => op switch
    {
        QueryOperator.EsAlguno => query.Where(x => x.Plate != null && valores.Contains(
            x.Plate.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
        QueryOperator.NoEsNinguno => query.Where(x => x.Plate == null || !valores.Contains(
            x.Plate.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
        QueryOperator.Contiene => query.Where(x => x.Plate != null && x.Plate
            .ToUpper().Replace("-", "").Replace(" ", "").Replace(".", "")
            .Contains(valores[0])),
        QueryOperator.EstaVacio => query.Where(x => x.Plate == null || x.Plate == ""),
        QueryOperator.NoEstaVacio => query.Where(x => x.Plate != null && x.Plate != ""),
        _ => query,
    };

    public static IQueryable<ProcedureInstance> PorVin(
        IQueryable<ProcedureInstance> query, string op, List<string> valores) => op switch
    {
        QueryOperator.EsAlguno => query.Where(x => x.Vin != null && valores.Contains(
            x.Vin.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
        QueryOperator.NoEsNinguno => query.Where(x => x.Vin == null || !valores.Contains(
            x.Vin.ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""))),
        QueryOperator.Contiene => query.Where(x => x.Vin != null && x.Vin
            .ToUpper().Replace("-", "").Replace(" ", "").Replace(".", "")
            .Contains(valores[0])),
        QueryOperator.EstaVacio => query.Where(x => x.Vin == null || x.Vin == ""),
        QueryOperator.NoEstaVacio => query.Where(x => x.Vin != null && x.Vin != ""),
        _ => query,
    };

    // ── Personas ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Comprador/vendedor: coincide por el nombre denormalizado O por el documento del actor.
    /// «Está vacío» mira solo el nombre — es lo que la columna del listado muestra, y un trámite con
    /// documento pero sin nombre no existe (los captura el mismo formulario).
    /// </summary>
    public static IQueryable<ProcedureInstance> PorActor(
        IQueryable<ProcedureInstance> query, string op, List<string> valores, string actorType)
    {
        var esComprador = actorType == ActorTipoComprador;

        return op switch
        {
            QueryOperator.EsAlguno => esComprador
                ? query.Where(x =>
                    (x.CompradorNombre != null && valores.Contains(x.CompradorNombre.ToUpper()))
                    || x.Actors.Any(a => a.ActorType == ActorTipoComprador
                        && valores.Contains(a.DocumentNumber.ToUpper())))
                : query.Where(x =>
                    (x.VendedorNombre != null && valores.Contains(x.VendedorNombre.ToUpper()))
                    || x.Actors.Any(a => a.ActorType == ActorTipoVendedor
                        && valores.Contains(a.DocumentNumber.ToUpper()))),

            QueryOperator.NoEsNinguno => esComprador
                ? query.Where(x =>
                    (x.CompradorNombre == null || !valores.Contains(x.CompradorNombre.ToUpper()))
                    && !x.Actors.Any(a => a.ActorType == ActorTipoComprador
                        && valores.Contains(a.DocumentNumber.ToUpper())))
                : query.Where(x =>
                    (x.VendedorNombre == null || !valores.Contains(x.VendedorNombre.ToUpper()))
                    && !x.Actors.Any(a => a.ActorType == ActorTipoVendedor
                        && valores.Contains(a.DocumentNumber.ToUpper()))),

            QueryOperator.Contiene => esComprador
                ? query.Where(x =>
                    (x.CompradorNombre != null && x.CompradorNombre.ToUpper().Contains(valores[0]))
                    || x.Actors.Any(a => a.ActorType == ActorTipoComprador
                        && a.DocumentNumber.ToUpper().Contains(valores[0])))
                : query.Where(x =>
                    (x.VendedorNombre != null && x.VendedorNombre.ToUpper().Contains(valores[0]))
                    || x.Actors.Any(a => a.ActorType == ActorTipoVendedor
                        && a.DocumentNumber.ToUpper().Contains(valores[0]))),

            QueryOperator.EstaVacio => esComprador
                ? query.Where(x => x.CompradorNombre == null || x.CompradorNombre == "")
                : query.Where(x => x.VendedorNombre == null || x.VendedorNombre == ""),

            QueryOperator.NoEstaVacio => esComprador
                ? query.Where(x => x.CompradorNombre != null && x.CompradorNombre != "")
                : query.Where(x => x.VendedorNombre != null && x.VendedorNombre != ""),

            _ => query,
        };
    }

    // ── Trámite ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Tipo de trámite, por el CÓDIGO del tipo (no por su nombre, que se puede renombrar).</summary>
    public static IQueryable<ProcedureInstance> PorTipoTramite(
        IQueryable<ProcedureInstance> query, string op, List<string> valores) => op switch
    {
        QueryOperator.EsAlguno => query.Where(x =>
            x.ProcedureType != null && valores.Contains(x.ProcedureType.Code.ToUpper())),
        QueryOperator.NoEsNinguno => query.Where(x =>
            x.ProcedureType == null || !valores.Contains(x.ProcedureType.Code.ToUpper())),
        _ => query,
    };

    /// <summary>
    /// Acota a una o varias compañías, comparando por identificador y no por razón social: dos
    /// empresas pueden renombrarse y el nombre no es clave de nada.
    ///
    /// <para>Es la «compañía» del gestor y la «empresa cliente» del organismo: la misma columna
    /// mirada desde los dos lados del trámite.</para>
    /// </summary>
    public static IQueryable<ProcedureInstance> PorTenant(
        IQueryable<ProcedureInstance> query, string op, List<string> valores)
    {
        var ids = new List<Guid>();
        foreach (var valor in valores)
            if (Guid.TryParse(valor, out var id)) ids.Add(id);
        if (ids.Count == 0) return query;

        return op switch
        {
            QueryOperator.EsAlguno => query.Where(x => ids.Contains(x.TenantId)),
            QueryOperator.NoEsNinguno => query.Where(x => !ids.Contains(x.TenantId)),
            _ => query,
        };
    }

    // ── Booleanos y marcas ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un booleano de la gramática: los valores llegan como "TRUE"/"FALSE" ya normalizados. Con las
    /// dos opciones marcadas no se filtra, porque «sí o no» es el universo entero.
    /// </summary>
    public static IQueryable<ProcedureInstance> Booleano(
        IQueryable<ProcedureInstance> query, string op, List<string> valores,
        Func<IQueryable<ProcedureInstance>, IQueryable<ProcedureInstance>> verdadero,
        Func<IQueryable<ProcedureInstance>, IQueryable<ProcedureInstance>> falso)
    {
        if (op != QueryOperator.EsAlguno || valores.Count != 1) return query;
        return valores[0] == "TRUE" ? verdadero(query) : falso(query);
    }

    /// <summary>
    /// El «No» de una marca es exactamente la negación de su «Sí», no una condición aparte: así los
    /// dos conjuntos son complementarios y ninguno pierde filas por construcción, en vez de por
    /// haber escrito dos veces la misma regla al derecho y al revés.
    /// </summary>
    public static Expression<Func<ProcedureInstance, bool>> Negar(
        Expression<Func<ProcedureInstance, bool>> expr) =>
        Expression.Lambda<Func<ProcedureInstance, bool>>(
            Expression.Not(expr.Body), expr.Parameters);

    /// <summary>
    /// Códigos y valores de las dos marcas, ya normalizados como los guarda la base, para no
    /// recalcularlos en cada consulta. Salen del DOMINIO —<see cref="ProcedureTypeLayers"/> y
    /// <see cref="TramiteMarcas"/>—, que es lo que impide que el <c>WHERE</c> y el ícono del listado
    /// se separen: si mañana entra un tipo o una forma afirmativa nueva, entra en los dos a la vez.
    /// </summary>
    private static readonly string[] CodigosPrendaBase =
        [.. ProcedureTypeLayers.CodigosPrendaBase.Select(c => c.ToUpperInvariant())];

    private static readonly string[] CodigosTransformacion =
        [.. ProcedureTypeLayers.CodigosTransformacion.Select(c => c.ToUpperInvariant())];

    private static readonly string[] ClavesTransformacion =
        [.. TramiteMarcas.ClavesTransformacion];

    private static readonly string[] ValoresAfirmativos =
        [.. TramiteMarcas.ValoresAfirmativos];

    /// <summary>
    /// «Tiene prenda», en SQL. Réplica de <see cref="TramiteMarcas.TienePrenda"/>: la decisión
    /// VIGENTE que no sea <c>omitir</c> ni <c>sin_prenda</c> <b>o</b> que el trámite SEA de prenda,
    /// que lo es desde que se abre, antes de que nadie capture la decisión.
    /// </summary>
    /// <remarks>
    /// Recibe el contexto porque la decisión de prenda no cuelga de la instancia como navegación:
    /// vive en su propia tabla y hay que ir a ella por subconsulta correlacionada.
    /// </remarks>
    public static Expression<Func<ProcedureInstance, bool>> TienePrenda(FlitDbContext db) =>
        x => (x.ProcedureType != null && CodigosPrendaBase.Contains(x.ProcedureType.Code.ToUpper()))
            || db.ProcedureInstancePrendas.Any(p => p.ProcedureInstanceId == x.Id
                && p.Estado == PrendaEstado.Vigente
                && p.Decision != PrendaDecision.SinPrenda
                && p.Decision != PrendaDecision.Omitir);

    /// <summary>
    /// «Tiene transformación», en SQL. Réplica de <see cref="TramiteMarcas.TieneTransformacion"/>:
    /// el tipo ES la transformación <b>o</b> el formulario declara alguna de las cuatro claves con
    /// un valor afirmativo. El <c>Trim</c>/<c>ToLower</c> no es adorno: por los migrados de V1
    /// circulan <c>1</c> y <c>si</c> además de <c>true</c>, y con espacios alrededor.
    /// </summary>
    public static Expression<Func<ProcedureInstance, bool>> TieneTransformacion { get; } =
        x => (x.ProcedureType != null && CodigosTransformacion.Contains(x.ProcedureType.Code.ToUpper()))
            || x.FieldValues.Any(fv => ClavesTransformacion.Contains(fv.FieldKey)
                && fv.ValueText != null
                && ValoresAfirmativos.Contains(fv.ValueText.Trim().ToLower()));
}
