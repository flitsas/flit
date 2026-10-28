// Único punto de los documentos legales del portal. Ningún texto legal se escribe en el código: mientras no exista el
// documento oficial cada uno figura como «no disponible» y la interfaz solo muestra el aviso. Cuando se entregue, basta con
// poner aquí su URL (y `disponible: true`); los enlaces del pie, del modal «Iniciar mi trámite» y del chat leen de este módulo.

export interface DocumentoLegal {
  clave: string;
  titulo: string;
  /** URL oficial del documento. `null` mientras no exista. */
  url: string | null;
  disponible: boolean;
}

export const DOCUMENTOS_LEGALES = {
  politicas: {
    clave: "politicas",
    titulo: "Políticas de Privacidad y Términos de Uso",
    url: null,
    disponible: false,
  },
} as const satisfies Record<string, DocumentoLegal>;

/** Título del enlace legal del pie; sale del único registro de documentos. */
export const TEXTO_POLITICAS = DOCUMENTOS_LEGALES.politicas.titulo;

/** Mensaje del diálogo mientras un documento no tiene texto oficial. */
export const TEXTO_DOCUMENTO_NO_DISPONIBLE = "El documento oficial aún no está disponible.";
