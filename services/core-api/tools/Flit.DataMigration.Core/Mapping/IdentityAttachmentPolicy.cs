using Flit.DataMigration.V1.Source;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Decide qué imágenes sueltas de la validación de identidad NO se migran porque la carta selfie
/// ya las contiene.
///
/// <para>
/// El flujo de validación de identidad de V1 deja tres JPG por parte: <c>frontalCard.jpg</c>,
/// <c>backCard.jpg</c> y <c>userSelfie.jpg</c>. Los tres van al mismo <c>tipo</c> de V2
/// (<c>cedulas</c>) y los tres quedan embebidos, junto con nombre, tipo y número de documento,
/// hash de la transacción, firma y fecha de validación, dentro de la carta selfie que materializa
/// la instancia 3. Migrarlos además por separado llena el expediente de adjuntos que dicen lo mismo
/// que un PDF que ya está ahí — en producción son ~70.000 archivos en traspaso y ~11.700 en
/// matrícula.
/// </para>
///
/// <para>
/// La decisión es POR PARTE y replica exactamente la condición con la que V1 decide construir la
/// carta: la parte tiene la identidad validada y tiene selfie. Es literalmente el mismo <c>if</c> en
/// los dos trámites —<c>vehicleTransferPdfUnionDraftService</c> y
/// <c>vehicleRegistrationConsolidatePdfService</c>—, solo cambian los nombres de las columnas. Si
/// esa condición no se cumple, V1 tampoco produce la carta y las imágenes son la única evidencia
/// que existe: entonces se migran. En la copia de producción eso ocurre en 375 traspasos (36 del
/// comprador, 339 del vendedor).
/// </para>
///
/// <para>
/// Lo que NO entra aquí: <c>id_attached_*_id</c> (<c>buyer</c>/<c>seller</c>/<c>owner</c>). Esos son
/// PDF que el usuario cargó a mano (facturas, cédulas escaneadas, documentos con nombre propio) y la
/// carta selfie no los contiene. Se migran siempre.
/// </para>
/// </summary>
public sealed class IdentityAttachmentPolicy
{
    /// <summary>Las imágenes que produce la validación de identidad de una parte.</summary>
    /// <param name="Nombre">Cómo se llama la parte en el reporte al operador.</param>
    /// <param name="ValidationColumn">Bandera de V1 que dice que la identidad quedó validada.</param>
    /// <param name="FaceColumn">Selfie: sin ella V1 no arma la carta aunque la identidad esté validada.</param>
    /// <param name="ImageColumns">Las columnas que la carta absorbe.</param>
    /// <param name="SelfieLetterPieceKey">
    /// Clave con la que la instancia 3 entrega la carta de esta parte. Es lo que permite
    /// VERIFICAR la predicción: la instancia 2 descartó imágenes apostando a que esta pieza
    /// llegaría.
    /// </param>
    private sealed record Party(
        string Nombre,
        string ValidationColumn,
        string FaceColumn,
        string[] ImageColumns,
        string SelfieLetterPieceKey);

    private readonly Party[] _parties;

    private IdentityAttachmentPolicy(Party[] parties) => _parties = parties;

    /// <summary>Traspaso: dos partes, comprador y vendedor, con cartas independientes.</summary>
    public static readonly IdentityAttachmentPolicy Transfer = new([
        new("comprador",
            "buyer_validation_identity",
            "id_attach_image_face_buyer",
            ["id_attach_document_front_buyer", "id_attach_document_back_buyer", "id_attach_image_face_buyer"],
            "getLetterSelfieBuyerName"),
        new("vendedor",
            "seller_validation_identity",
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
    public static readonly IdentityAttachmentPolicy Registration = new([
        new("titular",
            "owner_validation_identity",
            "id_attach_image_face_owner",
            ["id_attach_document_front_owner", "id_attach_document_back_owner", "id_attach_image_face_owner"],
            "getLetterSelfieOwner"),
    ]);

    /// <summary>
    /// Columnas de imagen que la carta selfie ya cubre para este trámite, con el motivo listo para
    /// el reporte. Vacío si ninguna parte cumple la condición.
    /// </summary>
    public IReadOnlyDictionary<string, string> RedundantColumns(V1SourceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var redundant = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var party in _parties)
        {
            if (!CartaSelfieAplica(record, party))
            {
                continue;
            }

            foreach (var column in party.ImageColumns)
            {
                redundant[column] = $"la carta selfie del {party.Nombre} ya contiene esta imagen";
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
    /// Misma condición que usa V1 para construir la carta: identidad validada y selfie presente.
    /// Si esto cambia en V1, esto tiene que cambiar con ello o se pierden imágenes.
    /// </summary>
    private static bool CartaSelfieAplica(V1SourceRecord record, Party party) =>
        string.Equals(record.Column(party.ValidationColumn), "true", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(record.Column(party.FaceColumn));
}
