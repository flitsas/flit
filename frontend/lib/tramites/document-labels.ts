// Nombre amigable de cada tipo de documento del trámite, para las listas de "Documentos generados"
// y "Documentos del expediente". Sin entrada → se humaniza el código (capitalize por palabra).

const DOCUMENT_LABELS: Record<string, string> = {
  fur: 'FUR',
  consolidado: 'Consolidado',
  compraventa: 'Formato de compraventa',
  tramite_virtual: 'Solicitud de trámite virtual',
  mandato: 'Contrato de mandato',
  certificado_identidad: 'Certificado de identidad',
  certificado_identidad_vendedor: 'Certificado de identidad (vendedor)',
  certificado_rues: 'Certificado RUES',
  certificado_rnmc: 'Certificado RNMC (medidas correctivas)',
  certificado_soat_rtm: 'Certificado SOAT / RTM',
  escritura: 'Escritura',
  escritura_comprador: 'Escritura (comprador)',
  // Escritura cargada por el gestor para un representante legal que no está en el módulo de
  // representantes de la compañía (y que por tanto no tiene escritura en el directorio).
  escritura_representante: 'Escritura del representante legal',
  escritura_representante_vendedor: 'Escritura del representante legal (vendedor)',
  escritura_representante_locatario: 'Escritura del representante legal (locatario)',
  licencia_transito: 'Licencia de tránsito',
  factura: 'Factura',
  aduana: 'Declaración de importación',
  impronta: 'Improntas',
  soat: 'SOAT',
  paz_salvo_rnmc: 'Paz y salvo RNMC',
};

/** Nombre visible de un tipo de documento; si no está mapeado, humaniza el código crudo. */
export function documentLabel(tipo: string): string {
  const known = DOCUMENT_LABELS[tipo];
  if (known) return known;
  return tipo
    .split('_')
    .map((w) => (w ? w.charAt(0).toUpperCase() + w.slice(1) : w))
    .join(' ');
}

/**
 * Nombre definido al crear el tipo en el módulo documental.
 * Sin nombre de catálogo, se humaniza el código (listas de adjuntos sin `nombre`).
 */
export function catalogDocumentName(codigo: string, nombre?: string | null): string {
  const fromCatalog = nombre?.trim();
  if (fromCatalog) return fromCatalog;
  return documentLabel(codigo.trim());
}

/** Nombre de catálogo más el código, para aria-label y `<option>`. */
export function catalogDocumentTitle(codigo: string, nombre?: string | null): string {
  const name = catalogDocumentName(codigo, nombre);
  const code = codigo.trim();
  return code ? `${name} (${code})` : name;
}

/** Alias: el gestor y el admin muestran el mismo nombre de catálogo. */
export function gestorDocumentDisplayName(codigo: string, fallback?: string | null): string {
  return catalogDocumentName(codigo, fallback);
}
