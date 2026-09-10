namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Tipos de documento emitidos SIN trámite (Feature #12201, ADR-0056-generacion-documental-standalone).
/// Los literales son los mismos del CHECK <c>ck_standalone_documents_document_type</c>
/// (<c>105-F12201-generacion-documental.sql</c>): la BD es la fuente de verdad y aquí solo se nombran.
/// <para><b>Ojo:</b> <c>transferencia_dominio_generada</c> NO es <c>transferencia_dominio</c>; este
/// último ya existe como tipo ADJUNTABLE del catálogo documental y designa otra cosa.</para>
/// </summary>
public static class StandaloneDocumentType
{
    public const string CertificadoRues = "certificado_rues";

    public const string TransferenciaDominioGenerada = "transferencia_dominio_generada";
}
