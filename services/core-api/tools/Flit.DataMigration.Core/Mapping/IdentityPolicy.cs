using System.Globalization;
using Flit.DataMigration.V1.Source;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Todo lo que la migración necesita saber sobre la VALIDACIÓN DE IDENTIDAD de un trámite de V1:
/// quiénes son las partes, cómo se sabe si cada una quedó validada, qué imágenes produjo y con qué
/// nombre entrega V1 su carta selfie.
///
/// <para>
/// Las dos decisiones que salen de aquí son distintas pero se apoyan en los MISMOS datos, y por eso
/// viven juntas: si las partes de traspaso y matrícula estuvieran definidas en dos listas separadas,
/// tarde o temprano una se actualizaría sin la otra y el desajuste sería silencioso —una identidad
/// marcada como aprobada cuyas imágenes sí se migraron, o al revés—.
/// </para>
/// <list type="number">
///   <item><b>Qué imágenes NO se migran</b> (<see cref="RedundantColumns"/>).</item>
///   <item><b>Qué partes quedaron validadas en V1</b> (<see cref="AprobadasEnV1"/>), para que V2 no
///   vuelva a pedir una validación que ya se hizo.</item>
/// </list>
///
/// <para>
/// <b>Imágenes.</b> El flujo de validación de identidad de V1 deja tres JPG por parte:
/// <c>frontalCard.jpg</c>, <c>backCard.jpg</c> y <c>userSelfie.jpg</c>. Los tres van al mismo
/// <c>tipo</c> de V2 (<c>cedulas</c>) y los tres quedan embebidos, junto con nombre, tipo y número de
/// documento, hash de la transacción, firma y fecha de validación, dentro de la carta selfie que
/// materializa la instancia 3. Migrarlos además por separado llena el expediente de adjuntos que
/// dicen lo mismo que un PDF que ya está ahí — en producción son ~70.000 archivos en traspaso y
/// ~11.700 en matrícula.
/// </para>
///
/// <para>
/// Las imágenes NO se migran NUNCA, pero por dos motivos distintos según la parte, y el reporte
/// dice cuál le tocó a cada una:
/// </para>
/// <list type="bullet">
///   <item><b>Identidad validada en V1</b> — misma condición con la que V1 decide construir la
///   carta (bandera de validación + selfie; literalmente el mismo <c>if</c> de
///   <c>vehicleTransferPdfUnionDraftService</c> y <c>vehicleRegistrationConsolidatePdfService</c>):
///   la carta que trae la instancia 3 ya las contiene.</item>
///   <item><b>Identidad NO validada</b> (pendiente o rechazada) — V1 tampoco produjo la carta, y esa
///   validación no sirve en V2: se rehace con Kyverum. Arrastrar los JPG de un intento que no
///   prosperó ensucia el expediente con evidencia de algo que no ocurrió. En la copia de producción
///   son 375 traspasos (36 del comprador, 339 del vendedor).</item>
/// </list>
/// <para>
/// Por eso <see cref="PartiesSinCartaSelfie"/> sigue existiendo: avisa solo cuando la parte SÍ
/// estaba validada y aun así la carta no llegó, que es el único caso en que se pierde evidencia.
/// </para>
///
/// <para>
/// Lo que NO entra aquí: <c>id_attached_*_id</c> (<c>buyer</c>/<c>seller</c>/<c>owner</c>). Esos son
/// PDF que el usuario cargó a mano (facturas, cédulas escaneadas, documentos con nombre propio) y la
/// carta selfie no los contiene. Se migran siempre.
/// </para>
/// </summary>
public sealed class IdentityPolicy
{
    /// <summary>Una parte del trámite con validación de identidad propia.</summary>
    /// <param name="Nombre">Cómo se llama la parte en el reporte al operador.</param>
    /// <param name="PartyRole">
    /// Rol con el que la parte quedó en V2 (<c>procedure_instance_actors.actor_type</c>). NO siempre
    /// coincide con <paramref name="Nombre"/>: el titular de matrícula inicial se migra como actor
    /// <c>comprador</c> (ver <c>RegistrationMapper.ActorTitular</c>), que es lo que esperan el gate de
    /// identidad y el wizard. Emparejarlos mal deja la validación colgando de un rol que nadie mira.
    /// </param>
    /// <param name="ValidationColumn">Bandera de V1 que dice que la identidad quedó validada.</param>
    /// <param name="PhysicalSignatureColumn">
    /// Bandera de V1 de FIRMA FÍSICA: el cliente firmó en papel ante el gestor y por eso no hubo
    /// validación biométrica. Cuenta como identidad acreditada igual que la biométrica.
    /// </param>
    /// <param name="ApprovalDateColumn">Cuándo se aprobó la identidad en V1.</param>
    /// <param name="FaceColumn">Selfie: sin ella V1 no arma la carta aunque la identidad esté validada.</param>
    /// <param name="ImageColumns">Las columnas que la carta absorbe.</param>
    /// <param name="SelfieLetterPieceKey">
    /// Clave con la que la instancia 3 entrega la carta de esta parte. Es lo que permite
    /// VERIFICAR la predicción: la instancia 2 descartó imágenes apostando a que esta pieza
    /// llegaría.
    /// </param>
    private sealed record Party(
        string Nombre,
        string PartyRole,
        string ValidationColumn,
        string PhysicalSignatureColumn,
        string ApprovalDateColumn,
        string FaceColumn,
        string[] ImageColumns,
        string SelfieLetterPieceKey);

    private readonly Party[] _parties;

    private IdentityPolicy(Party[] parties) => _parties = parties;

    /// <summary>Traspaso: dos partes, comprador y vendedor, con cartas independientes.</summary>
    public static readonly IdentityPolicy Transfer = new([
        new("comprador",
            "comprador",
            "buyer_validation_identity",
            // Sin sufijo, a diferencia del vendedor: en V1 la columna del comprador es la original y
            // la del vendedor se añadió después. No es un descuido de este mapa.
            "is_fisical_signature",
            "identity_validation_date_approve_buyer",
            "id_attach_image_face_buyer",
            ["id_attach_document_front_buyer", "id_attach_document_back_buyer", "id_attach_image_face_buyer"],
            "getLetterSelfieBuyerName"),
        new("vendedor",
            "vendedor",
            "seller_validation_identity",
            "is_fisical_signature_seller",
            "identity_validation_date_approve_seller",
            "id_attach_image_face_seller",
            ["id_attach_document_front_seller", "id_attach_document_back_seller", "id_attach_image_face_seller"],
            "getLetterSelfieSellerName"),
    ]);

    /// <summary>
    /// Matrícula inicial: una sola parte, el titular. La carta se llama <c>getLetterSelfieOwner</c>
    /// y V1 tiene cuatro variantes de armado (tramitador, persona jurídica, multipropietario,
    /// propietario único) — todas producen la MISMA pieza, así que a efectos de esta política es
    /// una sola. En pdn son 3.905 trámites con las tres imágenes.
    /// </summary>
    public static readonly IdentityPolicy Registration = new([
        new("titular",
            // El titular se migra como actor 'comprador' — ver la nota de PartyRole.
            "comprador",
            "owner_validation_identity",
            "is_fisical_signature_owner",
            "identity_validation_date_approve_owner",
            "id_attach_image_face_owner",
            ["id_attach_document_front_owner", "id_attach_document_back_owner", "id_attach_image_face_owner"],
            "getLetterSelfieOwner"),
    ]);

    /// <summary>
    /// Columnas de imagen de identidad que NO se migran, con el motivo listo para el reporte.
    /// Siempre las tres por parte: cambia el porqué, no el resultado.
    /// </summary>
    public IReadOnlyDictionary<string, string> RedundantColumns(V1SourceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var redundant = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var party in _parties)
        {
            // El motivo importa tanto como la exclusión: son dos situaciones distintas y el operador
            // tiene que poder distinguirlas leyendo el reporte, sin ir a consultar V1.
            var motivo = CartaSelfieAplica(record, party)
                ? $"la carta selfie del {party.Nombre} ya contiene esta imagen"
                : $"la identidad del {party.Nombre} no quedó validada en V1, así que esa validación se "
                    + "rehace en V2: sus imágenes no se migran";

            foreach (var column in party.ImageColumns)
            {
                redundant[column] = motivo;
            }
        }

        return redundant;
    }

    /// <summary>
    /// ¿Las partes cuyas imágenes se descartaron tienen efectivamente su carta selfie en V2?
    /// Es la contraparte de <see cref="RedundantColumns"/>: la instancia 2 descarta las imágenes
    /// prediciendo que la instancia 3 traerá la carta, y esto verifica que la predicción se cumplió.
    /// Devuelve el nombre de las partes para las que NO llegó.
    /// </summary>
    public IReadOnlyList<string> PartiesSinCartaSelfie(
        V1SourceRecord record,
        IReadOnlyCollection<string> pieceKeysRecibidas)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(pieceKeysRecibidas);

        var faltantes = new List<string>();
        foreach (var party in _parties)
        {
            if (!CartaSelfieAplica(record, party))
            {
                continue;
            }

            if (!pieceKeysRecibidas.Contains(party.SelfieLetterPieceKey, StringComparer.OrdinalIgnoreCase))
            {
                faltantes.Add(party.Nombre);
            }
        }

        return faltantes;
    }

    /// <summary>
    /// Partes cuya identidad quedó ACREDITADA en V1, por cualquiera de los dos caminos que V1 acepta:
    /// la validación biométrica aprobada, o la firma física ante el gestor. Es la lista que la
    /// instancia 3 convierte en validaciones aprobadas de V2.
    /// <para>
    /// Lo que NO está aquí es tan importante como lo que sí: una identidad pendiente o rechazada no
    /// aparece. Para esas, V2 no hereda nada y la validación se rehace con Kyverum — que es
    /// exactamente lo que debe pasar, porque en V1 tampoco llegó a ocurrir.
    /// </para>
    /// </summary>
    public IReadOnlyList<IdentityApproval> AprobadasEnV1(V1SourceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var aprobadas = new List<IdentityApproval>();
        foreach (var party in _parties)
        {
            var biometrica = EsVerdadero(record.Column(party.ValidationColumn));
            var firmaFisica = EsVerdadero(record.Column(party.PhysicalSignatureColumn));

            if (!biometrica && !firmaFisica)
            {
                continue;
            }

            aprobadas.Add(new IdentityApproval(
                Nombre: party.Nombre,
                PartyRole: party.PartyRole,
                // La biométrica manda cuando se dieron las dos: es la que dejó fecha y carta.
                PorFirmaFisica: !biometrica,
                AprobadaEnV1: ParseFecha(record.Column(party.ApprovalDateColumn)),
                SelfieLetterPieceKey: party.SelfieLetterPieceKey,
                // Solo la biométrica produce carta selfie; la firma física deja un PDF firmado que
                // migra la instancia 2 por su propia columna.
                ExigeCartaSelfie: biometrica && CartaSelfieAplica(record, party)));
        }

        return aprobadas;
    }

    /// <summary>
    /// Misma condición que usa V1 para construir la carta: identidad validada y selfie presente.
    /// Si esto cambia en V1, esto tiene que cambiar con ello o se pierden imágenes.
    /// </summary>
    private static bool CartaSelfieAplica(V1SourceRecord record, Party party) =>
        EsVerdadero(record.Column(party.ValidationColumn))
        && !string.IsNullOrWhiteSpace(record.Column(party.FaceColumn));

    /// <summary>
    /// Booleano de V1. Npgsql entrega los <c>boolean</c> como "True"/"False" al pasarlos a texto, pero
    /// la comparación va sin distinguir mayúsculas para no depender de eso.
    /// </summary>
    private static bool EsVerdadero(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fecha de V1 (<c>timestamp without time zone</c>, en UTC por convención del origen). Devuelve
    /// <c>null</c> ante cualquier valor que no parsee: la fecha es informativa y no vale la pena
    /// tumbar la migración de un trámite por ella.
    /// </summary>
    private static DateTimeOffset? ParseFecha(string? value) =>
        DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
}

/// <summary>Una parte cuya identidad V1 dio por acreditada, con el porqué y el cuándo.</summary>
/// <param name="Nombre">Nombre de la parte para el reporte ("comprador", "titular"…).</param>
/// <param name="PartyRole">Rol del actor en V2 con el que hay que emparejar la validación.</param>
/// <param name="PorFirmaFisica">
/// <c>true</c> si la acreditación viene de una firma en papel y no de la biométrica. Cambia la
/// evidencia que la respalda: no hay carta selfie ni hash de transacción, hay un PDF firmado.
/// </param>
/// <param name="AprobadaEnV1">Fecha de aprobación en V1, si V1 la registró.</param>
/// <param name="SelfieLetterPieceKey">Pieza del snapshot que soporta esta acreditación.</param>
/// <param name="ExigeCartaSelfie">
/// Si es <c>true</c>, no se debe afirmar nada en V2 mientras la carta no esté: es la única evidencia
/// de esa validación y sin ella la afirmación quedaría sin respaldo en el expediente.
/// </param>
public sealed record IdentityApproval(
    string Nombre,
    string PartyRole,
    bool PorFirmaFisica,
    DateTimeOffset? AprobadaEnV1,
    string SelfieLetterPieceKey,
    bool ExigeCartaSelfie);
