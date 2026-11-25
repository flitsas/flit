"use client";

import { useCallback, useEffect, useState } from "react";
import { iconoComo } from "@flit/ui/iconoComo";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { CarLoader } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { useToast } from "@/components/admin/Toast";
import {
  getRuntContingency,
  RUNT_CONTINGENCY_KIND_LABEL,
  RUNT_CONTINGENCY_PROVIDER_LABEL,
  runtContingencyErrorMessage,
  type RuntContingencyItem,
} from "@/lib/api/admin-runt-contingency";
import { formatFechaHora } from "@/lib/format/date";
import { ContingenciaRuntEditModal } from "./ContingenciaRuntEditModal";

type Estado = "loading" | "error" | "ready";

/**
 * Plataforma → Contingencia de proveedor RUNT (HU #14649): las tres filas fijas (VIN, placa,
 * conductor) con su estado, quién, cuándo y el motivo, y «Acciones → Editar» por fila. No hay estado
 * vacío: el backend siempre devuelve los tres tipos.
 */
export function ContingenciaRuntPanel() {
  const toast = useToast();
  const [estado, setEstado] = useState<Estado>("loading");
  const [items, setItems] = useState<RuntContingencyItem[]>([]);
  const [mensajeError, setMensajeError] = useState("");
  const [editando, setEditando] = useState<RuntContingencyItem | null>(null);

  const cargar = useCallback(async (signal?: AbortSignal) => {
    setEstado("loading");
    try {
      const data = await getRuntContingency(signal);
      if (signal?.aborted) return;
      setItems(data.items);
      setEstado("ready");
    } catch (err) {
      if (signal?.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
      setMensajeError(runtContingencyErrorMessage(err, "No se pudo cargar la contingencia."));
      setEstado("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API (mismo patrón que Confirmación RUNT)
    void cargar(controller.signal);
    return () => controller.abort();
  }, [cargar]);

  if (estado === "loading") {
    return (
      <div className="py-16" data-testid="contingencia-runt-loading">
        <CarLoader mode="runt" label="Cargando contingencia…" />
      </div>
    );
  }

  if (estado === "error") {
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la contingencia"
        action={
          <button type="button" onClick={() => void cargar()} className="text-xs font-semibold underline">
            Reintentar
          </button>
        }
      >
        {mensajeError}
      </InlineAlert>
    );
  }

  const columns: DataTableColumn<RuntContingencyItem>[] = [
    {
      key: "tipo",
      header: "Consulta",
      render: (i) => <span className="font-semibold">{RUNT_CONTINGENCY_KIND_LABEL[i.kind]}</span>,
    },
    {
      key: "estado",
      header: "Estado",
      render: (i) =>
        i.provider ? (
          <StatusBadge label={RUNT_CONTINGENCY_PROVIDER_LABEL[i.provider]} tone="warning" />
        ) : (
          <StatusBadge label="Sin contingencia" tone="neutral" />
        ),
    },
    { key: "quien", header: "Quién", render: (i) => i.updatedByName ?? "—" },
    { key: "cuando", header: "Cuándo", render: (i) => formatFechaHora(i.updatedAt) },
    { key: "motivo", header: "Motivo", render: (i) => i.reason ?? "—" },
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (i) => (
        <ActionsMenu
          ariaLabel={`Acciones para ${RUNT_CONTINGENCY_KIND_LABEL[i.kind]}`}
          items={[
            {
              key: "editar",
              label: "Editar",
              ariaLabel: `Editar contingencia de ${RUNT_CONTINGENCY_KIND_LABEL[i.kind]}`,
              icon: iconoComo("lapiz"),
              onSelect: () => setEditando(i),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <>
      <DataTable
        tarjeta={false}
        columns={columns}
        rows={items}
        getRowKey={(i) => i.kind}
        ariaLabel="Contingencia de proveedor RUNT"
        minWidth={720}
      />
      <ContingenciaRuntEditModal
        item={editando}
        onClose={() => setEditando(null)}
        onSaved={(saved) => {
          setItems((prev) => prev.map((i) => (i.kind === saved.kind ? saved : i)));
          setEditando(null);
          toast.show("Contingencia guardada", "success");
        }}
      />
    </>
  );
}
