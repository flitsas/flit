/**
 * Lista QA de campos cuyo texto cambió (HU #12694, Feature #12689).
 * Filas N/A, RN-07 o Ganador igual al texto actual no se certifican.
 */

export type QaChangedField = {
  id: string;
  superficie: string;
  textoAnterior: string;
  textoGanador: string;
};

/** Inventario de divergencia: solo entra a certificación si anterior ≠ ganador y ganador ≠ N/A. */
const GLOSSARY_DELTAS: readonly QaChangedField[] = [
  {
    id: 'A01',
    superficie: 'Listado OT',
    textoAnterior: 'Propietario / vendedor',
    textoGanador: 'Vendedor',
  },
  {
    id: 'A03',
    superficie: 'Listado OT',
    textoAnterior: 'Tipo trámite',
    textoGanador: 'Trámite',
  },
  {
    id: 'A04',
    superficie: 'Listado gestor',
    textoAnterior: 'Fecha de creación / Fecha de actualización',
    textoGanador: 'Fecha radicación',
  },
  {
    id: 'A05',
    superficie: 'Listado / wizard gestor',
    textoAnterior: 'Secretaría · Secretaría de destino',
    textoGanador: 'Organismo de tránsito',
  },
  {
    id: 'A05',
    superficie: 'OCR LT OT',
    textoAnterior: 'Organismo',
    textoGanador: 'Organismo de tránsito',
  },
  {
    id: 'A06',
    superficie: 'Listado OT (persona)',
    textoAnterior: 'Empresa / Gestor',
    textoGanador: 'Gestor',
  },
  {
    id: 'A07',
    superficie: 'Detalle OT',
    textoAnterior: 'Actores del Trámite',
    textoGanador: 'Actores del trámite',
  },
  {
    id: 'A08',
    superficie: 'Detalle OT',
    textoAnterior: 'Especificaciones del vehículo',
    textoGanador: 'Especificaciones técnicas',
  },
  {
    id: 'A09',
    superficie: 'Detalle gestor y OT',
    textoAnterior: 'N. Motor / N. Chasis / N. Serie',
    textoGanador: 'Nº Motor / Nº Chasis / Nº Serie',
  },
  {
    id: 'A10',
    superficie: 'Wizard gestor',
    textoAnterior: 'Pasajeros',
    textoGanador: 'Capacidad',
  },
  {
    id: 'A12',
    superficie: 'Detalle OT',
    textoAnterior: 'Ver consolidado del expediente',
    textoGanador: 'Ver consolidado',
  },
  {
    id: 'A13',
    superficie: 'Listado OT',
    textoAnterior: 'Exportar a Excel',
    textoGanador: 'Exportar',
  },
  {
    id: 'A17',
    superficie: 'H1 módulo Identidad (ambos)',
    textoAnterior: 'Validaciones',
    textoGanador: 'Identidad',
  },
  {
    id: 'A23',
    superficie: 'H1 Usuarios OT',
    textoAnterior: 'Administración OT — Usuarios',
    textoGanador: 'Usuarios',
  },
  {
    id: 'A24',
    superficie: 'Checklist gestor',
    textoAnterior: 'SOAT vigente',
    textoGanador: 'SOAT',
  },
  {
    id: 'A24',
    superficie: 'Ficha OT',
    textoAnterior: 'SOAT RUNT',
    textoGanador: 'SOAT',
  },
  {
    id: 'A25',
    superficie: 'Intro Centro de Ayuda',
    textoAnterior: 'términos no unificados en intro',
    textoGanador: 'Gestor · Organismo de tránsito',
  },
  {
    id: 'D01',
    superficie: 'Correo trámite aprobado',
    textoAnterior: 'APROBADO',
    textoGanador: 'Aprobado',
  },
  {
    id: 'D02',
    superficie: 'Correo trámite rechazado',
    textoAnterior: 'RECHAZADO',
    textoGanador: 'Rechazado',
  },
  {
    id: 'D06',
    superficie: 'PDF FUR',
    textoAnterior: 'Secretaría / Organismo',
    textoGanador: 'Organismo de tránsito',
  },
  {
    id: 'E01',
    superficie: 'Detalle OT',
    textoAnterior: 'Aprobar trámite / Rechazar trámite',
    textoGanador: 'Aprobar / Rechazar',
  },
  {
    id: 'E02',
    superficie: 'Diálogo OT',
    textoAnterior: 'Adjuntar Licencia de Tránsito (LT)',
    textoGanador: 'Adjuntar LT',
  },
  {
    id: 'E03',
    superficie: 'Dashboard gestor',
    textoAnterior: 'Total Trámites',
    textoGanador: 'Total trámites',
  },
  {
    id: 'E04',
    superficie: 'Listado / acordeón gestor',
    textoAnterior: 'Secretaría · Secretaría de destino',
    textoGanador: 'Organismo de tránsito',
  },
  {
    id: 'E05',
    superficie: 'Toast consolidado',
    textoAnterior: 'Consolidado regenerado. / Consolidado cargado.',
    textoGanador: 'Consolidado generado.',
  },
];

/** Filas que no se certifican (N/A, RN-07 o Ganador = actual). */
export const QA_EXCLUDED_GLOSSARY_IDS = [
  'A02',
  'A11',
  'A14',
  'A15',
  'A16',
  'A18',
  'A19',
  'A20',
  'A21',
  'A22',
  'B01',
  'B02',
  'B03',
  'B04',
  'B05',
  'B06',
  'B07',
  'B08',
  'B09',
  'B10',
  'B11',
  'B12',
  'B13',
  'B14',
  'B15',
  'B16',
  'B17',
  'B18',
  'B19',
  'B20',
  'B21',
  'B22',
  'B23',
  'D03',
  'D04',
  'D05',
  'D07',
] as const;

function isCertifiableChange(row: QaChangedField): boolean {
  if (row.textoGanador.trim() === '' || row.textoGanador === 'N/A') return false;
  return row.textoAnterior.trim() !== row.textoGanador.trim();
}

export function qaChangedFields(): QaChangedField[] {
  return GLOSSARY_DELTAS.filter(isCertifiableChange);
}

export function qaChangedFieldIds(): string[] {
  return [...new Set(qaChangedFields().map((row) => row.id))];
}

export const QA_LIST_REPO_PATH = 'docs/ado-drafts/epic-12551/lista-qa-campos-cambiados.md';
