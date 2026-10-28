"use client";

// HU #14076 AC2 — Alta y edición de un banner del portal. La imagen (JPG, PNG o WEBP de hasta 2 MB) es obligatoria al
// crear y opcional al editar; el texto alternativo es obligatorio. Cada regla se valida aquí con un mensaje claro y
// no se guarda nada mientras algo falle. La vigencia es opcional, con hora de Colombia. Texto plano, sin HTML.
import { useEffect, useRef, useState } from "react";
import { ImageIcon, Save, UploadCloud } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { Modal } from "@/components/atom/Modal";
import {
  BANNER_IMAGEN_TIPOS,
  BANNER_TEXTO_ALTERNATIVO_MAX,
  BANNER_TITULO_MAX,
  utcIsoAColombiaLocal,
  type BannerPn,
  type GuardarBanner,
} from "@/lib/api/admin-pn-contenido";
import { PN_FIELD_ERROR_CLS, PN_HINT_CLS, PN_INPUT_CLS, PN_INPUT_ERROR_CLS, PN_LABEL_CLS } from "../pn-ui";
import { PnButton } from "../pn-componentes";
import { pnErrorMessage } from "../pnErrors";
import { BannerImagen } from "./BannerImagen";
import { validarEnlace, validarImagenBanner, validarTextoPlano } from "./contenidoReglas";

type Errores = Partial<Record<"titulo" | "textoAlternativo" | "archivo" | "enlaceUrl" | "vigencia" | "general", string>>;

export interface BannerFormModalProps {
  /** Sin `banner`: alta. */
  banner?: BannerPn;
  onClose: () => void;
  onSave: (input: GuardarBanner) => Promise<void>;
}

export function BannerFormModal({ banner, onClose, onSave }: BannerFormModalProps) {
  const [titulo, setTitulo] = useState(banner?.titulo ?? "");
  const [textoAlternativo, setTextoAlternativo] = useState(banner?.textoAlternativo ?? "");
  const [enlaceUrl, setEnlaceUrl] = useState(banner?.enlaceUrl ?? "");
  const [vigenteDesde, setVigenteDesde] = useState(utcIsoAColombiaLocal(banner?.vigenteDesde ?? null));
  const [vigenteHasta, setVigenteHasta] = useState(utcIsoAColombiaLocal(banner?.vigenteHasta ?? null));
  const [activo, setActivo] = useState(banner?.activo ?? true);
  const [archivo, setArchivo] = useState<File | null>(null);
  const [vistaPrevia, setVistaPrevia] = useState<string | null>(null);
  const [errores, setErrores] = useState<Errores>({});
  const [busy, setBusy] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  // Vista previa local del archivo elegido, antes de guardar; se libera al cambiar o desmontar.
  useEffect(() => {
    if (!archivo || typeof URL.createObjectURL !== "function") return;
    const url = URL.createObjectURL(archivo);
    // eslint-disable-next-line react-hooks/set-state-in-effect -- derivado del archivo elegido: se libera en el cleanup
    setVistaPrevia(url);
    return () => {
      URL.revokeObjectURL(url);
      setVistaPrevia(null);
    };
  }, [archivo]);

  function elegirArchivo(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0] ?? null;
    if (!f) {
      setArchivo(null);
      return;
    }
    const error = validarImagenBanner(f);
    if (error) {
      setArchivo(null);
      setErrores((prev) => ({ ...prev, archivo: error }));
      if (inputRef.current) inputRef.current.value = "";
      return;
    }
    setArchivo(f);
    setErrores((prev) => ({ ...prev, archivo: undefined }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const nuevos: Errores = {
      titulo: validarTextoPlano(titulo, "El título", BANNER_TITULO_MAX) ?? undefined,
      textoAlternativo:
        validarTextoPlano(textoAlternativo, "La descripción de la imagen", BANNER_TEXTO_ALTERNATIVO_MAX) ?? undefined,
      enlaceUrl: validarEnlace(enlaceUrl) ?? undefined,
      archivo: errores.archivo,
    };
    if (!banner && !archivo && !nuevos.archivo) nuevos.archivo = "Sube la imagen del banner (JPG, PNG o WEBP de hasta 2 MB).";
    if (vigenteDesde && vigenteHasta && vigenteHasta < vigenteDesde) {
      nuevos.vigencia = "La fecha de fin no puede ser anterior a la de inicio.";
    }
    setErrores(nuevos);
    if (Object.values(nuevos).some(Boolean)) return;

    setBusy(true);
    try {
      await onSave({ titulo, textoAlternativo, enlaceUrl, vigenteDesde, vigenteHasta, activo, archivo });
    } catch (err) {
      setErrores({ general: pnErrorMessage(err) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={banner ? "Editar banner" : "Nuevo banner"}
      description="El ciudadano lo verá cuando publiques el contenido."
      icon={ImageIcon}
      size="lg"
      footer={
        <div className="flex justify-end gap-2">
          <PnButton variant="secondary" onClick={onClose} disabled={busy}>
            Cancelar
          </PnButton>
          <PnButton type="submit" form="pn-banner-form" variant="primary" icon={Save} loading={busy}>
            Guardar banner
          </PnButton>
        </div>
      }
    >
      <form id="pn-banner-form" onSubmit={handleSubmit} noValidate className="flex flex-col gap-4">
        <label className={PN_LABEL_CLS}>
          Título
          <input
            value={titulo}
            onChange={(e) => setTitulo(e.target.value)}
            maxLength={BANNER_TITULO_MAX}
            aria-invalid={Boolean(errores.titulo)}
            className={`${PN_INPUT_CLS} ${errores.titulo ? PN_INPUT_ERROR_CLS : ""}`}
          />
          {errores.titulo && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.titulo}</span>}
        </label>

        <div className="flex flex-col gap-2">
          <span className="text-sm font-semibold text-flit-primary dark:text-white">Imagen</span>
          <InlineAlert tone="info" compact>
            JPG, PNG o WEBP, de hasta 2 MB.
          </InlineAlert>
          <div className="flex flex-wrap items-center gap-3">
            <label className="inline-flex cursor-pointer items-center gap-2 rounded-full border border-flit-brand px-4 py-2 text-xs font-semibold text-[color:var(--flit-brand-ink)] focus-within:ring-2 focus-within:ring-flit-brand focus-within:ring-offset-2 hover:bg-[#F4F8FF] dark:hover:bg-white/5">
              <UploadCloud className="h-4 w-4" aria-hidden="true" />
              {archivo || banner ? "Cambiar imagen" : "Elegir imagen"}
              <input
                ref={inputRef}
                type="file"
                accept={BANNER_IMAGEN_TIPOS.join(",")}
                onChange={elegirArchivo}
                aria-label="Imagen del banner"
                aria-invalid={Boolean(errores.archivo)}
                className="sr-only"
              />
            </label>
            {archivo && <span className="text-xs">{archivo.name} · {(archivo.size / 1024).toFixed(0)} KB</span>}
            {vistaPrevia ? (
              // eslint-disable-next-line @next/next/no-img-element -- vista previa local (blob)
              <img src={vistaPrevia} alt="Vista previa de la imagen elegida" className="h-16 w-32 rounded-lg border border-[#DFE5ED] object-cover" />
            ) : (
              banner && <BannerImagen id={banner.id} alt={`Imagen actual: ${banner.textoAlternativo}`} className="h-16 w-32 rounded-lg border border-[#DFE5ED] object-cover" />
            )}
          </div>
          {errores.archivo && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.archivo}</span>}
        </div>

        <label className={PN_LABEL_CLS}>
          Descripción de la imagen
          <input
            value={textoAlternativo}
            onChange={(e) => setTextoAlternativo(e.target.value)}
            maxLength={BANNER_TEXTO_ALTERNATIVO_MAX}
            aria-invalid={Boolean(errores.textoAlternativo)}
            aria-describedby="pn-banner-alt-hint"
            className={`${PN_INPUT_CLS} ${errores.textoAlternativo ? PN_INPUT_ERROR_CLS : ""}`}
          />
          <span id="pn-banner-alt-hint" className={PN_HINT_CLS}>
            Cuenta en una frase qué muestra la imagen, para personas con discapacidad visual que usan lector de pantalla (obligatorio).
          </span>
          {errores.textoAlternativo && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.textoAlternativo}</span>}
        </label>

        <label className={PN_LABEL_CLS}>
          Enlace (opcional)
          <input
            type="url"
            value={enlaceUrl}
            onChange={(e) => setEnlaceUrl(e.target.value)}
            placeholder="https://"
            aria-invalid={Boolean(errores.enlaceUrl)}
            className={`${PN_INPUT_CLS} ${errores.enlaceUrl ? PN_INPUT_ERROR_CLS : ""}`}
          />
          {errores.enlaceUrl && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.enlaceUrl}</span>}
        </label>

        <div className="grid gap-4 sm:grid-cols-2">
          <label className={PN_LABEL_CLS}>
            Mostrar desde (opcional)
            <input type="datetime-local" value={vigenteDesde} onChange={(e) => setVigenteDesde(e.target.value)} className={PN_INPUT_CLS} />
          </label>
          <label className={PN_LABEL_CLS}>
            Mostrar hasta (opcional)
            <input
              type="datetime-local"
              value={vigenteHasta}
              onChange={(e) => setVigenteHasta(e.target.value)}
              aria-invalid={Boolean(errores.vigencia)}
              className={`${PN_INPUT_CLS} ${errores.vigencia ? PN_INPUT_ERROR_CLS : ""}`}
            />
          </label>
          {errores.vigencia && <span role="alert" className={`${PN_FIELD_ERROR_CLS} sm:col-span-2`}>{errores.vigencia}</span>}
          <p className={`${PN_HINT_CLS} sm:col-span-2`}>Hora de Colombia. Sin fechas, el banner se muestra siempre que esté visible.</p>
        </div>

        <label className="flex cursor-pointer items-center gap-2 text-sm font-semibold text-flit-primary dark:text-white">
          <input type="checkbox" checked={activo} onChange={(e) => setActivo(e.target.checked)} className="h-4 w-4 accent-[color:var(--color-flit-brand)]" />
          Mostrar este banner en el portal
        </label>

        {errores.general && <InlineAlert tone="error">{errores.general}</InlineAlert>}
      </form>
    </Modal>
  );
}
