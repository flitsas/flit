// Catálogo estático (mock) de organismos de tránsito de Colombia.
// Lista representativa de las principales secretarías/institutos de movilidad.
// Los `code` siguen un estilo DIVIPOLA (código de municipio) — son valores
// razonables para un mock; el contrato real con el RUNT puede afinarlos.

export interface OrganismoTransito {
  /** Código tipo DIVIPOLA del municipio sede del organismo. */
  code: string;
  /** Nombre oficial del organismo de tránsito. */
  name: string;
  /** Ciudad sede. */
  city: string;
}

/**
 * Catálogo de organismos de tránsito. Orden alfabético por ciudad para que la
 * búsqueda sea predecible. Para producción esto se reemplazaría por un fetch al
 * catálogo oficial del RUNT.
 */
export const ORGANISMOS_TRANSITO: OrganismoTransito[] = [
  { code: '05001', name: 'Secretaría de Movilidad de Medellín', city: 'Medellín' },
  { code: '05088', name: 'Tránsito de Bello', city: 'Bello' },
  { code: '05266', name: 'Instituto de Tránsito de Envigado', city: 'Envigado' },
  { code: '05360', name: 'Tránsito de Itagüí', city: 'Itagüí' },
  { code: '08001', name: 'Secretaría Distrital de Movilidad de Barranquilla', city: 'Barranquilla' },
  { code: '08758', name: 'Tránsito de Soledad', city: 'Soledad' },
  { code: '11001', name: 'Secretaría Distrital de Movilidad de Bogotá', city: 'Bogotá D.C.' },
  { code: '13001', name: 'Departamento Administrativo de Tránsito y Transporte de Cartagena', city: 'Cartagena' },
  { code: '15001', name: 'Instituto de Tránsito de Boyacá - Tunja', city: 'Tunja' },
  { code: '17001', name: 'Secretaría de Tránsito y Transporte de Manizales', city: 'Manizales' },
  { code: '19001', name: 'Secretaría de Tránsito y Transporte de Popayán', city: 'Popayán' },
  { code: '20001', name: 'Secretaría de Tránsito y Transporte de Valledupar', city: 'Valledupar' },
  { code: '23001', name: 'Secretaría de Tránsito y Transporte de Montería', city: 'Montería' },
  { code: '41001', name: 'Secretaría de Movilidad de Neiva', city: 'Neiva' },
  { code: '44001', name: 'Secretaría de Tránsito y Transporte de Riohacha', city: 'Riohacha' },
  { code: '47001', name: 'Secretaría de Movilidad de Santa Marta', city: 'Santa Marta' },
  { code: '50001', name: 'Secretaría de Movilidad de Villavicencio', city: 'Villavicencio' },
  { code: '52001', name: 'Secretaría de Tránsito y Transporte de Pasto', city: 'Pasto' },
  { code: '54001', name: 'Tránsito de Cúcuta', city: 'Cúcuta' },
  { code: '63001', name: 'Instituto de Tránsito y Transporte de Armenia', city: 'Armenia' },
  { code: '66001', name: 'Instituto de Tránsito de Pereira', city: 'Pereira' },
  { code: '68001', name: 'Dirección de Tránsito de Bucaramanga', city: 'Bucaramanga' },
  { code: '68276', name: 'Tránsito de Floridablanca', city: 'Floridablanca' },
  { code: '70001', name: 'Secretaría de Tránsito y Transporte de Sincelejo', city: 'Sincelejo' },
  { code: '73001', name: 'Secretaría de Movilidad de Ibagué', city: 'Ibagué' },
  { code: '76001', name: 'Secretaría de Movilidad de Cali', city: 'Cali' },
  { code: '76520', name: 'Tránsito de Palmira', city: 'Palmira' },
  { code: '76834', name: 'Tránsito de Tuluá', city: 'Tuluá' },
  { code: '85001', name: 'Secretaría de Tránsito y Transporte de Yopal', city: 'Yopal' },
  { code: '88001', name: 'Secretaría de Movilidad de San Andrés', city: 'San Andrés' },
];

/** Búsqueda case-insensitive por nombre, ciudad o código. */
export function filterOrganismos(query: string): OrganismoTransito[] {
  const q = query.trim().toLowerCase();
  if (!q) return ORGANISMOS_TRANSITO;
  return ORGANISMOS_TRANSITO.filter(
    (o) =>
      o.name.toLowerCase().includes(q) ||
      o.city.toLowerCase().includes(q) ||
      o.code.includes(q),
  );
}

/** Busca un organismo por nombre exacto (case-insensitive). */
export function findOrganismoByName(name: string): OrganismoTransito | undefined {
  const n = name.trim().toLowerCase();
  return ORGANISMOS_TRANSITO.find((o) => o.name.toLowerCase() === n);
}
