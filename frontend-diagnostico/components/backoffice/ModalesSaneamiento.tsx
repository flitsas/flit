"use client";

import { useId, useRef, useState, type DragEvent, type ReactNode } from "react";
import { Button, buttonClass } from "@flit/ui/Button";
import { Modal } from "@flit/ui/Modal";
import { ZonaArrastre } from "@flit/ui/ZonaArrastre";
import { AYUDA_CLS, CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";
import { MAXIMO_EVIDENCIA_BYTES } from "@/lib/backoffice/saneamientos";
import { MENSAJE_UN_SOLO_ARCHIVO, validarEvidenciaPdf } from "@/lib/backoffice/evidenciaPdf";
import { LIMITES_TEXTO, validarTextoLibre } from "@/lib/saneamiento/textoLibre";

// Modales de la gestión de un saneamiento (spec-ui §6.3.1–§6.5.1; HU #14577 AC30–AC35). Todos sobre `Modal` del kit
// (foco atrapado, Escape, X y retorno del foco); mientras hay una petición en vuelo quedan `busy` y no se cierran. Los
// errores del servidor llegan por `error` y se pintan con role="alert" dentro del modal.

/** Error de una acción en el modal; con `recargar`, el aviso ofrece «Recargar» (409 TRANSICION_INVALIDA). */
export interface ErrorModal {
  texto: string;
  recargar?: boolean;
}

/** «Quitar» de las confirmaciones: contorno con el color de peligro del kit (hueco H-2: Button no tiene `danger`). */
export const BOTON_PELIGRO_CLS = buttonClass({
  variant: "secondary",
  size: "sm",
  className: "border border-[color:var(--di-danger)] bg-transparent text-[color:var(--di-danger)] hover:bg-[color:var(--kit-hover)]",
});

function AvisoError({ error, onRecargar }: { error: ErrorModal | null; onRecargar?: () => void }) {
  if (!error) return null;
  return (
    <div role="alert" className="flex flex-wrap items-center gap-2 text-sm font-semibold text-[color:var(--di-danger)]">
      <span>{error.texto}</span>
      {error.recargar && onRecargar && (
        <Button type="button" variant="ghost" size="sm" onClick={onRecargar}>
          Recargar
        </Button>
      )}
    </div>
  );
}

function Pie({ ocupado, verbo, onCerrar, onConfirmar, autofocoCancelar }: { ocupado: boolean; verbo: string; onCerrar: () => void; onConfirmar: () => void; autofocoCancelar?: boolean }) {
  return (
    <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
      <Button type="button" variant="secondary" size="sm" onClick={onCerrar} disabled={ocupado} data-autofocus={autofocoCancelar ? true : undefined}>
        Cancelar
      </Button>
      <Button type="button" size="sm" cargando={ocupado} onClick={onConfirmar}>
        {verbo}
      </Button>
    </div>
  );
}

export interface ModalConfirmarProps {
  titulo: string;
  texto: ReactNode;
  verbo: string;
  /** «Quitar»: contorno de peligro en vez del primario. */
  peligro?: boolean;
  ocupado: boolean;
  error: ErrorModal | null;
  onCerrar: () => void;
  onConfirmar: () => void;
  onRecargar?: () => void;
}

/** Confirmación sin campos: «Cancelar» recibe el foco inicial (spec-ui §6.3.3 y §6.5). */
export function ModalConfirmar({ titulo, texto, verbo, peligro, ocupado, error, onCerrar, onConfirmar, onRecargar }: ModalConfirmarProps) {
  return (
    <Modal
      open
      size="sm"
      title={titulo}
      onClose={onCerrar}
      busy={ocupado}
      footer={
        peligro ? (
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button type="button" variant="secondary" size="sm" onClick={onCerrar} disabled={ocupado} data-autofocus>
              Cancelar
            </Button>
            <button type="button" onClick={onConfirmar} aria-busy={ocupado || undefined} aria-disabled={ocupado || undefined} className={BOTON_PELIGRO_CLS}>
              {verbo}
            </button>
          </div>
        ) : (
          <Pie ocupado={ocupado} verbo={verbo} onCerrar={onCerrar} onConfirmar={onConfirmar} autofocoCancelar />
        )
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-flit-primary">{texto}</p>
        <AvisoError error={error} onRecargar={onRecargar} />
      </div>
    </Modal>
  );
}

export interface ModalMotivoProps {
  titulo: string;
  texto?: ReactNode;
  verbo: string;
  ocupado: boolean;
  error: ErrorModal | null;
  /** Error del campo (422 MOTIVO_REQUERIDO). */
  errorCampo?: string | null;
  onCerrar: () => void;
  onConfirmar: (motivo: string) => void;
  onRecargar?: () => void;
}

/**
 * Modal con «Motivo» (cerrar como no procede, anular y reabrir; spec-ui §6.5.1): 10 a 500 caracteres, validado al
 * confirmar (no mientras escribe). El foco inicial va al campo, porque es un formulario.
 */
export function ModalMotivo({ titulo, texto, verbo, ocupado, error, errorCampo, onCerrar, onConfirmar, onRecargar }: ModalMotivoProps) {
  const id = useId();
  const campo = useRef<HTMLTextAreaElement>(null);
  const [motivo, setMotivo] = useState("");
  const [invalido, setInvalido] = useState<string | null>(null);
  const mensaje = invalido ?? errorCampo ?? null;

  function confirmar() {
    if (ocupado) return;
    const problema = validarTextoLibre(motivo, LIMITES_TEXTO.motivo);
    if (problema) {
      setInvalido(problema);
      campo.current?.focus();
      return;
    }
    setInvalido(null);
    onConfirmar(motivo.trim());
  }

  return (
    <Modal open size="md" title={titulo} onClose={onCerrar} busy={ocupado} footer={<Pie ocupado={ocupado} verbo={verbo} onCerrar={onCerrar} onConfirmar={confirmar} />}>
      <div className="flex flex-col gap-3">
        {texto && <p className="text-sm text-flit-primary">{texto}</p>}
        <div className="flex flex-col gap-1">
          <label htmlFor={`${id}-motivo`} className={ETIQUETA_CLS}>
            Motivo
          </label>
          <textarea
            ref={campo}
            id={`${id}-motivo`}
            data-autofocus
            rows={3}
            maxLength={LIMITES_TEXTO.motivo.max}
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
            readOnly={ocupado}
            aria-invalid={mensaje ? "true" : undefined}
            aria-describedby={`${id}-ayuda${mensaje ? ` ${id}-error` : ""}`}
            className={`${CAMPO_CLS} h-auto min-h-20 py-2 text-sm`}
          />
          <p id={`${id}-ayuda`} className={`${AYUDA_CLS} flex justify-between gap-2`}>
            <span>Entre 10 y 500 caracteres. La compañía lo verá.</span>
            <span className="tabular-nums">{`${motivo.length}/${LIMITES_TEXTO.motivo.max}`}</span>
          </p>
          {mensaje && (
            <p id={`${id}-error`} className="text-xs font-semibold text-[color:var(--di-danger)]">
              {mensaje}
            </p>
          )}
        </div>
        <AvisoError error={error} onRecargar={onRecargar} />
      </div>
    </Modal>
  );
}

export interface ModalAgregarConceptoProps {
  ocupado: boolean;
  error: ErrorModal | null;
  onCerrar: () => void;
  onAgregar: (descripcion: string) => void;
}

/** «Agregar otro concepto» (spec-ui §6.3.1): descripción obligatoria de 3 a 200 caracteres. */
export function ModalAgregarConcepto({ ocupado, error, onCerrar, onAgregar }: ModalAgregarConceptoProps) {
  const id = useId();
  const campo = useRef<HTMLInputElement>(null);
  const [descripcion, setDescripcion] = useState("");
  const [invalido, setInvalido] = useState<string | null>(null);

  function agregar() {
    if (ocupado) return;
    const problema = validarTextoLibre(descripcion, LIMITES_TEXTO.descripcion);
    if (problema) {
      setInvalido(problema);
      campo.current?.focus();
      return;
    }
    setInvalido(null);
    onAgregar(descripcion.trim());
  }

  return (
    <Modal
      open
      size="md"
      title="Agregar otro concepto"
      icono="mas"
      onClose={onCerrar}
      busy={ocupado}
      footer={<Pie ocupado={ocupado} verbo="Agregar" onCerrar={onCerrar} onConfirmar={agregar} />}
    >
      <div className="flex flex-col gap-1">
        <label htmlFor={`${id}-descripcion`} className={ETIQUETA_CLS}>
          Descripción
        </label>
        <input
          ref={campo}
          id={`${id}-descripcion`}
          data-autofocus
          type="text"
          maxLength={LIMITES_TEXTO.descripcion.max}
          value={descripcion}
          onChange={(e) => setDescripcion(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              agregar();
            }
          }}
          readOnly={ocupado}
          required
          aria-invalid={invalido ? "true" : undefined}
          aria-describedby={`${id}-ayuda${invalido ? ` ${id}-error` : ""}`}
          className={CAMPO_CLS}
          autoComplete="off"
        />
        <p id={`${id}-ayuda`} className={AYUDA_CLS}>
          Por ejemplo: embargo o prenda que las fuentes no reflejan.
        </p>
        {invalido && (
          <p id={`${id}-error`} className="text-xs font-semibold text-[color:var(--di-danger)]">
            {invalido}
          </p>
        )}
        <AvisoError error={error} />
      </div>
    </Modal>
  );
}

export interface ModalEvidenciaProps {
  concepto: string;
  ocupado: boolean;
  /** Error del servidor (413, 422, 400, 409 DEMASIADOS_REEMPLAZOS). */
  error: string | null;
  onCerrar: () => void;
  onSubir: (archivo: File) => void;
}

/**
 * Carga o reemplazo del PDF de evidencia (spec-ui §6.3.2, D-05): `ZonaArrastre` con un solo PDF de hasta 10 MB. Antes de
 * habilitar «Subir» comprueba en el cliente la firma `%PDF-` y el `%%EOF` (SP-17); dos archivos a la vez se rechazan.
 * El servidor vuelve a validar todo y manda.
 */
export function ModalEvidencia({ concepto, ocupado, error, onCerrar, onSubir }: ModalEvidenciaProps) {
  const [archivo, setArchivo] = useState<File | null>(null);
  const [rechazo, setRechazo] = useState<string | null>(null);
  const [varios, setVarios] = useState(false);
  const turno = useRef(0);

  async function recibir(f: File | null) {
    const n = ++turno.current;
    setVarios(false);
    setRechazo(null);
    setArchivo(null);
    if (!f) return;
    const problema = await validarEvidenciaPdf(f);
    if (n !== turno.current) return;
    if (problema) setRechazo(problema);
    else setArchivo(f);
  }

  /** Un solo archivo (SP-16): si sueltan varios, se rechaza antes de que la zona tome el primero. */
  function alSoltarCaptura(e: DragEvent<HTMLDivElement>) {
    if ((e.dataTransfer?.files?.length ?? 0) <= 1) return;
    e.preventDefault();
    e.stopPropagation();
    turno.current++;
    setArchivo(null);
    setRechazo(null);
    setVarios(true);
  }

  return (
    <Modal
      open
      size="md"
      title={`Evidencia de ${concepto}`}
      icono="archivo-subir"
      onClose={onCerrar}
      busy={ocupado}
      footer={
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button type="button" variant="secondary" size="sm" onClick={onCerrar} disabled={ocupado}>
            Cancelar
          </Button>
          <Button type="button" size="sm" cargando={ocupado} disabled={!archivo && !ocupado} onClick={() => archivo && !ocupado && onSubir(archivo)}>
            {ocupado ? "Subiendo…" : "Subir"}
          </Button>
        </div>
      }
    >
      <div onDropCapture={alSoltarCaptura} className="flex flex-col gap-2">
        <ZonaArrastre
          etiqueta="PDF de evidencia de pago"
          aceptar={[".pdf"]}
          maxBytes={MAXIMO_EVIDENCIA_BYTES}
          archivo={archivo}
          onArchivo={(f) => void recibir(f)}
          ayuda="Un PDF de hasta 10 MB."
          error={rechazo ?? error ?? undefined}
        />
        {varios && <p role="alert" className="text-sm font-semibold text-[color:var(--di-danger)]">{MENSAJE_UN_SOLO_ARCHIVO}</p>}
      </div>
    </Modal>
  );
}
