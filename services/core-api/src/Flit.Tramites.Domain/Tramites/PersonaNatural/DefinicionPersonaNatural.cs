using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.PersonaNatural;

/// <summary>Un rol de actor que el portal debe pedir para el tipo, derivado del perfil de capacidades (sin copiarlo entero).</summary>
public sealed record ActorPortal(string Rol, string Etiqueta, int Min, int Max, bool Biometria, bool Firma, bool PersonaNaturalPermitida);

/// <summary>Las capacidades del tipo que el portal necesita, con nombres propios del contrato (no los del perfil interno).</summary>
public sealed record CapacidadesPortal(
    string EntryMode,
    bool RequiereComercial,
    bool RequierePrenda,
    bool PermiteTransformaciones,
    bool PermitePrendaComplementaria,
    bool RequiereBiometria,
    bool RequiereSolicitudPlaca);

/// <summary>
/// HU #14078 (Feature K, Épica #14021) — proyecta lo que FLIT 2 ya sabe de un tipo (su <c>gate_profile</c> y sus reglas
/// documentales) a la estructura que consume el portal de persona natural. Solo lectura y sin reglas nuevas: cada
/// respuesta sale de los mismos resolvedores que usa el wizard de FLIT 2 (<c>ResolveSignatureActors</c>,
/// <c>ComplementaryTransformationsAllowed</c>, <c>ConditionalDocumentRules</c>…). Ningún tipo se nombra aquí.
/// </summary>
public static class DefinicionPersonaNatural
{
    /// <summary>Tope de actores por lado (ADR-0053: ordinal 1..4) cuando el perfil admite varios.</summary>
    public const int MaximoPorLado = 4;

    public const string RolVendedor = "vendedor";
    public const string RolComprador = "comprador";
    public const string RolLocatario = "locatario";

    // Vocabulario de ProcedureTypeGateProfile.BiometricActors / SignatureActors.
    private const string ActorPropietario = "OWNER";
    private const string ActorComprador = "BUYER";
    private const string ActorLocatario = "LESSEE";

    /// <summary>
    /// Roles del tipo. <c>min</c>/<c>max</c> no existen en la configuración: se derivan de <c>requiresX</c> (1) o ausente (0)
    /// y de <c>allowsMultipleX</c> (tope ADR-0053). Un tipo de un solo titular declara solo el comprador: se llama «titular».
    /// </summary>
    public static IReadOnlyList<ActorPortal> Actores(ProcedureTypeGateProfile perfil, bool personaNaturalPermitida)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        var firman = perfil.ResolveSignatureActors();
        bool Biometria(string actor) => perfil.RequiresBiometrics && perfil.BiometricActors.Contains(actor, StringComparer.OrdinalIgnoreCase);
        bool Firma(string actor) => perfil.RequiresSignature && firman.Contains(actor, StringComparer.OrdinalIgnoreCase);

        var actores = new List<ActorPortal>(3);
        if (perfil.RequiresSeller)
            actores.Add(new(RolVendedor, "Vendedor", 1, perfil.AllowsMultipleSeller ? MaximoPorLado : 1, Biometria(ActorPropietario), Firma(ActorPropietario), personaNaturalPermitida));

        if (perfil.RequiresBuyer)
            actores.Add(new(RolComprador, perfil.RequiresSeller ? "Comprador" : "Titular", 1, perfil.AllowsMultipleBuyer ? MaximoPorLado : 1,
                Biometria(ActorComprador), Firma(ActorComprador), personaNaturalPermitida));

        if (perfil.RequiresLessee)
            actores.Add(new(RolLocatario, "Locatario", 1, 1, Biometria(ActorLocatario), Firma(ActorLocatario), personaNaturalPermitida));

        return actores;
    }

    /// <summary>Capacidades del tipo con nombres del contrato; cada una con el mismo resolvedor que usa FLIT 2.</summary>
    public static CapacidadesPortal Capacidades(ProcedureTypeGateProfile perfil, string? familia)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        return new CapacidadesPortal(
            // Sin modo declarado el tipo acepta cualquiera de los dos identificadores.
            perfil.EntryMode ?? ProcedureTypeGateProfile.EntryModeBoth,
            perfil.RequiresCommercialValue,
            perfil.HasPrendaGate,
            perfil.ComplementaryTransformationsAllowed(familia),
            perfil.ComplementaryPrendaAllowed(familia),
            perfil.RequiresBiometrics,
            perfil.RequiresPlateRequest);
    }

    /// <summary>Un documento adicional que una regla condicional agrega o exige y que no está en la matriz base del tipo.</summary>
    public sealed record DocumentoCondicional(string DocCode, string Nombre, bool Obligatorio, string Condicion);

    /// <summary>
    /// Condiciones legibles de los documentos del tipo según <see cref="ConditionalDocumentRules"/>: por código de documento
    /// (para anotar los de la matriz) y los documentos que solo existen cuando la condición se cumple. Las reglas viven en
    /// código y no son serializables: aquí solo se les pone texto.
    /// </summary>
    public static (IReadOnlyDictionary<string, string> PorDocumento, IReadOnlyList<DocumentoCondicional> Adicionales) CondicionesDocumentales(string? tipoCodigo)
    {
        var porDoc = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var adicionales = new List<DocumentoCondicional>();
        foreach (var regla in ConditionalDocumentRules.For(tipoCodigo))
        {
            var doc = regla.Item.DocTipo ?? regla.Item.Id;
            var texto = TextoDeRegla(regla);
            if (!porDoc.TryGetValue(doc, out var lista))
                porDoc[doc] = lista = [];
            if (!lista.Contains(texto, StringComparer.Ordinal))
                lista.Add(texto);

            if (regla.Effect != ConditionalEffect.Hide && !adicionales.Any(a => string.Equals(a.DocCode, doc, StringComparison.OrdinalIgnoreCase)))
                adicionales.Add(new DocumentoCondicional(doc, regla.Item.Label, regla.Effect == ConditionalEffect.Require && regla.Item.Obligatorio, texto));
        }

        return (porDoc.ToDictionary(kv => kv.Key, kv => string.Join("; ", kv.Value), StringComparer.OrdinalIgnoreCase), adicionales);
    }

    /// <summary>Texto en lenguaje claro de una regla condicional (por su <c>Id</c> estable). Una regla sin texto cae a uno genérico.</summary>
    public static string TextoDeRegla(ConditionalRule regla)
    {
        ArgumentNullException.ThrowIfNull(regla);
        return regla.Id switch
        {
            "tramitador_poder" => "Se pide si el trámite lo gestiona un tramitador.",
            "carroceria_factura" => "Se pide si el trámite cambia la carrocería.",
            "servicio_especial_anexos" => "Se pide si el vehículo es de servicio especial.",
            "nit_sin_cedula" => "No se pide si la parte es una persona jurídica (NIT).",
            "pn_sin_cedula" => "No se pide si la parte es persona natural: su identidad se valida de forma digital.",
            "mandato_autogenerado" => "Lo genera el sistema cuando la parte actúa por mandato.",
            "leasing_contrato" or "leasing_declaracion" => "Se pide si el vehículo está en leasing.",
            var id when id.StartsWith("cancelacion_", StringComparison.Ordinal) =>
                regla.Effect == ConditionalEffect.Hide
                    ? "No se pide cuando la causal de cancelación elegida no lo exige."
                    : "Se pide cuando la causal de cancelación elegida lo exige.",
            _ => "Se pide solo cuando se cumple una condición del trámite.",
        };
    }
}
