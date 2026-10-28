"use client";

// HU #14076 AC1/AC4/AC5 — Enlaces del portal (política de datos, términos, contacto de ventas y video). Un enlace
// legal sin documento oficial se muestra como «Sin documento oficial»: el portal conserva su aviso actual y no se
// inventa texto legal. Cada guardado queda en el borrador. Tabla estándar con RowActions y los cuatro estados.
import { useCallback, useEffect, useState } from "react";
import { Link2, Loader2, Pencil } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { Modal } from "@/components/atom/Modal";
import { RowActions } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { formatFechaHora } from "@/lib/format/date";
import {
  ENLACES_LEGALES,
  ENLACE_LABEL,
  ENLACE_MAX,
  establecerEnlacePn,
  fetchEnlacesPn,
  type EnlacePn,
} from "@/lib/api/admin-pn-contenido";
import {
  PN_CANCEL_BTN_CLS,
  PN_FIELD_ERROR_CLS,
  PN_HINT_CLS,
  PN_INPUT_CLS,
  PN_INPUT_ERROR_CLS,
  PN_LABEL_CLS,
  PN_PRIMARY_BTN_CLS,
  PN_PRIMARY_BTN_STYLE,
} from "../pn-ui";
import { pnErrorMessage } from "../pnErrors";
import { validarEnlace } from "./contenidoReglas";

/** Texto de la columna «Valor»: la URL, «Sin documento oficial» (legales) o «Sin valor» (los demás). */
export function valorEnlace(e: EnlacePn): { texto: string; vacio: boolean } {
  if (e.url) return { texto: e.url, vacio: false };
  return { texto: ENLACES_LEGALES.includes(e.codigo) ? "Sin documento oficial" : "Sin valor", vacio: true };
}

function EnlaceFormModal({ enlace, onClose, onSave }: { enlace: EnlacePn; onClose: () => void; onSave: (url: string | null) => Promise<void> }) {
  const legal = ENLACES_LEGALES.includes(enlace.codigo);
  const [url, setUrl] = useState(enlace.url ?? "");
  const [error, setError] = useState<string | null>(null);
  const [general, setGeneral] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const invalido = validarEnlace(url, enlace.codigo);
    setError(invalido);
    setGeneral(null);
    if (invalido) return;
    setBusy(true);
    try {
      await onSave(url.trim() === "" ? null : url.trim());
    } catch (err) {
      setGeneral(pnErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={`Editar: ${ENLACE_LABEL[enlace.codigo]}`}
      description="El cambio queda en el borrador hasta que lo publique."
      icon={Link2}
      size="md"
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} disabled={busy} className={PN_CANCEL_BTN_CLS}>
            Cancelar
          </button>
          <button type="submit" form="pn-enlace-form" disabled={busy} className={PN_PRIMARY_BTN_CLS} style={PN_PRIMARY_BTN_STYLE}>
            {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
            Guardar enlace
          </button>
        </div>
      }
    >
      <form id="pn-enlace-form" onSubmit={handleSubmit} noValidate className="flex flex-col gap-4">
        <label className={PN_LABEL_CLS}>
          Dirección (URL)
          <input
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            maxLength={ENLACE_MAX}
            placeholder="https://"
            aria-invalid={Boolean(error)}
            aria-describedby="pn-enlace-hint"
            className={`${PN_INPUT_CLS} ${error ? PN_INPUT_ERROR_CLS : ""}`}
          />
          <span id="pn-enlace-hint" className={PN_HINT_CLS}>
            {legal
              ? "Déjelo vacío si no hay documento oficial: el portal conserva su aviso actual."
              : enlace.codigo === "CONTACTO_VENTAS"
                ? "Una página web, un correo (mailto:) o un teléfono (tel:). Vacío = sin valor."
                : "Dirección del video. Vacío = sin valor."}
          </span>
          {error && <span role="alert" className={PN_FIELD_ERROR_CLS}>{error}</span>}
        </label>
        {general && (
          <p role="alert" className="text-sm font-semibold text-[#C2410C]">
            {general}
          </p>
        )}
      </form>
    </Modal>
  );
}

export function EnlacesSection({ onCambio }: { onCambio: () => void }) {
  const { show } = useToast();
  const [enlaces, setEnlaces] = useState<EnlacePn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [editando, setEditando] = useState<EnlacePn | null>(null);
  const pg = usePaginacion("pn.contenido.enlaces");

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const list = await fetchEnlacesPn(signal);
      if (signal?.aborted) return;
      setEnlaces(list);
      setStatus(list.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const columns: DataTableColumn<EnlacePn>[] = [
    { key: "enlace", header: "Enlace", cellClassName: "font-semibold", render: (e) => ENLACE_LABEL[e.codigo] },
    {
      key: "valor",
      header: "Valor",
      cellClassName: "max-w-[420px] break-all text-xs",
      render: (e) => {
        const v = valorEnlace(e);
        return v.vacio ? <StatusBadge label={v.texto} tone="neutral" /> : v.texto;
      },
    },
    {
      key: "actualizado",
      header: "Actualizado",
      cellClassName: "whitespace-nowrap text-xs",
      render: (e) => (e.actualizadoEn ? formatFechaHora(e.actualizadoEn) : "—"),
    },
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (e) => (
        <RowActions actions={[{ icon: Pencil, label: `Editar ${ENLACE_LABEL[e.codigo]}`, tone: "primary", onClick: () => setEditando(e) }]} />
      ),
    },
  ];

  return (
    <section aria-labelledby="pn-enlaces-titulo" className="flex flex-col gap-3">
      <div>
        <h2 id="pn-enlaces-titulo" className="text-base font-bold text-flit-primary dark:text-white">
          Enlaces
        </h2>
        <p className="text-xs text-[#59677D] dark:text-white/70">
          Política de datos, términos, contacto de ventas y video. Sin documento oficial, el portal conserva su aviso actual.
        </p>
      </div>
      {status === "loading" ? (
        <CarLoaderModal label="Cargando enlaces…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="No hay enlaces para configurar."
          errorMessage="No se pudieron cargar los enlaces."
          onRetry={() => void load()}
        >
          <DataTable<EnlacePn>
            ariaLabel="Enlaces del portal"
            columns={columns}
            rows={pg.paginar(enlaces)}
            getRowKey={(e) => e.codigo}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: enlaces.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}

      {editando && (
        <EnlaceFormModal
          enlace={editando}
          onClose={() => setEditando(null)}
          onSave={async (url) => {
            const guardado = await establecerEnlacePn(editando.codigo, url);
            setEnlaces((prev) => prev.map((e) => (e.codigo === guardado.codigo ? guardado : e)));
            setEditando(null);
            show("Enlace guardado en el borrador.", "success");
            onCambio();
          }}
        />
      )}
    </section>
  );
}
