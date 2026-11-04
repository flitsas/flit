"use client";

// HU #14074 AC4 — Historial de una tarifa: las versiones anteriores del mismo ámbito (concepto, tipo y organismo) con
// su vigencia, valor y usuario. Solo lectura: una versión no se edita, el cambio es una versión nueva. El historial es
// por concepto, así que si la fila tiene varios se elige cuál ver.
import { useEffect, useState } from "react";
import { History } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { formatFechaCalendario, formatFechaHora } from "@/lib/format/date";
import {
  CONCEPTOS_TARIFA,
  CONCEPTO_LABEL,
  fetchHistorialTarifaPn,
  formatPesos,
  type ConceptoTarifa,
  type EstadoTarifa,
  type GrupoTarifa,
  type TarifaPn,
} from "@/lib/api/admin-pn-tarifas";
import { PN_LABEL_CLS, PN_INPUT_CLS } from "./pn-ui";

export const ESTADO_TARIFA_LABEL: Record<EstadoTarifa, string> = {
  vigente: "Vigente",
  futura: "Próxima",
  cerrada: "Anterior",
};
export const ESTADO_TARIFA_TONE: Record<EstadoTarifa, "success" | "info" | "neutral"> = {
  vigente: "success",
  futura: "info",
  cerrada: "neutral",
};

const columnas: DataTableColumn<TarifaPn>[] = [
  { key: "desde", header: "Vigente desde", render: (v) => formatFechaCalendario(v.vigenteDesde) },
  {
    key: "hasta",
    header: "Hasta",
    render: (v) => (v.vigenteHasta ? formatFechaCalendario(v.vigenteHasta) : "Sin fecha de cierre"),
  },
  { key: "valor", header: "Valor", cellClassName: "font-semibold", render: (v) => formatPesos(v.valorCop) },
  {
    key: "estado",
    header: "Estado",
    render: (v) => <StatusBadge label={ESTADO_TARIFA_LABEL[v.estado]} tone={ESTADO_TARIFA_TONE[v.estado]} />,
  },
  { key: "registrada", header: "Registrada el", cellClassName: "whitespace-nowrap", render: (v) => formatFechaHora(v.creadaEn) },
];

export function TarifaHistorialModal({ grupo, onClose }: { grupo: GrupoTarifa; onClose: () => void }) {
  const conceptos = CONCEPTOS_TARIFA.filter((c) => grupo.porConcepto[c]);
  const [concepto, setConcepto] = useState<ConceptoTarifa>(conceptos[0]);
  const [versiones, setVersiones] = useState<TarifaPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [intento, setIntento] = useState(0);
  const pg = usePaginacion("pn.tarifas.historial");
  const idDelConcepto = grupo.porConcepto[concepto]?.id;

  useEffect(() => {
    if (!idDelConcepto) return;
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: el estado de carga se pinta de inmediato
    setStatus("loading");
    fetchHistorialTarifaPn(idDelConcepto, controller.signal)
      .then((list) => {
        if (controller.signal.aborted) return;
        setVersiones(list);
        setStatus(list.length === 0 ? "empty" : "ready");
      })
      .catch(() => {
        if (!controller.signal.aborted) setStatus("error");
      });
    return () => controller.abort();
  }, [idDelConcepto, intento]);

  return (
    <Modal
      open
      onClose={onClose}
      title="Historial de la tarifa"
      description={`${grupo.tipoTramiteNombre ?? "Todos los trámites"} · ${grupo.organismoNombre ?? "Todos los organismos"}`}
      icon={History}
      size="xl"
    >
      <div className="flex flex-col gap-4">
        {conceptos.length > 1 && (
          <label className={`${PN_LABEL_CLS} sm:max-w-xs`}>
            Cargo
            <select
              value={concepto}
              onChange={(e) => setConcepto(e.target.value as ConceptoTarifa)}
              className={PN_INPUT_CLS}
            >
              {conceptos.map((c) => (
                <option key={c} value={c}>
                  {CONCEPTO_LABEL[c]}
                </option>
              ))}
            </select>
          </label>
        )}
        {conceptos.length === 1 && (
          <p className="text-sm font-semibold text-flit-primary dark:text-white">{CONCEPTO_LABEL[concepto]}</p>
        )}

        {status === "loading" ? (
          <CarLoaderModal label="Cargando historial…" />
        ) : (
          <UiStateBoundary
            status={status}
            emptyMessage="Esta tarifa todavía no tiene cambios registrados."
            errorMessage="No se pudo cargar el historial."
            onRetry={() => setIntento((n) => n + 1)}
          >
            <DataTable<TarifaPn>
              ariaLabel="Versiones de la tarifa"
              columns={columnas}
              rows={pg.paginar(versiones)}
              getRowKey={(v) => v.id}
              pagination={{
                page: pg.page,
                pageSize: pg.pageSize,
                totalCount: versiones.length,
                onPageChange: pg.setPage,
                onPageSizeChange: pg.setPageSize,
              }}
            />
          </UiStateBoundary>
        )}
      </div>
    </Modal>
  );
}
