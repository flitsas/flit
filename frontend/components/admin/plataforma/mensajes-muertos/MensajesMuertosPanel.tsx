"use client";

import { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { AlertTriangle, RefreshCw, RotateCcw, Trash2 } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { Modal } from "@/components/atom/Modal";
import { useToast } from "@/components/admin/Toast";
import { superadminClient, type ColaMensajesMuertos, type MensajeMuerto } from "@/lib/api/superadmin-client";
import { formatFechaHora } from "@/lib/format/date";

/** Mismos tamaños que /tramites; el elegido se recuerda durante la sesión (sessionStorage, con guarda). */
const TAMANOS_DE_PAGINA = [10, 25, 50, 100] as const;
const PAGE_SIZE_POR_DEFECTO = 10;
const CLAVE_PAGE_SIZE = "mensajes-muertos.pageSize";

const COLAS: Array<{ cola: ColaMensajesMuertos; etiqueta: string }> = [
  { cola: "correos", etiqueta: "Correos" },
  { cola: "webhooks", etiqueta: "Webhooks" },
];

function suscripcionInerte(): () => void {
  return () => {};
}

function leerPageSizeGuardado(): number {
  try {
    const guardado = Number(sessionStorage.getItem(CLAVE_PAGE_SIZE));
    return TAMANOS_DE_PAGINA.includes(guardado as (typeof TAMANOS_DE_PAGINA)[number]) ? guardado : PAGE_SIZE_POR_DEFECTO;
  } catch {
    return PAGE_SIZE_POR_DEFECTO;
  }
}

type Estado = "loading" | "error" | "ready";
type Accion = { tipo: "descartar"; mensaje: MensajeMuerto };

/**
 * Mensajes muertos de Notificaciones (Epic #13316, HU #13358): por cola, los correos y webhooks que agotaron sus
 * reintentos, con su cola, tipo, empresa, fecha y último error (AC1). Reintentar los devuelve a su cola; descartar pide
 * confirmación en la página y queda en la auditoría (AC2). Tabla sin tarjeta y pie de /tramites (regla de UI/UX).
 */
export function MensajesMuertosPanel() {
  const toast = useToast();
  const [cola, setCola] = useState<ColaMensajesMuertos>("correos");
  const [mensajes, setMensajes] = useState<MensajeMuerto[]>([]);
  const [estado, setEstado] = useState<Estado>("loading");
  const [recarga, setRecarga] = useState(0);
  const [page, setPage] = useState(1);
  const [empresas, setEmpresas] = useState<Map<string, string>>(new Map());
  const [ocupado, setOcupado] = useState<string | null>(null);
  const [confirmar, setConfirmar] = useState<Accion | null>(null);

  const pageSizeGuardado = useSyncExternalStore(suscripcionInerte, leerPageSizeGuardado, () => PAGE_SIZE_POR_DEFECTO);
  const [pageSizeElegido, setPageSizeElegido] = useState<number | null>(null);
  const pageSize = pageSizeElegido ?? pageSizeGuardado;

  useEffect(() => {
    let vigente = true;
    superadminClient
      .listCompanies()
      .then(({ data }) => {
        if (vigente) setEmpresas(new Map(data.map((c) => [c.id, c.razonSocial])));
      })
      .catch(() => {
        // Sin el catálogo se muestra el id de la empresa.
      });
    return () => {
      vigente = false;
    };
  }, []);

  useEffect(() => {
    let vigente = true;
    superadminClient
      .listMensajesMuertos(cola)
      .then((r) => {
        if (!vigente) return;
        setMensajes(r.mensajes);
        setEstado("ready");
      })
      .catch(() => {
        if (vigente) setEstado("error");
      });
    return () => {
      vigente = false;
    };
  }, [cola, recarga]);

  const recargar = useCallback(() => {
    setEstado("loading");
    setRecarga((n) => n + 1);
  }, []);

  const cambiarCola = useCallback((siguiente: ColaMensajesMuertos) => {
    setCola(siguiente);
    setPage(1);
    setEstado("loading");
  }, []);

  const cambiarPageSize = useCallback((n: number) => {
    setPageSizeElegido(n);
    setPage(1);
    try {
      sessionStorage.setItem(CLAVE_PAGE_SIZE, String(n));
    } catch {
      // ventana privada: se queda en memoria y ya
    }
  }, []);

  const reintentar = useCallback(
    async (m: MensajeMuerto) => {
      setOcupado(m.id);
      try {
        await superadminClient.retryMensajeMuerto(cola, m.id);
        toast.show("El mensaje volvió a su cola y se procesará de nuevo.", "success");
        recargar();
      } catch (err) {
        toast.show(err instanceof Error ? `No se pudo reintentar: ${err.message}` : "No se pudo reintentar.", "error");
      } finally {
        setOcupado(null);
      }
    },
    [cola, recargar, toast],
  );

  const descartar = useCallback(
    async (m: MensajeMuerto) => {
      setOcupado(m.id);
      try {
        await superadminClient.discardMensajeMuerto(cola, m.id);
        toast.show("Mensaje descartado. Quedó en la auditoría.", "success");
        setConfirmar(null);
        recargar();
      } catch (err) {
        toast.show(err instanceof Error ? `No se pudo descartar: ${err.message}` : "No se pudo descartar.", "error");
      } finally {
        setOcupado(null);
      }
    },
    [cola, recargar, toast],
  );

  const columnas: DataTableColumn<MensajeMuerto>[] = useMemo(
    () => [
      { key: "muertoEn", header: "Fecha", render: (m) => formatFechaHora(m.muertoEn ?? m.ocurridoEn) },
      { key: "tipo", header: "Tipo", render: (m) => <span className="font-mono text-[11px]">{m.tipo}</span> },
      {
        key: "empresa",
        header: "Empresa",
        render: (m) => (m.empresaId ? empresas.get(m.empresaId) ?? m.empresaId : "—"),
      },
      { key: "origen", header: "Origen", render: (m) => m.origen ?? "—" },
      {
        key: "causa",
        header: "Causa",
        render: (m) => {
          const c = causa(m.motivo);
          return (
            <span className="whitespace-nowrap text-xs font-semibold" style={{ color: c.color }} title={c.ayuda}>
              {c.texto}
            </span>
          );
        },
      },
      {
        key: "error",
        header: "Detalle",
        render: (m) => (
          <span className="line-clamp-2 max-w-[360px] text-xs" title={m.ultimoError ?? m.motivo ?? ""}>
            {m.ultimoError ?? m.motivo ?? "—"}
          </span>
        ),
      },
      { key: "intentos", header: "Intentos", align: "center", render: (m) => m.intentos },
      {
        key: "acciones",
        header: "Acciones",
        align: "right",
        render: (m) => (
          <div className="flex justify-end gap-2" onClick={(e) => e.stopPropagation()}>
            <button
              type="button"
              disabled={ocupado !== null}
              onClick={() => void reintentar(m)}
              className="inline-flex items-center gap-1 rounded-lg border border-[#557EFF] px-2.5 py-1 text-xs font-semibold text-[#557EFF] disabled:opacity-50"
              aria-label={`Reintentar el mensaje ${m.id}`}
            >
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
              Reintentar
            </button>
            <button
              type="button"
              disabled={ocupado !== null}
              onClick={() => setConfirmar({ tipo: "descartar", mensaje: m })}
              className="inline-flex items-center gap-1 rounded-lg border border-[#E5484D] px-2.5 py-1 text-xs font-semibold text-[#E5484D] disabled:opacity-50"
              aria-label={`Descartar el mensaje ${m.id}`}
            >
              <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              Descartar
            </button>
          </div>
        ),
      },
    ],
    [empresas, ocupado, reintentar],
  );

  const pagina = mensajes.slice((page - 1) * pageSize, page * pageSize);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div role="tablist" aria-label="Cola de mensajes muertos" className="inline-flex gap-1 rounded-xl bg-[#EEF2F8] p-1">
          {COLAS.map(({ cola: c, etiqueta }) => (
            <button
              key={c}
              type="button"
              role="tab"
              aria-selected={cola === c}
              onClick={() => cambiarCola(c)}
              className={`rounded-lg px-3 py-1.5 text-xs font-semibold ${cola === c ? "bg-white text-[#162744] shadow-sm" : "text-[#557089]"}`}
            >
              {etiqueta}
            </button>
          ))}
        </div>
        <button
          type="button"
          onClick={recargar}
          className="inline-flex items-center gap-1.5 text-xs font-semibold text-[#557EFF]"
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          Actualizar
        </button>
      </div>

      <DataTable<MensajeMuerto>
        columns={columnas}
        rows={pagina}
        getRowKey={(m) => m.id}
        status={estado === "loading" ? "loading" : estado === "error" ? "error" : undefined}
        onRetry={recargar}
        errorMessage="No se pudieron cargar los mensajes muertos. ¿Está Notificaciones en este ambiente?"
        emptyMessage={cola === "correos" ? "No hay correos sin enviar." : "No hay webhooks sin entregar."}
        ariaLabel="Mensajes muertos de Notificaciones"
        minWidth={960}
        pagination={{
          page,
          pageSize,
          totalCount: mensajes.length,
          onPageChange: setPage,
          pageSizeOptions: TAMANOS_DE_PAGINA,
          onPageSizeChange: cambiarPageSize,
        }}
      />

      {confirmar ? (
        <Modal
          open
          onClose={() => setConfirmar(null)}
          busy={ocupado !== null}
          icon={AlertTriangle}
          iconBg="#F9AC00"
          title="Descartar mensaje"
          titleClassName="text-base font-bold text-[#162744]"
          size="md"
        >
          <div className="space-y-3 text-xs" data-testid="mensajes-muertos-descartar">
            <p>
              El {cola === "correos" ? "correo" : "webhook"} <strong className="font-mono">{confirmar.mensaje.id}</strong> no se
              enviará. Esta acción queda en la auditoría y no se puede deshacer.
            </p>
            <div className="flex justify-end gap-2 pt-1">
              <button
                type="button"
                onClick={() => setConfirmar(null)}
                disabled={ocupado !== null}
                className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
              >
                Cancelar
              </button>
              <button
                type="button"
                onClick={() => void descartar(confirmar.mensaje)}
                disabled={ocupado !== null}
                className="rounded-xl bg-[#E5484D] px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
              >
                {ocupado ? "Descartando…" : "Descartar"}
              </button>
            </div>
          </div>
        </Modal>
      ) : null}
    </div>
  );
}

/**
 * HU #13359 — por qué está aquí, a partir del motivo que deja el consumidor (el tipo de la última falla). Un rechazo del
 * proveedor no se arregla reintentando solo: hay que corregir la causa (buzón lleno, destinatario) antes de reintentar.
 */
function causa(motivo: string | null): { texto: string; color: string; ayuda: string } {
  switch (motivo) {
    case "CorreoRechazadoException":
      return {
        texto: "Rechazado por el proveedor",
        color: "#C62828",
        ayuda: "No se reintenta solo. Corrige la causa del detalle y reintenta, o descarta.",
      };
    case "poison":
      return {
        texto: "Mensaje inválido",
        color: "#B45309",
        ayuda: "El mensaje no se pudo leer: reintentarlo no lo arregla. Descártalo.",
      };
    default:
      return {
        texto: "Agotó los reintentos",
        color: "#475569",
        ayuda: "Falló en todos sus reintentos automáticos. Si la causa ya pasó, reintenta.",
      };
  }
}
