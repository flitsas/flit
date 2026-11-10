"use client";

// HU #14462 (Feature #14453, Épica #14210) — «Cobros de caja» del organismo en el hub del OT: cobros del día con su total
// y anulación con motivo obligatorio. «Anular» queda deshabilitado, con el motivo, cuando el cobro ya no se puede anular
// (el trámite ya llegó a FLIT 2.0). La caja no anula: solo el administrador del organismo y el SuperAdmin.
import { useEffect, useMemo, useState } from "react";
import { Ban } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { PnConfirmDialog } from "@/components/admin/plataforma/persona-natural/pn-componentes";
import { PN_HINT_CLS, PN_INPUT_CLS, PN_LABEL_CLS } from "@/components/admin/plataforma/persona-natural/pn-ui";
import { usePermissions } from "@/hooks/usePermissions";
import {
  MOTIVO_ANULACION_MAX,
  MOTIVO_ANULACION_MIN,
  crearCajaApiOt,
  type CajaApi,
  type CajaCobro,
  type CajaCobroLista,
} from "@/lib/api/admin-ot-portal-caja";
import { ApiError } from "@/lib/api/types";
import { formatPesos } from "@/lib/api/admin-pn-tarifas";
import { formatFechaHora, ZONA_COLOMBIA } from "@/lib/format/date";
import { cajaErrorMessage, cajaNoHabilitada, CAJA_COBRO_NO_ANULABLE } from "./cajaErrores";

export const CAJA_COBROS_CARGANDO = "Cargando cobros de caja…";
export const CAJA_COBROS_VACIO = "No hay cobros de caja en este día.";
export const CAJA_MOTIVO_ERROR = `El motivo debe tener entre ${MOTIVO_ANULACION_MIN} y ${MOTIVO_ANULACION_MAX} caracteres.`;

const MEDIO_LABEL: Record<string, string> = {
  efectivo: "Efectivo",
  datafono: "Datáfono",
  transferencia: "Transferencia",
  otro: "Otro",
};

/** Hoy en Colombia como AAAA-MM-DD. */
export function hoyColombia(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: ZONA_COLOMBIA }).format(new Date());
}

export interface OtCajaCobrosSectionProps {
  transitOfficeId: string;
  /** Solo para pruebas: cliente ya resuelto. */
  api?: CajaApi;
}

export function OtCajaCobrosSection({ transitOfficeId, api: apiProp }: OtCajaCobrosSectionProps) {
  const { isSuperAdmin } = usePermissions();
  const api = useMemo(
    () => apiProp ?? crearCajaApiOt(isSuperAdmin ? { transitOfficeId } : undefined),
    [apiProp, isSuperAdmin, transitOfficeId],
  );

  const [dia, setDia] = useState(hoyColombia);
  const [lista, setLista] = useState<CajaCobroLista | null>(null);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [mensajeError, setMensajeError] = useState("");
  const [noHabilitada, setNoHabilitada] = useState(false);
  const [recarga, setRecarga] = useState(0);
  const [aAnular, setAAnular] = useState<CajaCobro | null>(null);
  const [motivo, setMotivo] = useState("");
  const [aviso, setAviso] = useState<string | null>(null);
  const pg = usePaginacion("portal.caja.cobros");
  const { page, pageSize, setPage } = pg;

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- consulta del servidor: el estado de carga es intencional
    setStatus("loading");
    api
      .listarCobros({ date: dia, page, pageSize }, controller.signal)
      .then((res) => {
        if (controller.signal.aborted) return;
        setLista(res);
        setNoHabilitada(false);
        setStatus("ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setMensajeError(cajaErrorMessage(err));
        setNoHabilitada(cajaNoHabilitada(err));
        setStatus("error");
      });
    return () => controller.abort();
  }, [api, dia, page, pageSize, recarga]);

  function abrirAnulacion(c: CajaCobro) {
    setMotivo("");
    setAviso(null);
    setAAnular(c);
  }

  async function anular() {
    if (!aAnular) return;
    const m = motivo.trim();
    if (m.length < MOTIVO_ANULACION_MIN || m.length > MOTIVO_ANULACION_MAX) {
      // Lanza para que el diálogo no se cierre y muestre el motivo.
      throw new ApiError(400, CAJA_MOTIVO_ERROR, { message: CAJA_MOTIVO_ERROR });
    }
    try {
      await api.anular(aAnular, m);
    } catch (err) {
      // Si el cobro cambió o ya no es anulable, se refresca la lista para reflejarlo.
      setRecarga((n) => n + 1);
      // El diálogo muestra el `message` del error: se le entrega el texto ya resuelto.
      const mensaje = cajaErrorMessage(err);
      const estado = err instanceof ApiError ? err.status : 400;
      throw new ApiError(estado, mensaje, { code: "CAJA", message: mensaje });
    }
    setAAnular(null);
    setAviso("Cobro anulado.");
    setRecarga((n) => n + 1);
  }

  const columns: DataTableColumn<CajaCobro>[] = [
    { key: "codigo", header: "Trámite", render: (c) => c.codigo },
    { key: "valor", header: "Valor", align: "right", render: (c) => formatPesos(c.valorCop) },
    {
      key: "medio",
      header: "Medio",
      render: (c) => {
        const base = MEDIO_LABEL[c.medio] ?? c.medio;
        return c.medioDetalle ? `${base} · ${c.medioDetalle}` : base;
      },
    },
    { key: "pagado", header: "Pagado", render: (c) => formatFechaHora(c.pagadoEn, "") },
    { key: "cajero", header: "Cajero", render: (c) => c.pagadoPorNombre ?? "" },
    {
      key: "estado",
      header: "Estado",
      render: (c) =>
        c.estado === "anulado" ? (
          <span>
            Anulado
            {c.anuladoEn ? ` el ${formatFechaHora(c.anuladoEn, "")}` : ""}
            {c.anuladoPorNombre ? ` por ${c.anuladoPorNombre}` : ""}
            {c.anulacionMotivo ? `. Motivo: ${c.anulacionMotivo}` : ""}
          </span>
        ) : (
          "Pagado"
        ),
    },
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (c) => (
        <ActionsMenu
          ariaLabel={`Acciones del cobro ${c.codigo}`}
          items={[
            {
              key: "anular",
              label: "Anular",
              icon: Ban,
              tone: "danger",
              ariaLabel: `Anular el cobro ${c.codigo}`,
              disabled: c.estado === "anulado" || !c.anulable,
              disabledReason:
                c.estado === "anulado" ? "El cobro ya está anulado." : c.motivoNoAnulable?.trim() || CAJA_COBRO_NO_ANULABLE,
              onSelect: () => abrirAnulacion(c),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <section aria-labelledby="caja-cobros-titulo" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 id="caja-cobros-titulo" className="text-base font-bold text-flit-primary dark:text-white">
            Cobros de caja
          </h2>
          <p className="text-xs text-[#59677D] dark:text-white/70">
            Lo que se cobró en la caja de tu organismo. Un cobro se anula solo mientras el trámite no haya llegado a FLIT 2.0.
          </p>
        </div>
        <label className="flex flex-col gap-1 text-xs font-semibold text-flit-primary dark:text-white" htmlFor="caja-cobros-dia">
          Día
          <input
            id="caja-cobros-dia"
            type="date"
            className={PN_INPUT_CLS}
            value={dia}
            max={hoyColombia()}
            onChange={(e) => {
              if (!e.target.value) return;
              setDia(e.target.value);
              setPage(1);
            }}
          />
        </label>
      </div>

      {aviso && <InlineAlert tone="success">{aviso}</InlineAlert>}
      {status === "ready" && lista && (
        <p className="text-sm font-semibold text-flit-primary dark:text-white">
          Total del día: {formatPesos(lista.totalDiaCop)}{" "}
          <span className={PN_HINT_CLS}>(sin cobros anulados)</span>
        </p>
      )}

      {status === "loading" ? (
        <CarLoaderModal label={CAJA_COBROS_CARGANDO} />
      ) : noHabilitada ? (
        <p role="status" className="px-4 py-10 text-center text-sm font-semibold text-flit-primary dark:text-white">
          {mensajeError}
        </p>
      ) : (
        <UiStateBoundary status={status} errorMessage={mensajeError} onRetry={() => setRecarga((n) => n + 1)}>
          <DataTable<CajaCobro>
            ariaLabel="Cobros de caja"
            columns={columns}
            rows={lista?.items ?? []}
            getRowKey={(c) => c.cobroId}
            emptyContent={
              <p role="status" className="px-4 py-10 text-center text-sm font-semibold text-flit-primary dark:text-white">
                {CAJA_COBROS_VACIO}
              </p>
            }
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: lista?.total ?? 0,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}

      {aAnular && (
        <PnConfirmDialog
          title="Anular cobro"
          icon={Ban}
          tone="danger"
          confirmLabel="Anular cobro"
          onClose={() => setAAnular(null)}
          onConfirm={anular}
        >
          <p>
            Vas a anular el cobro del trámite {aAnular.codigo} por {formatPesos(aAnular.valorCop)}. No se puede deshacer.
          </p>
          <label className={PN_LABEL_CLS} htmlFor="caja-anular-motivo">
            Motivo de la anulación
            <textarea
              id="caja-anular-motivo"
              rows={3}
              maxLength={MOTIVO_ANULACION_MAX}
              className={PN_INPUT_CLS}
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
              aria-required="true"
            />
          </label>
        </PnConfirmDialog>
      )}
    </section>
  );
}
