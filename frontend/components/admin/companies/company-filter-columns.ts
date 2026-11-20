// Columnas filtrables de las tablas de compañías (HU #13999, Feature #13996). Solo DECLARAN las
// columnas: la mecánica de filtros vive en el estándar (`TablaFiltrosBar`, HU #13997).
import { TENANT_TYPE_LABELS, B2B_TENANT_TYPES } from "@/lib/api/types";
import type { ColumnaFiltro, OpcionFiltro } from "@/lib/tablas/filtrar-filas";

/** Tipos de compañía con la misma etiqueta que muestran las tablas. */
export const OPCIONES_TIPO_COMPANIA: readonly OpcionFiltro[] = B2B_TENANT_TYPES.map((t) => ({
  value: t,
  label: TENANT_TYPE_LABELS[t],
}));

/** Estado como booleano serializado ("true"/"false"). */
export const OPCIONES_ESTADO_COMPANIA: readonly OpcionFiltro[] = [
  { value: "true", label: "Activa" },
  { value: "false", label: "Inactiva" },
];

/** Listado de compañías (modo servidor). */
export const COLUMNAS_LISTADO_COMPANIAS: readonly ColumnaFiltro[] = [
  { id: "codigo", label: "Código", tipo: "texto", param: "codigo" },
  { id: "nit", label: "NIT", tipo: "texto", param: "nit" },
  { id: "razonSocial", label: "Razón social", tipo: "texto", param: "razonSocial" },
  {
    id: "tipo",
    label: "Tipo",
    tipo: "seleccion",
    opciones: OPCIONES_TIPO_COMPANIA,
    param: "tenantType",
  },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: OPCIONES_ESTADO_COMPANIA,
    param: "estadoActivo",
  },
  {
    id: "fechaCreacion",
    label: "Fecha creación",
    tipo: "fecha",
    paramDesde: "fechaDesde",
    paramHasta: "fechaHasta",
  },
];

/** Compañías hijas / Panel de red (modo cliente). */
export function columnasHijas(clienteLabel: "Razón social" | "Cliente"): readonly ColumnaFiltro[] {
  return [
    { id: "razonSocial", label: clienteLabel, tipo: "texto" },
    { id: "nit", label: "NIT", tipo: "texto" },
    { id: "tipo", label: "Tipo", tipo: "seleccion", opciones: OPCIONES_TIPO_COMPANIA },
    {
      id: "estado",
      label: "Estado",
      tipo: "seleccion",
      opciones: [
        { value: "true", label: "Activo" },
        { value: "false", label: "Inactivo" },
      ],
    },
    { id: "vinculacion", label: "Vinculación", tipo: "fecha" },
  ];
}
