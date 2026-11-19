"use client";

// Tiempo para anular un cobro de caja (por organismo, 0 a `maxMinutos`, 15 por defecto). Vive en una franja propia bajo las tarjetas de
// Cobro → Medios de pago. Lo cambian el Admin OT (su organismo) y el SuperAdmin; con 0 el trámite se envía al cobrar y el
// cobro no se puede anular. Aplica solo a los cobros nuevos. Incluye el historial de cambios en un modal.
import { useCallback, useEffect, useId, useState } from "react";
import { Button } from "@flit/ui/Button";
import { Modal } from "@/components/atom/Modal";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { PnLinkButton } from "@/components/admin/plataforma/persona-natural/pn-componentes";
import { pnErrorMessage } from "@/components/admin/plataforma/persona-natural/pnErrors";
import {
  PN_FIELD_ERROR_CLS,
  PN_HINT_CLS,
  PN_INPUT_CLS,
  PN_INPUT_ERROR_CLS,
} from "@/components/admin/plataforma/persona-natural/pn-ui";
import type { CajaAnulacion, CambioCajaAnulacion, CobroApi } from "@/lib/api/admin-ot-portal-cobro";
import { formatFechaHora } from "@/lib/format/date";

export const CAJA_ANULACION_AYUDA =
  "Después de este tiempo el trámite se envía al organismo. Aplica a cobros nuevos.";
export const CAJA_ANULACION_CERO = "Con 0, el trámite se envía al cobrar y el cobro no se puede anular.";

export interface CajaAnulacionCampoProps {
  api: CobroApi;
  valor: CajaAnulacion;
  onGuardado: (nuevo: CajaAnulacion) => void;
  /** «PIN en caja» apagado: la franja se ve pero no se puede editar. */
  deshabilitado?: boolean;
}

function minutosEn(texto: string, max: number): number | null {
  if (!/^\d+$/.test(texto.trim())) return null;
  const n = Number(texto);
  return n >= 0 && n <= max ? n : null;
}

export function CajaAnulacionCampo({ api, valor, onGuardado, deshabilitado = false }: CajaAnulacionCampoProps) {
  const id = useId();
  const { show } = useToast();
  const [texto, setTexto] = useState(String(valor.minutos));
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [verHistorial, setVerHistorial] = useState(false);

  // Si el valor guardado cambia desde afuera (recarga), el campo lo sigue.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- el campo refleja el último valor guardado
    setTexto(String(valor.minutos));
  }, [valor.minutos]);

  const mensajeRango = `Escribe un número entero de minutos entre 0 y ${valor.maxMinutos}.`;
  const n = minutosEn(texto, valor.maxMinutos);
  const ayudaId = `${id}-ayuda`;
  const errorId = `${id}-error`;

  async function guardar() {
    if (!api.guardarCajaAnulacion) return;
    if (n === null) {
      setError(mensajeRango);
      return;
    }
    setGuardando(true);
    setError(null);
    try {
      const nuevo = await api.guardarCajaAnulacion(n);
      onGuardado(nuevo);
      show("Cambio guardado.", "success");
    } catch (err) {
      setError(pnErrorMessage(err, "No se pudo guardar el tiempo para anular. Inténtalo de nuevo."));
    } finally {
      setGuardando(false);
    }
  }

  const sinCambios = texto.trim() === String(valor.minutos);
  const bloqueado = deshabilitado || guardando;

  return (
    <div
      className="flex flex-col gap-2 rounded-[14px] border border-border bg-card p-4"
      data-testid="caja-anulacion"
    >
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <h3 className="flex items-center gap-2 text-base font-semibold text-flit-primary">
          Anulación de cobros en caja
          {valor.porDefecto && (
            <span className="rounded-full bg-secondary px-2 py-0.5 text-xs font-semibold text-muted-foreground">
              Valor por defecto
            </span>
          )}
        </h3>
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <label htmlFor={`${id}-minutos`} className="text-sm font-semibold text-flit-primary">
            Tiempo para anular:
          </label>
          <input
            id={`${id}-minutos`}
            type="number"
            inputMode="numeric"
            min={0}
            max={valor.maxMinutos}
            step={1}
            value={texto}
            disabled={bloqueado}
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? `${ayudaId} ${errorId}` : ayudaId}
            className={`${PN_INPUT_CLS} !w-24 shrink-0 ${error ? PN_INPUT_ERROR_CLS : ""}`}
            onChange={(e) => {
              setTexto(e.target.value);
              setError(null);
            }}
          />
          <span className="text-sm text-muted-foreground">min</span>
          <Button
            variant="primary"
            cargando={guardando}
            disabled={bloqueado || sinCambios}
            onClick={() => void guardar()}
            aria-label="Guardar tiempo para anular"
          >
            Guardar
          </Button>
          <PnLinkButton onClick={() => setVerHistorial(true)} ariaLabel="Ver historial de cambios del tiempo para anular">
            Ver historial
          </PnLinkButton>
        </div>
      </div>
      <p id={ayudaId} className={PN_HINT_CLS}>
        {CAJA_ANULACION_AYUDA}
        {deshabilitado && " Activa PIN en caja para usarlo."}
      </p>
      {n === 0 && (
        <p role="status" className="text-xs font-semibold text-flit-primary">
          {CAJA_ANULACION_CERO}
        </p>
      )}
      {error && (
        <p id={errorId} role="alert" className={PN_FIELD_ERROR_CLS}>
          {error}
        </p>
      )}
      {verHistorial && <HistorialModal api={api} onClose={() => setVerHistorial(false)} />}
    </div>
  );
}

function HistorialModal({ api, onClose }: { api: CobroApi; onClose: () => void }) {
  const [items, setItems] = useState<CambioCajaAnulacion[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const pg = usePaginacion("pn.cobro.caja-anulacion.historial");

  const cargar = useCallback(
    async (signal?: AbortSignal) => {
      const lista = (await api.historialCajaAnulacion?.(signal)) ?? [];
      if (signal?.aborted) return;
      setItems(lista);
      setStatus(lista.length === 0 ? "empty" : "ready");
    },
    [api],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial del historial al abrir el modal
    cargar(controller.signal).catch(() => {
      if (!controller.signal.aborted) setStatus("error");
    });
    return () => controller.abort();
  }, [cargar]);

  const columnas: DataTableColumn<CambioCajaAnulacion>[] = [
    { key: "cuando", header: "Fecha y hora", render: (c) => formatFechaHora(c.cambiadoEn) },
    { key: "quien", header: "Persona", render: (c) => c.actorNombre ?? "—" },
    { key: "antes", header: "Antes (min)", align: "right", render: (c) => c.minutosAntes },
    { key: "despues", header: "Después (min)", align: "right", render: (c) => c.minutosDespues },
  ];

  return (
    <Modal
      open
      onClose={onClose}
      title="Historial del tiempo para anular"
      icono="reloj"
      size="lg"
      description="Cada vez que se cambió el tiempo para anular un cobro de caja."
      footer={
        <div className="flex justify-end gap-3 px-6 pb-5">
          <Button variant="secondary" onClick={onClose}>
            Cerrar
          </Button>
        </div>
      }
    >
      <div className="px-6 pb-4">
        <UiStateBoundary
          status={status}
          emptyMessage="Aún no se ha cambiado este tiempo."
          errorMessage="No se pudo cargar el historial."
          onRetry={() => {
            setStatus("loading");
            cargar().catch(() => setStatus("error"));
          }}
        >
          <DataTable<CambioCajaAnulacion>
            ariaLabel="Historial del tiempo para anular un cobro"
            columns={columnas}
            rows={pg.paginar(items)}
            getRowKey={(c) => `${c.cambiadoEn}|${c.minutosAntes}|${c.minutosDespues}`}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: items.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      </div>
    </Modal>
  );
}
