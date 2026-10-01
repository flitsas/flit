namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// HU #13142 (ADR-0066) — nivel de la prelación en el que se resolvió el firmante del mandato. El orden del
/// enum es el de la prelación: lo que define el OT prevalece sobre lo que define la compañía.
/// </summary>
public enum MandateSignerLevel
{
    /// <summary>Elección del OT al aprobar, o el firmante ya guardado en el trámite, siempre que sea válido.</summary>
    Explicita,

    /// <summary>Mandatario que el OT (o el Super Admin) configuró para la compañía.</summary>
    OtParaCompania,

    /// <summary>Mandatario propio de la compañía en ese organismo.</summary>
    PropioDeCompania,

    /// <summary>Mandatario de OTRA compañía asociado a la del trámite (HU #13180). Solo cuenta si ningún nivel superior resuelve.</summary>
    AsociadoDeOtraCompania,

    /// <summary>Default del OT, aunque no esté vinculado a la compañía gestora.</summary>
    DefaultDelOt,

    /// <summary>Ningún nivel resuelve: el gate de radicación decide según el modo.</summary>
    Ninguno,
}

/// <summary>Un mandatario descartado por el resolver, con el motivo (vocabulario estable de ADR-0066).</summary>
public sealed record MandateSignerDiscard(Guid SignerId, MandateSignerLevel Level, string Motivo);

/// <summary>Motivos de descarte: los tres de ADR-0061 más los tres de ADR-0066.</summary>
public static class MandateSignerDiscardReasons
{
    public const string FueraDeVigencia = "mandatario_fuera_de_vigencia";
    public const string Inactivo = "mandatario_inactivo";
    public const string SinValidacionAprobada = "sin_validacion_aprobada";
    public const string BaulSinFirmaVigente = "baul_sin_firma_vigente";
    public const string FirmaFisicaSinMigrar = "firma_fisica_sin_migrar";
    public const string Eliminado = "mandatario_eliminado";

    /// <summary>Motivos que hablan de la FIRMA (no de la vigencia o el estado del mandatario).</summary>
    public static bool EsDeFirma(string motivo) =>
        motivo is SinValidacionAprobada or BaulSinFirmaVigente or FirmaFisicaSinMigrar;
}

/// <summary>
/// Resultado del resolver. <see cref="Signer"/> es nulo cuando <see cref="Level"/> es
/// <see cref="MandateSignerLevel.Ninguno"/>, cuando el nivel es ambiguo (varios válidos sin designado;
/// <see cref="Desempate"/> trae el conjunto) o cuando la elección del OT no es válida
/// (<see cref="EleccionInvalida"/>).
/// </summary>
/// <param name="Validos">Conjunto válido sin repetidos (vínculos + default del OT): sobre él valida el selector y
/// se arma el 409 <c>mandatario_requerido</c>.</param>
/// <param name="Descartados">Quién se descartó, en qué nivel y por qué.</param>
/// <param name="Desempate">Conjunto ambiguo sobre el que solo vale el cotejo por usuario (ADR-0036 §D9, ahora
/// desempate); nulo si el nivel no es ambiguo.</param>
/// <param name="EleccionInvalida">El OT eligió a alguien que no está entre los válidos: debe elegir otra vez.</param>
/// <param name="FirmaFisicaPendiente">Mejor candidato descartado SOLO por <c>firma_fisica_sin_migrar</c> cuando no
/// hay ninguno válido: la marca <c>signs_physically</c> se sigue honrando en el PDF y en la aprobación (P4) aunque
/// no haga válida la firma para el gate.</param>
public sealed record MandateSignerPrelacion(
    MandateSignerLevel Level,
    MandateSignerCandidate? Signer,
    IReadOnlyList<MandateSignerCandidate> Validos,
    IReadOnlyList<MandateSignerDiscard> Descartados,
    IReadOnlyList<MandateSignerCandidate>? Desempate = null,
    bool EleccionInvalida = false,
    MandateSignerCandidate? FirmaFisicaPendiente = null)
{
    /// <summary>El nivel es ambiguo: hay varios válidos y ninguno designado.</summary>
    public bool Ambiguo => Signer is null && !EleccionInvalida && Desempate is { Count: > 1 };
}

/// <summary>
/// Resuelve el mandatario que firma el mandato (ADR-0066, enmienda parcial de ADR-0036). Función PURA y única:
/// la pantalla, el PDF, la aprobación y el gate de radicación la usan, así que el firmante es el mismo en los
/// cuatro sitios. Niveles: Explícita → OT para la compañía → propio de la compañía → asociado de otra compañía
/// (HU #13180) → default del OT → Ninguno.
/// </summary>
public static class MandateSignerDefaultResolver
{
    /// <param name="candidatos">Vínculos de la compañía en el organismo (con origen) y los asociados de otras compañías.</param>
    /// <param name="defaultDelOt">Default del OT (vía <c>GetByIdAsync</c>), aunque no esté vinculado a la compañía.</param>
    /// <param name="eleccionOt">Elección explícita del OT al aprobar.</param>
    /// <param name="guardado">Firmante ya guardado en el trámite; si no es válido se ignora.</param>
    /// <param name="designadoRegla">
    /// <c>default_mandate_signer_id</c> de la regla compañía×OT: ya no es un nivel, solo desempate transitorio si
    /// un grupo de origen aún tiene N&gt;1 válidos.
    /// </param>
    public static MandateSignerPrelacion Resolve(
        IReadOnlyList<MandateSignerCandidate> candidatos,
        MandateSignerCandidate? defaultDelOt,
        Guid? eleccionOt,
        Guid? guardado,
        Guid? designadoRegla = null)
    {
        ArgumentNullException.ThrowIfNull(candidatos);

        var descartados = new List<MandateSignerDiscard>();
        var validosPorNivel = new Dictionary<MandateSignerLevel, List<MandateSignerCandidate>>
        {
            [MandateSignerLevel.OtParaCompania] = [],
            [MandateSignerLevel.PropioDeCompania] = [],
            [MandateSignerLevel.AsociadoDeOtraCompania] = [],
            [MandateSignerLevel.DefaultDelOt] = [],
        };

        // Clasifica cada candidato en su nivel y lo descarta con motivo si no cuenta.
        foreach (var c in candidatos)
        {
            var nivel = NivelDe(c.Origen);
            var motivo = MotivoDescarte(c);
            if (motivo is null)
            {
                validosPorNivel[nivel].Add(c);
            }
            else
            {
                descartados.Add(new MandateSignerDiscard(c.Id, nivel, motivo));
            }
        }

        if (defaultDelOt is not null)
        {
            var motivo = MotivoDescarte(defaultDelOt);
            if (motivo is null)
            {
                validosPorNivel[MandateSignerLevel.DefaultDelOt].Add(defaultDelOt);
            }
            else if (!descartados.Any(d => d.SignerId == defaultDelOt.Id))
            {
                descartados.Add(new MandateSignerDiscard(defaultDelOt.Id, MandateSignerLevel.DefaultDelOt, motivo));
            }
        }

        // Conjunto válido sin repetidos (el mismo mandatario puede entrar por dos orígenes).
        var validos = validosPorNivel.Values.SelectMany(l => l).DistinctBy(c => c.Id).ToList();

        // Nivel 0 — elección explícita del OT, o el guardado, solo si es válido.
        if (eleccionOt is { } elegido && elegido != Guid.Empty)
        {
            var match = validos.FirstOrDefault(c => c.Id == elegido);
            return match is not null
                ? new MandateSignerPrelacion(MandateSignerLevel.Explicita, match, validos, descartados)
                : new MandateSignerPrelacion(
                    MandateSignerLevel.Ninguno, null, validos, descartados, null, EleccionInvalida: true);
        }

        if (guardado is { } saved && saved != Guid.Empty)
        {
            var match = validos.FirstOrDefault(c => c.Id == saved);
            if (match is not null)
            {
                return new MandateSignerPrelacion(MandateSignerLevel.Explicita, match, validos, descartados);
            }
        }

        // Niveles 1 a 3: un solo válido por nivel; varios sin designado ⇒ ambiguo (no se elige al azar).
        foreach (var nivel in new[]
        {
            MandateSignerLevel.OtParaCompania, MandateSignerLevel.PropioDeCompania,
            MandateSignerLevel.AsociadoDeOtraCompania,
        })
        {
            var grupo = validosPorNivel[nivel].DistinctBy(c => c.Id).ToList();
            if (grupo.Count == 0)
            {
                continue;
            }

            if (grupo.Count == 1)
            {
                return new MandateSignerPrelacion(nivel, grupo[0], validos, descartados);
            }

            var designado = designadoRegla is { } d && d != Guid.Empty ? grupo.FirstOrDefault(c => c.Id == d) : null;
            if (designado is not null)
            {
                return new MandateSignerPrelacion(nivel, designado, validos, descartados);
            }

            // Dos asociados de otras compañías no bloquean la prelación: siguen al default del OT y
            // ambos quedan como candidatos si ninguno lo resuelve.
            if (nivel == MandateSignerLevel.AsociadoDeOtraCompania
                && validosPorNivel[MandateSignerLevel.DefaultDelOt].Count > 0)
            {
                continue;
            }

            return new MandateSignerPrelacion(nivel, null, validos, descartados, grupo);
        }

        // Nivel 4 — default del OT.
        var ot = validosPorNivel[MandateSignerLevel.DefaultDelOt];
        if (ot.Count > 0)
        {
            return new MandateSignerPrelacion(MandateSignerLevel.DefaultDelOt, ot[0], validos, descartados);
        }

        return new MandateSignerPrelacion(
            MandateSignerLevel.Ninguno, null, validos, descartados, null,
            FirmaFisicaPendiente: validos.Count == 0
                ? MejorFirmaFisicaPendiente(candidatos, defaultDelOt, descartados)
                : null);
    }

    /// <summary>Origen → nivel: el Super Admin se equipara al OT (supuesto S-4 de ADR-0066).</summary>
    private static MandateSignerLevel NivelDe(string? origen) => origen switch
    {
        MandateSignerOrigins.Compania => MandateSignerLevel.PropioDeCompania,
        MandateSignerOrigins.Asociado => MandateSignerLevel.AsociadoDeOtraCompania,
        _ => MandateSignerLevel.OtParaCompania,
    };

    /// <summary>
    /// Motivo por el que un candidato NO cuenta, o <c>null</c> si cuenta. Persona jurídica y Formato en blanco
    /// no exigen firma personal; la Persona natural necesita firma válida (vigencia propia + biometría vigente
    /// o baúl vigente). <c>signs_physically</c> no hace válida la firma (P4).
    /// </summary>
    public static string? MotivoDescarte(MandateSignerCandidate c)
    {
        ArgumentNullException.ThrowIfNull(c);

        if (c.Eliminado)
        {
            return MandateSignerDiscardReasons.Eliminado;
        }

        if (c.SignerModel is MandateSignerOrigins.ModeloJuridica or MandateSignerOrigins.ModeloFormatoBlanco)
        {
            return null;
        }

        string? motivo = null;
        if (!c.FirmaValida)
        {
            motivo = c.MotivoSinFirma ?? MandateSignerDiscardReasons.SinValidacionAprobada;
        }
        else if (c.SignatureMethod == MandateSignerOrigins.FormaBaul && !c.BaulVigente)
        {
            // MandateSignerFirmaValidez da válido a «baúl» sin consultar el baúl (hallazgo ADR-0066).
            motivo = MandateSignerDiscardReasons.BaulSinFirmaVigente;
        }

        return motivo is not null && c.FirmaFisica && MandateSignerDiscardReasons.EsDeFirma(motivo)
            ? MandateSignerDiscardReasons.FirmaFisicaSinMigrar
            : motivo;
    }

    private static MandateSignerCandidate? MejorFirmaFisicaPendiente(
        IReadOnlyList<MandateSignerCandidate> candidatos,
        MandateSignerCandidate? defaultDelOt,
        List<MandateSignerDiscard> descartados)
    {
        var porId = candidatos.Concat(defaultDelOt is null ? [] : [defaultDelOt]).GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First());
        return descartados
            .Where(d => d.Motivo == MandateSignerDiscardReasons.FirmaFisicaSinMigrar)
            .OrderBy(d => d.Level)
            .Select(d => porId[d.SignerId])
            .FirstOrDefault();
    }
}
