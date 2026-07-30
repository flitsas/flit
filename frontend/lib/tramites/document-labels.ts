// Nombre amigable de cada tipo de documento del trámite, para las listas de "Documentos generados"
// y "Documentos del expediente". Sin entrada → se humaniza el código (capitalize por palabra).

const DOCUMENT_LABELS: Record<string, string> = {
  fur: 'FUR',
  consolidado: 'Expediente consolidado',
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
  licencia_transito: 'Licencia de tránsito',
  factura: 'Factura',
  aduana: 'Declaración de importación',
  impronta: 'Impronta',
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
