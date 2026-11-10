"use client";

import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { ArrowDown, ArrowUp, ImagePlus, Images, Pencil, Trash2 } from "lucide-react";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { Modal } from "@/components/atom/Modal";
import {
  portalMarcaErrorCode,
  portalMarcaErrorMessage,
  type PortalMarca,
  type PortalMarcaCarruselItem,
} from "@/lib/api/admin-ot-portal-marca";
import { PORTAL_IMAGE_ACCEPT, validatePortalCarouselLink } from "@/lib/brand/validate-portal-image";
import type { ClienteMarcaPortal } from "./cliente-marca";
import { useAuthedImage } from "./useAuthedImage";

/** Operaciones que necesita el gestor (las cumplen `carrusel` y `banners` del cliente de marca). */
export interface ApiGestorImagenes {
  subir(file: File, alt: string, linkUrl: string | null): Promise<PortalMarca>;
  editar(imageId: string, body: { alt: string; linkUrl: string | null }): Promise<PortalMarca>;
  ordenar(imageIds: string[]): Promise<PortalMarca>;
  quitar(imageId: string): Promise<PortalMarca>;
}

/** Lo que cambia entre el carrusel y los banners: textos, tamaño recomendado y reglas de la imagen. */
export interface ConfigGestorImagenes {
  /** Prefijo de ids (únicos en la pantalla). */
  idBase: string;
  titulo: string;
  descripcion: string;
  max: number;
  botonAgregar: string;
  tituloModalAgregar: string;
  tituloModalEditar: string;
  tituloModalQuitar: string;
  listaLabel: string;
  /** Etiqueta del menú «Acciones» de cada fila; recibe la posición (1…) y el texto alternativo. */
  accionesLabel: (n: number, alt: string) => string;
  textoLleno: string;
  vacio: string;
  guia: ReactNode;
  /** Ayuda bajo el selector de archivo. */
  ayudaArchivo: string;
  placeholderAlt: string;
  /** Clases de la miniatura de cada fila. */
  miniatura: string;
  msgAgregado: string;
  msgActualizado: string;
  msgOrden: string;
  msgQuitado: string;
  errAgregar: string;
  errGuardar: string;
  errOrden: string;
  errQuitar: string;
  /** Texto de la confirmación de quitar; recibe el texto alternativo. */
  confirmarQuitar: (alt: string) => string;
  /** Motivo si el archivo no sirve; `null` si puede subirse. */
  validarArchivo: (file: File) => string | null;
  /** Aviso opcional que no bloquea (p. ej. proporción distinta a la recomendada). */
  avisarArchivo?: (file: File) => Promise<string | null>;
}

export interface GestorImagenesProps {
  config: ConfigGestorImagenes;
  items: PortalMarcaCarruselItem[];
  api: ApiGestorImagenes;
  imagen: ClienteMarcaPortal["imagen"];
  /** Solo lectura: se ven las imágenes pero no hay acciones. */
  readOnly: boolean;
  /** Otra operación de la pantalla en curso. */
  disabled: boolean;
  onChange: (marca: PortalMarca, mensaje: string) => void;
  /** Errores que debe mostrar la pantalla (conflicto de versión, etc.). */
  onError: (err: unknown, fallback: string) => void;
  onBusyChange: (busy: boolean) => void;
}

const BORDE = "var(--border)";
const FOCO =
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--ring)]";

/** Lista administrable de imágenes del portal (carrusel y banners): agregar, editar, ordenar y quitar sobre el borrador. */
export function GestorImagenes({
  config,
  items,
  api,
  imagen,
  readOnly,
  disabled,
  onChange,
  onError,
  onBusyChange,
}: GestorImagenesProps) {
  const [formFor, setFormFor] = useState<"new" | PortalMarcaCarruselItem | null>(null);
  const [removeTarget, setRemoveTarget] = useState<PortalMarcaCarruselItem | null>(null);
  const [pending, setPending] = useState(false);
  const [removeError, setRemoveError] = useState("");

  const lleno = items.length >= config.max;
  const bloqueado = disabled || pending;
  const titleId = `${config.idBase}-title`;
  const fullId = `${config.idBase}-full`;

  async function run(
    op: () => Promise<PortalMarca>,
    mensaje: string,
    fallback: string,
  ): Promise<string | null> {
    setPending(true);
    onBusyChange(true);
    try {
      const updated = await op();
      onChange(updated, mensaje);
      return null;
    } catch (err) {
      if (portalMarcaErrorCode(err) === "PORTAL_MARCA_CONFLICTO") onError(err, fallback);
      return portalMarcaErrorMessage(err, fallback);
    } finally {
      setPending(false);
      onBusyChange(false);
    }
  }

  async function mover(index: number, delta: -1 | 1) {
    const ids = items.map((i) => i.imageId);
    const [moved] = ids.splice(index, 1);
    ids.splice(index + delta, 0, moved);
    const error = await run(() => api.ordenar(ids), config.msgOrden, config.errOrden);
    if (error) onError(new Error(error), error);
  }

  async function confirmarQuitar() {
    if (!removeTarget) return;
    setRemoveError("");
    const error = await run(() => api.quitar(removeTarget.imageId), config.msgQuitado, config.errQuitar);
    if (error) setRemoveError(error);
    else setRemoveTarget(null);
  }

  return (
    <section className="flex flex-col gap-3" aria-labelledby={titleId}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="max-w-xl">
          <h3
            id={titleId}
            className="text-xs font-semibold"
            style={{ color: "var(--color-flit-primary)" }}
          >
            {config.titulo}
          </h3>
          <p className="text-xs opacity-70">{config.descripcion}</p>
        </div>
        {!readOnly && (
          <div className="flex flex-col items-end gap-1">
            <button
              type="button"
              onClick={() => setFormFor("new")}
              disabled={bloqueado || lleno}
              aria-describedby={lleno ? fullId : undefined}
              className={`${FOCO} inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-[color:var(--di-on-accent)] disabled:cursor-not-allowed disabled:opacity-50`}
              style={{ background: "var(--di-accent)" }}
            >
              <ImagePlus className="h-3.5 w-3.5" aria-hidden /> {config.botonAgregar}
            </button>
            {lleno && (
              <p id={fullId} className="text-xs opacity-70">
                {config.textoLleno}
              </p>
            )}
          </div>
        )}
      </div>

      <p className="rounded-xl border px-3 py-2 text-xs" style={{ borderColor: BORDE }}>
        {config.guia}
      </p>

      {items.length === 0 ? (
        <p
          role="status"
          className="flex items-center justify-center gap-2 rounded-xl border border-dashed px-4 py-6 text-center text-xs opacity-70"
          style={{ borderColor: BORDE }}
        >
          <Images className="h-4 w-4 shrink-0" aria-hidden />
          {config.vacio}
        </p>
      ) : (
        <ol className="flex flex-col gap-2" aria-label={config.listaLabel}>
          {items.map((item, index) => (
            <FilaImagen
              key={item.imageId}
              config={config}
              item={item}
              index={index}
              total={items.length}
              imagen={imagen}
              readOnly={readOnly}
              disabled={bloqueado}
              onEdit={() => setFormFor(item)}
              onMove={(delta) => void mover(index, delta)}
              onRemove={() => {
                setRemoveError("");
                setRemoveTarget(item);
              }}
            />
          ))}
        </ol>
      )}

      {formFor && (
        <FormularioImagen
          key={formFor === "new" ? "new" : formFor.imageId}
          config={config}
          item={formFor === "new" ? null : formFor}
          busy={pending}
          onClose={() => setFormFor(null)}
          onSubmit={async ({ file, alt, linkUrl }) => {
            const target = formFor;
            const error = await run(
              () =>
                target === "new"
                  ? api.subir(file as File, alt, linkUrl)
                  : api.editar(target.imageId, { alt, linkUrl }),
              target === "new" ? config.msgAgregado : config.msgActualizado,
              target === "new" ? config.errAgregar : config.errGuardar,
            );
            if (!error) setFormFor(null);
            return error;
          }}
        />
      )}

      <Modal
        open={removeTarget !== null}
        onClose={() => setRemoveTarget(null)}
        busy={pending}
        size="sm"
        icon={Trash2}
        title={config.tituloModalQuitar}
      >
        <p className="text-sm opacity-80">{config.confirmarQuitar(removeTarget?.alt ?? "")}</p>
        {removeError && (
          <p role="alert" className="mt-3 text-xs" style={{ color: "var(--destructive)" }}>
            {removeError}
          </p>
        )}
        <div className="mt-5 flex gap-3">
          <button
            type="button"
            onClick={() => setRemoveTarget(null)}
            disabled={pending}
            className={`${FOCO} flex-1 rounded-full border py-2.5 text-sm font-medium disabled:opacity-60`}
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void confirmarQuitar()}
            disabled={pending}
            className={`${FOCO} flex-1 rounded-full py-2.5 text-sm font-semibold text-destructive-foreground disabled:opacity-60`}
            style={{ background: "var(--destructive)" }}
          >
            {pending ? "Quitando…" : "Quitar"}
          </button>
        </div>
      </Modal>
    </section>
  );
}

interface FilaProps {
  config: ConfigGestorImagenes;
  item: PortalMarcaCarruselItem;
  index: number;
  total: number;
  imagen: ClienteMarcaPortal["imagen"];
  readOnly: boolean;
  disabled: boolean;
  onEdit: () => void;
  onMove: (delta: -1 | 1) => void;
  onRemove: () => void;
}

function FilaImagen({ config, item, index, total, imagen, readOnly, disabled, onEdit, onMove, onRemove }: FilaProps) {
  const src = useAuthedImage(item.imageUrl, imagen);
  return (
    <li className="flex items-center gap-3 rounded-xl border p-2" style={{ borderColor: BORDE }}>
      <span className="w-5 shrink-0 text-center text-xs font-semibold opacity-70" aria-hidden>
        {index + 1}
      </span>
      <div className={`${config.miniatura} shrink-0 overflow-hidden rounded-lg border bg-secondary`} style={{ borderColor: BORDE }}>
        {src && (
          // eslint-disable-next-line @next/next/no-img-element -- imagen del borrador (URL de objeto)
          <img src={src} alt={item.alt} className="h-full w-full object-cover" />
        )}
      </div>
      <div className="min-w-0 flex-1 text-xs">
        <p className="truncate font-semibold">{item.alt}</p>
        <p className="truncate opacity-70">{item.linkUrl ? item.linkUrl : "Sin enlace"}</p>
      </div>
      {!readOnly && (
        <div className="w-28 shrink-0">
          <ActionsMenu
            ariaLabel={config.accionesLabel(index + 1, item.alt)}
            items={[
              { key: "editar", label: "Editar", icon: Pencil, onSelect: onEdit, disabled },
              {
                key: "subir",
                label: "Subir",
                icon: ArrowUp,
                onSelect: () => onMove(-1),
                disabled: disabled || index === 0,
                disabledReason: "Ya es la primera.",
              },
              {
                key: "bajar",
                label: "Bajar",
                icon: ArrowDown,
                onSelect: () => onMove(1),
                disabled: disabled || index === total - 1,
                disabledReason: "Ya es la última.",
              },
              { key: "quitar", label: "Quitar", icon: Trash2, onSelect: onRemove, disabled, tone: "danger" },
            ]}
          />
        </div>
      )}
    </li>
  );
}

interface FormularioProps {
  config: ConfigGestorImagenes;
  /** `null` = imagen nueva. */
  item: PortalMarcaCarruselItem | null;
  busy: boolean;
  onClose: () => void;
  /** Devuelve el motivo si falla, o `null` si salió bien. */
  onSubmit: (v: { file: File | null; alt: string; linkUrl: string | null }) => Promise<string | null>;
}

function FormularioImagen({ config, item, busy, onClose, onSubmit }: FormularioProps) {
  const nueva = item === null;
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState("");
  const [fileWarning, setFileWarning] = useState("");
  const [alt, setAlt] = useState(item?.alt ?? "");
  const [link, setLink] = useState(item?.linkUrl ?? "");
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState("");
  const altRef = useRef<HTMLInputElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [arrastrando, setArrastrando] = useState(false);
  const ids = { file: `${config.idBase}-file`, hint: `${config.idBase}-file-hint`, alt: `${config.idBase}-alt`, link: `${config.idBase}-link` };

  useEffect(() => {
    if (!nueva) altRef.current?.focus();
  }, [nueva]);

  // Vista previa local del archivo elegido; se libera la URL al cambiar de archivo o cerrar.
  const vistaPrevia = useMemo(() => (file && typeof URL.createObjectURL === "function" ? URL.createObjectURL(file) : null), [file]);
  useEffect(() => {
    return () => {
      if (vistaPrevia) URL.revokeObjectURL(vistaPrevia);
    };
  }, [vistaPrevia]);

  // Misma validación para el clic y para arrastrar y soltar.
  function elegirArchivo(chosen: File | null) {
    const reason = chosen ? config.validarArchivo(chosen) : null;
    setFileError(reason ?? "");
    setFileWarning("");
    setFile(reason ? null : chosen);
    if (chosen && !reason && config.avisarArchivo) {
      void config.avisarArchivo(chosen).then((aviso) => setFileWarning(aviso ?? ""));
    }
  }

  const altTrim = alt.trim();
  const altError = !altTrim ? "Escribe el texto alternativo." : altTrim.length > 120 ? "Máximo 120 caracteres." : "";
  const linkError = validatePortalCarouselLink(link) ?? "";
  const fileMissing = nueva && !file && !fileError ? "Elige una imagen." : "";

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmitted(true);
    setServerError("");
    if (altError || linkError || fileMissing || fileError) return;
    const error = await onSubmit({ file, alt: altTrim, linkUrl: link.trim() || null });
    if (error) setServerError(error);
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      size="md"
      icon={nueva ? ImagePlus : Pencil}
      title={nueva ? config.tituloModalAgregar : config.tituloModalEditar}
    >
      <form onSubmit={(e) => void handleSubmit(e)} noValidate className="flex flex-col gap-4">
        {nueva && (
          <div className="flex flex-col gap-1">
            <label htmlFor={ids.file} className="text-xs font-semibold" style={{ color: "var(--color-flit-primary)" }}>
              Imagen
            </label>
            {/* Zona de carga: clic o arrastrar. El <input> real queda oculto pero accesible (etiqueta, teclado, foco). */}
            <div
              onClick={() => {
                if (!busy) inputRef.current?.click();
              }}
              onDragOver={(e) => {
                e.preventDefault();
                if (!busy) setArrastrando(true);
              }}
              onDragLeave={() => setArrastrando(false)}
              onDrop={(e) => {
                e.preventDefault();
                setArrastrando(false);
                if (!busy) elegirArchivo(e.dataTransfer.files?.[0] ?? null);
              }}
              className={`flex cursor-pointer items-center gap-4 rounded-xl border-2 border-dashed p-4 transition-colors focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-[var(--ring)] ${
                busy ? "cursor-not-allowed opacity-60" : "hover:bg-[color:var(--di-accent-soft)]"
              } ${arrastrando ? "bg-[color:var(--di-accent-soft)]" : "bg-card"}`}
              style={{ borderColor: arrastrando || fileError ? (fileError ? "var(--destructive)" : "var(--di-action)") : BORDE }}
            >
              {vistaPrevia ? (
                // eslint-disable-next-line @next/next/no-img-element -- vista previa local (blob:) del archivo elegido
                <img src={vistaPrevia} alt="" className="h-16 w-28 shrink-0 rounded-md border bg-card object-contain" style={{ borderColor: BORDE }} />
              ) : (
                <span aria-hidden className="grid h-16 w-16 shrink-0 place-items-center rounded-lg" style={{ background: "var(--di-accent-soft)", color: "var(--di-action)" }}>
                  <ImagePlus className="h-7 w-7" />
                </span>
              )}
              <div className="flex min-w-0 flex-col items-start gap-1">
                <span
                  className="inline-flex items-center rounded-full border px-4 py-1.5 text-xs font-semibold"
                  style={{ borderColor: "var(--di-action)", color: "var(--di-action)" }}
                >
                  {file ? "Cambiar imagen" : "Seleccionar imagen"}
                </span>
                <span className="max-w-full truncate text-xs opacity-80">{file ? file.name : "O arrástrala aquí"}</span>
              </div>
            </div>
            <input
              ref={inputRef}
              id={ids.file}
              type="file"
              accept={PORTAL_IMAGE_ACCEPT}
              disabled={busy}
              aria-describedby={ids.hint}
              aria-invalid={Boolean(fileError || (submitted && fileMissing))}
              onChange={(e) => elegirArchivo(e.target.files?.[0] ?? null)}
              className="sr-only"
            />
            <p id={ids.hint} className="text-xs opacity-70">
              {config.ayudaArchivo}
            </p>
            {(fileError || (submitted && fileMissing)) && (
              <p role="alert" className="text-xs" style={{ color: "var(--destructive)" }}>
                {fileError || fileMissing}
              </p>
            )}
            {fileWarning && !fileError && (
              <p role="status" className="text-xs" style={{ color: "var(--di-warning)" }}>
                {fileWarning}
              </p>
            )}
          </div>
        )}

        <div className="flex flex-col gap-1">
          <label htmlFor={ids.alt} className="text-xs font-semibold" style={{ color: "var(--color-flit-primary)" }}>
            Texto alternativo
          </label>
          <input
            id={ids.alt}
            ref={altRef}
            type="text"
            value={alt}
            onChange={(e) => setAlt(e.target.value)}
            maxLength={120}
            disabled={busy}
            placeholder={config.placeholderAlt}
            aria-invalid={Boolean(submitted && altError)}
            className={`${FOCO} w-full rounded-lg border px-3 py-2 text-sm`}
            style={{ borderColor: BORDE }}
          />
          {submitted && altError && (
            <p role="alert" className="text-xs" style={{ color: "var(--destructive)" }}>
              {altError}
            </p>
          )}
        </div>

        <div className="flex flex-col gap-1">
          <label htmlFor={ids.link} className="text-xs font-semibold" style={{ color: "var(--color-flit-primary)" }}>
            Enlace (opcional)
          </label>
          <input
            id={ids.link}
            type="url"
            value={link}
            onChange={(e) => setLink(e.target.value)}
            maxLength={500}
            disabled={busy}
            placeholder="https://…"
            aria-invalid={Boolean(linkError)}
            className={`${FOCO} w-full rounded-lg border px-3 py-2 text-sm`}
            style={{ borderColor: BORDE }}
          />
          {linkError && (
            <p role="alert" className="text-xs" style={{ color: "var(--destructive)" }}>
              {linkError}
            </p>
          )}
        </div>

        {serverError && (
          <p role="alert" className="text-xs" style={{ color: "var(--destructive)" }}>
            {serverError}
          </p>
        )}

        <div className="flex gap-3">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className={`${FOCO} flex-1 rounded-full border py-2.5 text-sm font-medium disabled:opacity-60`}
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={busy}
            className={`${FOCO} flex-1 rounded-full py-2.5 text-sm font-semibold text-[color:var(--di-on-action)] disabled:opacity-60`}
            style={{ background: "var(--di-gradient-action)" }}
          >
            {busy ? "Guardando…" : nueva ? "Agregar" : "Guardar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
