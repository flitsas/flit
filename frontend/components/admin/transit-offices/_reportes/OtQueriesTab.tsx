"use client";

// «Consultas» del organismo: la consola compartida, amarrada al catálogo y a la API del OT.
//
// Todo el comportamiento —las fichas de filtro, el enlace vivo, el aviso de cobertura, el export
// que recorre todas las páginas— vive en `@/components/consultas/QueryConsole` y lo comparte con
// las consultas de la empresa gestora. Aquí solo queda lo que es del organismo: qué columnas tiene
// su tabla, sobre qué fechas filtra y a qué endpoints llama.

import { useMemo } from "react";
import { QueryConsole } from "@/components/consultas/QueryConsole";
import type { QuerySource } from "@/lib/api/queries";
import {
  deleteOtSavedQuery,
  fetchOtQueryFields,
  fetchOtSavedQueries,
  runOtQuery,
  saveOtQuery,
  OT_DATE_FIELDS,
  type OtQueryRow,
} from "@/lib/api/ot-queries";
import { estadoMeta } from "./report-columns";
import { defaultQueryColumns, QUERY_COLUMNS, QUERY_PRESETS } from "./query-columns";

export interface OtQueriesTabProps {
  transitOfficeId: string;
}

export function OtQueriesTab({ transitOfficeId }: OtQueriesTabProps) {
  // El origen se memoriza por organismo: la consola lo usa como dependencia de sus efectos, y una
  // identidad nueva en cada render relanzaría el catálogo y las guardadas sin parar.
  const source = useMemo<QuerySource<OtQueryRow>>(
    () => ({
      testIdPrefix: "ot-query",
      dateFields: OT_DATE_FIELDS,
      defaultDateField: "radicacion",
      columnsStorageKey: "flit-ot-consultas-columnas",
      exportPrefix: "consulta",
      rowNoun: ["trámite", "trámites"],
      fetchFields: (signal) => fetchOtQueryFields(transitOfficeId, signal),
      run: (definition, options) => runOtQuery(definition, { ...options, transitOfficeId }),
      fetchSaved: (signal) => fetchOtSavedQueries(transitOfficeId, signal),
      save: (input) => saveOtQuery(input, transitOfficeId),
      remove: (id) => deleteOtSavedQuery(id, transitOfficeId),
    }),
    [transitOfficeId],
  );

  return (
    <QueryConsole
      source={source}
      columns={QUERY_COLUMNS}
      presets={QUERY_PRESETS}
      defaultColumns={defaultQueryColumns()}
      rowKey={(fila) => fila.procedureInstanceId}
      sheetName="Consulta OT"
      ambito="este organismo"
      rootTestId="ot-queries-tab"
      renderCell={(columnId, fila) =>
        columnId === "estado" ? (
          <span
            className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
            style={{
              background: `${estadoMeta(fila.estadoOt).color}1A`,
              color: estadoMeta(fila.estadoOt).color,
            }}
          >
            {estadoMeta(fila.estadoOt).label}
          </span>
        ) : null
      }
    />
  );
}
