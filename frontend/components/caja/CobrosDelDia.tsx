"use client";

// Pestaña «Cobros del día»: lo cobrado en la caja del organismo, con el total sin anulados. Tabla estándar de FLIT 2.0
// (modelo de Trámites): «Acciones» → «Anular» solo para el Admin OT, con motivo obligatorio de 10 a 300 caracteres. Un
// cobro que ya no se puede anular deja «Anular» deshabilitado con su motivo.
import { useEffect, useRef, useState } from "react";
import { Ban } from "lucide-react";
import { Button } from "@flit/ui/Button";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { PN_FIELD_ERROR_CLS, PN_HINT_CLS, PN_INPUT_CLS, PN_LABEL_CLS } from "@/components/admin/plataforma/persona-natural/pn-ui";
import { formatPesos } from "@/lib/api/admin-pn-tarifas";
import {
  ETIQUETA_MEDIO,
  MOTIVO_ANULACION_MAX,
  MOTIVO_ANULACION_MIN,
  type CajaCobro,
  type CajaCobroLista,
  type OtCajaApi,
} from "@/lib/api/ot-caja";
import { formatFechaHora, ZONA_COLOMBIA } from "@/lib/format/date";
import { CAJA_COBRO_NO_ANULABLE, cajaNoHabilitada, mensajeDeCaja } from "@/lib/caja/mensajes";

export const CAJA_COBROS_CARGANDO = "Cargando cobros de caja…";
export const CAJA_COBROS_VACIO = "No hay cobros de caja en este día.";
export const CAJA_MOTIVO_ERROR = `El motivo debe tener entre ${MOTIVO_ANULACION_MIN} y ${MOTIVO_ANULACION_MAX} caracteres.`;

/** Hoy en Colombia como AAAA-MM-DD. */
export function hoyColombia(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: ZONA_COLOMBIA }).format(new Date());
}

export interface CobrosDelDiaProps {
  api: OtCajaApi;
  /** Solo el Admin OT anula (y elige el día). La persona de caja ve el día de hoy y nada más. */
  puedeAnular: boolean;
  /** Cambia cuando se registra un cobro: fuerza la recarga. */
  version?: number;
}

export function CobrosDelDia({ api, puedeAnular, version = 0 }: CobrosDelDiaProps) {
  const [dia, setDia] = useState(hoyColombia);
  const [lista, setLista] = useState<CajaCobroLista | null>(null);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [mensajeError, setMensajeError] = useState("");
  const [noHabilitada, setNoHabilitada] = useState(false);
  const [recarga, setRecarga] = useState(0);
  const [aAnular, setAAnular] = useState<CajaCobro | null>(null);
  const [motivo, setMotivo] = useState("");
  const [errorMotivo, setErrorMotivo] = useState<string | null>(null);
  const [anulando, setAnulando] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const pg = usePaginacion("caja.cobros");
  const { page, pageSize, setPage } = pg;
  const campoMotivo = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- consulta del servidor: el estado de carga es intencional
    setStatus("loading");
    api
      .cobros({ date: dia, page, pageSize }, controller.signal)
      .then((res) => {
        if (controller.signal.aborted) return;
        setLista(res);
        setNoHabilitada(false);
        setStatus("ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setMensajeError(mensajeDeCaja(err).texto);
        setNoHabilitada(cajaNoHabilitada(err));
        setStatus("error");
      });
    return () => controller.abort();
  }, [api, dia, page, pageSize, recarga, version]);

  function abrirAnulacion(c: CajaCobro) {
    setMotivo("");
    setErrorMotivo(null);
    setAviso(null);
    setAAnular(c);
  }

  async function anular() {
    if (!aAnular) return;
    const m = motivo.trim();
    if (m.length < MOTIVO_ANULACION_MIN || m.length > MOTIVO_ANULACION_MAX) {
      setErrorMotivo(CAJA_MOTIVO_ERROR);
      campoMotivo.current?.focus();
      return;
    }
    setAnulando(true);
    try {
      await api.anular(aAnular, m);
      setAAnular(null);
      setAviso("Cobro anulado.");
      setRecarga((n) => n + 1);
    } catch (err) {
      // El cobro cambió o ya no es anulable: se muestra el motivo y se refresca la lista para reflejarlo.
      setErrorMotivo(mensajeDeCaja(err).texto);
      setRecarga((n) => n + 1);
    } finally {
      setAnulando(false);
    }
  }

  const columns: DataTableColumn<CajaCobro>[] = [
    { key: "codigo", header: "Trámite", render: (c) => <Tachable c={c}>{c.codigo}</Tachable> },
    { key: "valor", header: "Valor", align: "right", render: (c) => <Tachable c={c}>{formatPesos(c.valorCop)}</Tachable> },
    {
      key: "medio",
      header: "Medio",
      render: (c) => {
        const base = ETIQUETA_MEDIO[c.medio] ?? c.medio;
        return <Tachable c={c}>{c.medioDetalle ? `${base} · ${c.medioDetalle}` : base}</Tachable>;
      },
    },
    { key: "hora", header: "Fecha y hora", render: (c) => <Tachable c={c}>{formatFechaHora(c.pagadoEn, "")}</Tachable> },
    { key: "usuario", header: "Usuario", render: (c) => <Tachable c={c}>{c.pagadoPorNombre ?? ""}</Tachable> },
    {
      key: "estado",
      header: "Estado",
      render: (c) =>
        c.estado === "anulado" ? (
          <div className="flex flex-col gap-1">
            <StatusBadge tone="danger" label="Anulado" />
            <span className="text-xs">
              {c.anuladoEn ? `El ${formatFechaHora(c.anuladoEn, "")}` : ""}
              {c.anuladoPorNombre ? ` por ${c.anuladoPorNombre}` : ""}
              {c.anulacionMotivo ? `. Motivo: ${c.anulacionMotivo}` : ""}
            </span>
          </div>
        ) : (
          <StatusBadge tone="success" label="Pagado" />
        ),
    },
  ];
  if (puedeAnular) {
    columns.push({
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
    });
  }

  return (
    <section aria-labelledby="caja-cobros-titulo" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 id="caja-cobros-titulo" className="text-xl font-bold text-[color:var(--di-heading)]">
            Cobros del día
          </h2>
          <p className={PN_HINT_CLS}>Lo que se cobró en la caja de tu organismo.</p>
        </div>
        {puedeAnular && (
          <label className="flex flex-col gap-1 text-xs font-semibold text-flit-primary" htmlFor="caja-cobros-dia">
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
        )}
      </div>

      {aviso && <InlineAlert tone="success">{aviso}</InlineAlert>}
      {status === "ready" && lista && (
        <p className="text-sm font-semibold text-flit-primary" data-testid="total-dia">
          Total del día: {formatPesos(lista.totalDiaCop)} <span className={PN_HINT_CLS}>(sin cobros anulados)</span>
        </p>
      )}

      {status === "loading" ? (
        <CarLoaderModal label={CAJA_COBROS_CARGANDO} />
      ) : noHabilitada ? (
        <p role="status" className="px-4 py-10 text-center text-sm font-semibold text-flit-primary">
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
              <p role="status" className="px-4 py-10 text-center text-sm font-semibold text-flit-primary">
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

      <Modal
        open={aAnular !== null}
        onClose={() => setAAnular(null)}
        busy={anulando}
        title="Anular cobro"
        icono="alerta"
        description={
          aAnular ? `Vas a anular el cobro del trámite ${aAnular.codigo} por ${formatPesos(aAnular.valorCop)}. No se puede deshacer.` : undefined
        }
        footer={
          <div className="flex flex-wrap justify-end gap-3 px-6 pb-5">
            <Button variant="secondary" onClick={() => setAAnular(null)} disabled={anulando}>
              Cancelar
            </Button>
            <Button variant="alerta" onClick={() => void anular()} cargando={anulando}>
              Anular cobro
            </Button>
          </div>
        }
      >
        <div className="flex flex-col gap-3 px-6 pb-4">
          <label className={PN_LABEL_CLS} htmlFor="caja-anular-motivo">
            Motivo de la anulación
            <textarea
              id="caja-anular-motivo"
              ref={campoMotivo}
              rows={3}
              maxLength={MOTIVO_ANULACION_MAX}
              className={`${PN_INPUT_CLS} h-auto py-2`}
              value={motivo}
              aria-required="true"
              aria-invalid={errorMotivo ? true : undefined}
              aria-describedby="caja-anular-ayuda"
              onChange={(e) => {
                setMotivo(e.target.value);
                setErrorMotivo(null);
              }}
            />
          </label>
          <p id="caja-anular-ayuda" className={PN_HINT_CLS}>
            Entre {MOTIVO_ANULACION_MIN} y {MOTIVO_ANULACION_MAX} caracteres.
          </p>
          {errorMotivo && (
            <p role="alert" className={PN_FIELD_ERROR_CLS}>
              {errorMotivo}
            </p>
          )}
        </div>
      </Modal>
    </section>
  );
}

/** Un cobro anulado se ve tachado: no suma al total del día. */
function Tachable({ c, children }: { c: CajaCobro; children: React.ReactNode }) {
  return <span className={c.estado === "anulado" ? "line-through opacity-70" : undefined}>{children}</span>;
}
