"use client";

import {
  ActoresValidacionCell,
  type CampoActor,
} from "@/components/atom/modules/_reportes/consultas/ActoresValidacionCell";
import { actoresDelGrupo } from "@/components/atom/modules/_reportes/consultas/identidad-validacion";
import type { ActorValidacionDto } from "@/lib/api/company-queries";
import { Pagination } from "@/components/atom/Pagination";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import type { DetailedReportPage, NetworkDetailedReportPage } from "@/lib/api/detailed-report";
import { canalLabel } from "@/lib/tramites/tramites-row-labels";
import { statusLabel } from "../_reportes/categories";

const COLUMNAS_ROL = [
  "Comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador",
  "Vendedor", "Validación vendedor", "Intentos vendedor", "Motivo rechazo vendedor",
] as const;

const COLUMNAS = ["Referencia", "Tipo", "Estado", "Canal", ...COLUMNAS_ROL, "Transformación", "Leasing", "Pago", "Traspaso", "Radicador"];

interface DetailedReportGridProps {
  data: DetailedReportPage | NetworkDetailedReportPage | null;
  /** HU #12364 — filas de la red: se añade la columna «Cliente» (dueño del trámite, AC2). */
  networkScope?: boolean;
  /** Épica #14296 — SuperAdmin con «Todas las compañías»: misma columna de compañía que la red. */
  allCompanies?: boolean;
  uiStatus: UiStatus;
  errorMessage?: string;
  emptyMessage?: string;
  onRetry?: () => void;
  page: number;
  /** Tamaño de página elegido; si no llega, se usa el que informa la API. */
  pageSize?: number;
  onPageChange: (page: number) => void;
  /** Bug #13055 — «Filas por página»; sin este manejador el selector no se muestra. */
  onPageSizeChange?: (pageSize: number) => void;
}

export function DetailedReportGrid({
  data,
  uiStatus,
  errorMessage,
  emptyMessage = "No hay trámites que coincidan con los filtros seleccionados.",
  onRetry,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  networkScope = false,
  allCompanies = false,
}: DetailedReportGridProps) {
  // La red muestra el «Cliente»; todas las compañías, la «Compañía». Es la misma columna.
  const columnaCompania = networkScope ? "Cliente" : allCompanies ? "Compañía" : null;
  const columnas = columnaCompania ? [columnaCompania, ...COLUMNAS] : COLUMNAS;

  return (
    <>
      {uiStatus === "loading" && (
        <p className="text-sm font-medium opacity-80" data-testid="detallado-armando">
          Armando el reporte…
        </p>
      )}
    <UiStateBoundary
      status={uiStatus}
      emptyMessage={emptyMessage}
      errorMessage={errorMessage}
      onRetry={onRetry}
      skeletonRows={6}
    >
      {/* Bug #13055 — tabla homologada con el modelo de trámites (sin tarjeta envolvente). */}
      <div className={CARDLIST_SCROLL}>
        <table className={`min-w-full ${CARDLIST_TABLE}`} aria-label="Reporte detallado de trámites">
          <thead>
            <tr className={CARDLIST_HEAD_ROW}>
              {columnas.map((h) => (
                <th key={h} scope="col" className={CARDLIST_TH}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {data?.items.map((row) => (
              <tr key={row.id} className={CARDLIST_ROW}>
                {columnaCompania && (
                  <td className={`${CARDLIST_CELL} font-medium`} data-testid="detallado-fila-cliente">
                    {"tenantName" in row && row.tenantName ? row.tenantName : "—"}
                  </td>
                )}
                <td className={CARDLIST_CELL}>{row.referenceNumber}</td>
                <td className={CARDLIST_CELL}>{row.procedureTypeName}</td>
                <td className={CARDLIST_CELL}>{statusLabel(row.status) ?? row.status}</td>
                <td className={CARDLIST_CELL} data-testid="detallado-fila-canal">
                  {canalLabel(row.channel)}
                  {row.channel === "pn_punto" && row.channelTransitOfficeName ? (
                    <span className="block text-[11px] opacity-70">{row.channelTransitOfficeName}</span>
                  ) : null}
                </td>
                {/* Ocho columnas, cuatro por rol, alineadas por actor (46 px cada uno). El propietario va con los compradores. Sin `actores` (ausente, null o vacío): «—» en las ocho; la «persona» vieja era de cualquier rol y no se puede etiquetar como comprador. */}
                {(() => {
                  const compradores = actoresDelGrupo(row.actores, "comprador");
                  const vendedores = actoresDelGrupo(row.actores, "vendedor");
                  const celda = (label: string, actores: ActorValidacionDto[], campo: CampoActor) => (
                    <td key={label} data-label={label} className={CARDLIST_CELL}>
                      <ActoresValidacionCell actores={actores} campo={campo} />
                    </td>
                  );
                  return [
                    celda("Comprador", compradores, "persona"),
                    celda("Validación comprador", compradores, "validacion"),
                    celda("Intentos comprador", compradores, "intentos"),
                    celda("Motivo rechazo comprador", compradores, "motivo"),
                    celda("Vendedor", vendedores, "persona"),
                    celda("Validación vendedor", vendedores, "validacion"),
                    celda("Intentos vendedor", vendedores, "intentos"),
                    celda("Motivo rechazo vendedor", vendedores, "motivo"),
                  ];
                })()}
                <td data-label="Transformación" className={CARDLIST_CELL}>{row.hasTransformation ? row.transformationDetail ?? "Sí" : "No"}</td>
                <td data-label="Leasing" className={CARDLIST_CELL}>{row.isLeasing ? "Sí" : "No"}</td>
                <td data-label="Pago" className={CARDLIST_CELL}>{row.paymentType || "—"}</td>
                <td data-label="Traspaso" className={CARDLIST_CELL}>{row.transferType}</td>
                <td data-label="Radicador" className={CARDLIST_CELL}>{row.createdByDisplayName}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {data && (
        <Pagination
          page={page}
          pageSize={pageSize ?? data.pageSize}
          totalCount={data.totalCount}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
          ariaLabel="Paginación del reporte detallado"
          noun="trámites"
        />
      )}
    </UiStateBoundary>
    </>
  );
}
