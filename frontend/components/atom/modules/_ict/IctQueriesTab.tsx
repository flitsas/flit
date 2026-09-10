"use client";

// «Consultas» de ICT: la consola compartida, amarrada al catálogo y a la API de pre-trámites de
// Integración con Terceros.
//
// Todo el comportamiento —las fichas de filtro, el enlace vivo, el aviso de cobertura, el export
// que recorre todas las páginas— vive en `@/components/consultas/QueryConsole` y lo comparte con
// las consultas del organismo y de la empresa gestora. Aquí solo queda lo que es propio de ICT: qué
// columnas tiene su tabla, sobre qué fechas filtra y a qué endpoint llama.

import { useMemo } from "react";
import { QueryConsole } from "@/components/consultas/QueryConsole";
import type { QuerySource, SavedQuery } from "@/lib/api/queries";
import {
  deleteIctSavedQuery,
  fetchIctQueryFields,
  fetchIctSavedQueries,
  ICT_DATE_FIELDS,
  runIctQuery,
  saveIctQuery,
  type IctQueryRow,
} from "@/lib/api/ict-queries";
import {
  defaultIctQueryColumns,
  ICT_QUERY_COLUMNS,
  ICT_QUERY_PRESETS,
  ictEstadoMeta,
} from "./ict-query-columns";

export interface IctQueriesTabProps {
  /** Solo SuperAdmin: la compañía cuyos pre-trámites se están mirando. El resto va con la suya. */
  tenantId?: string;
  /**
   * "Programar este informe" en una consulta guardada de ICT. Sin esto el botón no aparece (mismo
   * criterio que Consultas del organismo).
   */
  onScheduleQuery?: (query: SavedQuery) => void;
}

export function IctQueriesTab({ tenantId, onScheduleQuery }: IctQueriesTabProps) {
  // El origen se memoriza por compañía: la consola lo usa como dependencia de sus efectos, y una
  // identidad nueva en cada render relanzaría el catálogo y las guardadas sin parar.
  const source = useMemo<QuerySource<IctQueryRow>>(
    () => ({
      testIdPrefix: "ict-query",
      dateFields: ICT_DATE_FIELDS,
      defaultDateField: "registro",
      columnsStorageKey: "flit-ict-consultas-columnas",
      exportPrefix: "consulta-ict",
      rowNoun: ["pre-trámite", "pre-trámites"],
      fetchFields: (signal) => fetchIctQueryFields(tenantId, signal),
      run: (definition, options) => runIctQuery(definition, { ...options, tenantId }),
      fetchSaved: (signal) => fetchIctSavedQueries(tenantId, signal),
      save: (input) => saveIctQuery(input, tenantId),
      remove: (id) => deleteIctSavedQuery(id, tenantId),
      onSchedule: onScheduleQuery,
    }),
    [tenantId, onScheduleQuery],
  );

  return (
    <QueryConsole
      source={source}
      columns={ICT_QUERY_COLUMNS}
      presets={ICT_QUERY_PRESETS}
      defaultColumns={defaultIctQueryColumns()}
      rowKey={(fila) => fila.id}
      sheetName="Consulta ICT"
      ambito="tu empresa"
      rootTestId="ict-queries-tab"
      renderCell={(columnId, fila) =>
        columnId === "estado" ? (
          <span
            className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
            style={{
              background: `${ictEstadoMeta(fila.estado).color}1A`,
              color: ictEstadoMeta(fila.estado).color,
            }}
          >
            {ictEstadoMeta(fila.estado).label}
          </span>
        ) : null
      }
    />
  );
}
