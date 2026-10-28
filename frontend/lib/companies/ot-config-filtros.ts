/**
 * Filtros por columna de las tablas de organismos de tránsito de Compañías (HU #14003,
 * Feature #13996, Épica #13514): «Organismos de tránsito» (OTConfigTable), «Bloqueos» editable
 * (TransitBlocksPanel) y «Bloqueos» solo lectura (TransitBlocksReadOnlySection).
 * Solo DECLARA columnas y accesores; el estado y la barra son los genéricos de la HU #13997.
 */
import type { ColumnaFiltro, CondicionFiltro } from "@/lib/tablas/filtrar-filas";
import { filtrarFilas } from "@/lib/tablas/filtrar-filas";
import type { TransitOffice } from "@/lib/api/types";

export const COLUMNAS_TEXTO_OT: ColumnaFiltro[] = [
  { id: "name", label: "Organismo", tipo: "texto" },
  { id: "code", label: "Código", tipo: "texto" },
];

const BUSQUEDA_OT = (o: TransitOffice) => [o.name, o.code] as const;

/** Organismos de tránsito: Organismo, Código, Estado y, solo si la tabla pinta Convenio, Convenio. */
export function columnasFiltroOtConfig(conConvenio: boolean): ColumnaFiltro[] {
  return [
    ...COLUMNAS_TEXTO_OT,
    {
      id: "estado",
      label: "Estado",
      tipo: "seleccion",
      opciones: [
        { value: "habilitado", label: "Habilitado" },
        { value: "no_habilitado", label: "No habilitado" },
      ],
    },
    ...(conConvenio
      ? [
          {
            id: "convenio",
            label: "Convenio",
            tipo: "seleccion" as const,
            opciones: [
              { value: "con", label: "Con convenio" },
              { value: "sin", label: "Sin convenio" },
            ],
          },
        ]
      : []),
  ];
}

export function filtrarOrganismosOtConfig(
  offices: readonly TransitOffice[],
  condiciones: readonly CondicionFiltro[],
  busqueda: string,
  habilitados: ReadonlySet<string>,
  convenios: ReadonlySet<string>,
): TransitOffice[] {
  return filtrarFilas(offices, condiciones, busqueda, {
    columnas: {
      name: (o) => o.name,
      code: (o) => o.code,
      estado: (o) => (habilitados.has(o.id) ? "habilitado" : "no_habilitado"),
      convenio: (o) => (convenios.has(o.id) ? "con" : "sin"),
    },
    busqueda: BUSQUEDA_OT,
  });
}

/** Bloqueos editable: Organismo, Código, Estado en red. */
export const COLUMNAS_FILTRO_BLOQUEOS: ColumnaFiltro[] = [
  ...COLUMNAS_TEXTO_OT,
  {
    id: "estado",
    label: "Estado en red",
    tipo: "seleccion",
    opciones: [
      { value: "bloqueado", label: "Bloqueado" },
      { value: "disponible", label: "Disponible" },
    ],
  },
];

export function filtrarBloqueos(
  offices: readonly TransitOffice[],
  condiciones: readonly CondicionFiltro[],
  busqueda: string,
  bloqueados: ReadonlySet<string>,
): TransitOffice[] {
  return filtrarFilas(offices, condiciones, busqueda, {
    columnas: {
      name: (o) => o.name,
      code: (o) => o.code,
      estado: (o) => (bloqueados.has(o.id) ? "bloqueado" : "disponible"),
    },
    busqueda: BUSQUEDA_OT,
  });
}

/** Bloqueos solo lectura: todas las filas están bloqueadas, pero la columna Estado se filtra igual. */
export const COLUMNAS_FILTRO_BLOQUEOS_LECTURA: ColumnaFiltro[] = [
  { id: "name", label: "Organismo bloqueado", tipo: "texto" },
  { id: "code", label: "Código", tipo: "texto" },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: [{ value: "bloqueado", label: "Bloqueado" }],
  },
];

export function filtrarBloqueosLectura(
  offices: readonly TransitOffice[],
  condiciones: readonly CondicionFiltro[],
  busqueda: string,
): TransitOffice[] {
  return filtrarFilas(offices, condiciones, busqueda, {
    columnas: {
      name: (o) => o.name,
      code: (o) => o.code,
      estado: () => "bloqueado",
    },
    busqueda: BUSQUEDA_OT,
  });
}
