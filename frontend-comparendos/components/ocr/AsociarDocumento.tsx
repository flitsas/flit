"use client";

import { Modal } from "@flit/ui";
import { BOTON_PELIGRO_CLS, BOTON_PRIMARIO_CLS, controlCls } from "@/components/ui/estilos";
import { API_PREFIX } from "@/lib/api";
import { formatoFechaHora } from "@/lib/format/date";
import { type EvidenciaActual, numerosDifieren, tamanoLegible, vistaPorContentType } from "@/lib/ocr";

/** Comparendo existente tal como lo describe la revisión (los campos que muestran los modales). */
export interface ComparendoExistente {
  id: string;
  numero: string | null;
  fuente: string | null;
  activo?: boolean;
  pagado?: boolean;
}

/** Aviso cuando el comparendo ya no recibe avisos: inactivo o pagado. */
export function avisoSinAvisos(c: Pick<ComparendoExistente, "activo" | "pagado">): string | null {
  if (c.activo === false && c.pagado) return "Este comparendo está inactivo y pagado: ya no recibe avisos, pero el documento queda guardado.";
  if (c.activo === false) return "Este comparendo está inactivo: ya no recibe avisos, pero el documento queda guardado.";
  if (c.pagado) return "Este comparendo está pagado: ya no recibe avisos, pero el documento queda guardado.";
  return null;
}

const AVISO_NUMEROS = "Los números no coinciden exactamente: confirma que es el mismo comparendo.";

/** Número leído del documento junto al del sistema, y el aviso si difieren en algo más que letra o formato. */
function Numeros({ documento, sistema }: { documento: string | null; sistema: string | null }) {
  return (
    <>
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
        <dt className="font-semibold">Número en el documento</dt>
        <dd className="min-w-0 break-all tabular-nums">{documento ?? "—"}</dd>
        <dt className="font-semibold">Número en el sistema</dt>
        <dd className="min-w-0 break-all tabular-nums">{sistema ?? "—"}</dd>
      </dl>
      {numerosDifieren(documento, sistema) && (
        <p role="alert" className="rounded-xl border border-[var(--badge-warning-fg)] px-3 py-2 text-xs text-[var(--badge-warning-fg)]">
          {AVISO_NUMEROS}
        </p>
      )}
    </>
  );
}

function Vista({ archivoId, nombre, contentType, titulo }: { archivoId: string; nombre: string; contentType: string; titulo: string }) {
  const url = `${API_PREFIX}/archivos/${archivoId}?inline=true`;
  const vista = vistaPorContentType(contentType, nombre);
  return (
    <div className="h-48 overflow-auto rounded-2xl border border-border bg-secondary sm:h-72 lg:h-[50vh]">
      {vista === "imagen" && (
        // eslint-disable-next-line @next/next/no-img-element -- archivo privado servido por el proxy de la API
        <img src={url} alt={titulo} loading="lazy" decoding="async" className="block h-auto w-full object-contain" />
      )}
      {vista === "pdf" && <iframe title={titulo} src={url} loading="lazy" className="h-full w-full" />}
      {vista === "descarga" && <p className="p-4 text-xs text-muted-foreground">El navegador no muestra archivos TIFF; no hay vista previa.</p>}
    </div>
  );
}

function Columna({ rotulo, nombre, detalle, children }: { rotulo: string; nombre: string; detalle: string; children: React.ReactNode }) {
  return (
    <section className="flex min-w-0 flex-col gap-2" aria-label={rotulo}>
      <div className="min-w-0">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{rotulo}</h3>
        <p className="truncate text-sm font-semibold" title={nombre}>
          {nombre}
        </p>
        <p className="text-xs text-muted-foreground">{detalle}</p>
      </div>
      {children}
    </section>
  );
}

/** «Asociar al comparendo»: el comparendo existente no tiene documento; se le agrega el archivo escaneado. */
export function ModalAsociar({
  comparendo,
  documento,
  ocupado,
  onConfirmar,
  onClose,
}: {
  comparendo: ComparendoExistente;
  documento: { nombreArchivo: string; numeroComparendoExtraido?: string | null };
  ocupado: boolean;
  onConfirmar: () => void;
  onClose: () => void;
}) {
  const aviso = avisoSinAvisos(comparendo);
  return (
    <Modal
      icono="documento"
      open
      onClose={onClose}
      busy={ocupado}
      title="Asociar al comparendo"
      description={`El archivo «${documento.nombreArchivo}» se agrega al comparendo ${comparendo.numero ?? ""}. Los datos del comparendo no cambian; sale adjunto en sus próximos avisos.`}
      footer={
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:flex-wrap sm:justify-end [&>button]:w-full sm:[&>button]:w-auto">
          <button type="button" className={controlCls(false)} disabled={ocupado} onClick={onClose}>
            Cancelar
          </button>
          <button type="button" data-autofocus className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={onConfirmar}>
            {ocupado ? "Asociando…" : "Asociar al comparendo"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm">
        <Numeros documento={documento.numeroComparendoExtraido ?? null} sistema={comparendo.numero} />
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
          <dt className="font-semibold">Fuente</dt>
          <dd className="min-w-0 break-words">{comparendo.fuente ?? "—"}</dd>
          <dt className="font-semibold">Estado</dt>
          <dd>
            {comparendo.activo === false ? "Inactivo" : "Activo"}
            {comparendo.pagado ? " · pagado" : ""}
          </dd>
        </dl>
        {aviso && (
          <p role="note" className="rounded-xl border border-[var(--badge-warning-fg)] px-3 py-2 text-xs text-[var(--badge-warning-fg)]">
            {aviso}
          </p>
        )}
      </div>
    </Modal>
  );
}

/** «Comparar documentos»: lado a lado el que entra y el que ya tiene el comparendo; reemplazar, descartar o cancelar. */
export function ModalComparar({
  comparendo,
  entrante,
  actual,
  ocupado,
  onReemplazar,
  onDescartar,
  onClose,
}: {
  comparendo: ComparendoExistente;
  entrante: { archivoId: string; nombreArchivo: string; tipoArchivo: string; numeroComparendoExtraido?: string | null };
  actual: EvidenciaActual;
  ocupado: boolean;
  onReemplazar: () => void;
  onDescartar: () => void;
  onClose: () => void;
}) {
  const aviso = avisoSinAvisos(comparendo);
  const tipoEntrante = entrante.tipoArchivo === "PDF" ? "application/pdf" : "";
  return (
    <Modal
      icono="documento"
      open
      onClose={onClose}
      busy={ocupado}
      size="2xl"
      title="Comparar documentos"
      description={`El comparendo ${comparendo.numero ?? ""} ya tiene un documento. Si lo reemplazas, el anterior queda en el historial del comparendo.`}
      footer={
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:flex-wrap sm:justify-end [&>button]:w-full sm:[&>button]:w-auto">
          <button type="button" data-autofocus className={controlCls(false)} disabled={ocupado} onClick={onClose}>
            Cancelar
          </button>
          <button type="button" className={controlCls(false)} disabled={ocupado} onClick={onDescartar}>
            Descartar este
          </button>
          <button type="button" className={BOTON_PELIGRO_CLS} disabled={ocupado} onClick={onReemplazar}>
            {ocupado ? "Reemplazando…" : "Reemplazar"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <Numeros documento={entrante.numeroComparendoExtraido ?? null} sistema={comparendo.numero} />
        {aviso && (
          <p role="note" className="rounded-xl border border-[var(--badge-warning-fg)] px-3 py-2 text-xs text-[var(--badge-warning-fg)]">
            {aviso}
          </p>
        )}
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Columna rotulo="Documento que entra" nombre={entrante.nombreArchivo} detalle="El que estás revisando">
            <Vista archivoId={entrante.archivoId} nombre={entrante.nombreArchivo} contentType={tipoEntrante} titulo={`Documento que entra: ${entrante.nombreArchivo}`} />
          </Columna>
          <Columna rotulo="Documento actual" nombre={actual.nombre} detalle={`${tamanoLegible(actual.tamanoBytes)} · cargado ${formatoFechaHora(actual.cargadaEn)}`}>
            <Vista archivoId={actual.archivoId} nombre={actual.nombre} contentType={actual.contentType} titulo={`Documento actual: ${actual.nombre}`} />
          </Columna>
        </div>
      </div>
    </Modal>
  );
}
