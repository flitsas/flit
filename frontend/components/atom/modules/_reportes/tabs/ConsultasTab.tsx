"use client";

// «Consultas» de la empresa gestora: la consola compartida, amarrada al catálogo y a la API de la
// empresa.
//
// Es la misma pantalla que usa el organismo de tránsito —fichas de filtro, enlace vivo, aviso de
// cobertura, export que recorre todas las páginas— sobre otro catálogo. La pregunta que resuelve es
// la de una gestora: «pegué las cuarenta placas de esta flota, ¿por qué salieron treinta y siete?».

import { useMemo } from "react";
import { QueryConsole } from "@/components/consultas/QueryConsole";
import type { QuerySource } from "@/lib/api/queries";
import {
  COMPANY_DATE_FIELDS,
  deleteCompanySavedQuery,
  fetchCompanyQueryFields,
  fetchCompanySavedQueries,
  runCompanyQuery,
  saveCompanyQuery,
  type CompanyQueryRow,
} from "@/lib/api/company-queries";
import {
  COMPANY_QUERY_COLUMNS,
  COMPANY_QUERY_PRESETS,
  defaultCompanyQueryColumns,
  estadoEmpresa,
} from "../consultas/company-columns";

export interface ConsultasTabProps {
  /** Solo SuperAdmin: la compañía que se está mirando. El resto va con la suya. */
  tenantId?: string;
  /** SuperAdmin sin compañía elegida: el backend exige una y no hay nada que consultar. */
  needsCompany?: boolean;
}

export function ConsultasTab({ tenantId, needsCompany = false }: ConsultasTabProps) {
  // El origen se memoriza por compañía: la consola lo usa como dependencia de sus efectos, y una
  // identidad nueva en cada render relanzaría el catálogo y las guardadas sin parar.
  const source = useMemo<QuerySource<CompanyQueryRow>>(
    () => ({
      testIdPrefix: "empresa-query",
      dateFields: COMPANY_DATE_FIELDS,
      defaultDateField: "creacion",
      columnsStorageKey: "flit-empresa-consultas-columnas",
      exportPrefix: "consulta",
      rowNoun: ["trámite", "trámites"],
      fetchFields: (signal) => fetchCompanyQueryFields(tenantId, signal),
      run: (definition, options) => runCompanyQuery(definition, { ...options, tenantId }),
      fetchSaved: (signal) => fetchCompanySavedQueries(tenantId, signal),
      save: (input) => saveCompanyQuery(input, tenantId),
      remove: (id) => deleteCompanySavedQuery(id, tenantId),
    }),
    [tenantId],
  );

  // Sin compañía elegida no se consulta: el backend responde 400 y un error rojo se lee como una
  // avería. Se dice qué falta, que es lo único accionable.
  if (needsCompany) {
    return (
      <div
        className="rounded-2xl border border-[#DFE5ED] p-8 text-center dark:border-white/10"
        data-testid="empresa-query-sin-compania"
      >
        <p className="text-sm font-medium">Selecciona una compañía para consultar sus trámites.</p>
        <p className="mt-1 text-xs opacity-70">
          Las consultas guardadas y el catálogo de filtros son propios de cada compañía.
        </p>
      </div>
    );
  }

  return (
    <QueryConsole
      source={source}
      columns={COMPANY_QUERY_COLUMNS}
      presets={COMPANY_QUERY_PRESETS}
      defaultColumns={defaultCompanyQueryColumns()}
      rowKey={(fila) => fila.procedureInstanceId}
      sheetName="Consulta"
      ambito="su empresa"
      renderCell={(columnId, fila) =>
        columnId === "estado" ? (
          <span
            className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
            style={{
              background: `${estadoEmpresa(fila.status).color}1A`,
              color: estadoEmpresa(fila.status).color,
            }}
          >
            {estadoEmpresa(fila.status).label}
          </span>
        ) : null
      }
    />
  );
}
