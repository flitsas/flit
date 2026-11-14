"use client";

import { useEffect, useRef, useState, type ElementType, type FocusEvent, type ReactNode } from "react";
import { cn } from "./cn";
import { Button, buttonClass } from "./Button";

/**
 * Pantalla de resultado del Manual UI Kit 2026 (HU #14562 · UI-1, AC18/AC19): tono, título, mensaje, acción primaria y
 * secundaria y, opcional, una redirección automática con «Quedarme aquí» (WCAG 2.2.1). La región aria-live polite
 * anuncia la redirección una vez y su cancelación; la cuenta visible no se repite en la región. El temporizador se limpia
 * al desmontar y al detener. Navegar lo hace quien la usa (`onNavegar`).
 *
 * HU #14562, auditoría de UI M1 (2.2.1 con lector de pantalla y teclado):
 * - Al aparecer, el foco va al título: el control que lo tenía (el envío del formulario) ya no existe.
 * - La región viva se monta vacía y recibe el texto un instante después; una región que nace con contenido no se anuncia
 *   de forma fiable en NVDA, JAWS ni VoiceOver.
 * - Cualquier interacción dentro de la pantalla (tecla, toque o foco en otro control) detiene la cuenta, igual que
 *   «Quedarme aquí» (AC18: «a los 5 s sin interacción»). Si al detenerse desaparece el control enfocado, el foco vuelve
 *   al título.
 */
export interface AccionResultado {
  etiqueta: string;
  href?: string;
  onClick?: () => void;
  enlace?: ElementType;
}

export interface PantallaResultadoProps {
  tono: "exito" | "error" | "info";
  titulo: ReactNode;
  mensaje?: ReactNode;
  primaria: AccionResultado;
  secundaria?: AccionResultado;
  redireccion?: { segundos: number; destino: string; onNavegar: () => void; onDetener?: () => void };
  className?: string;
}

function Accion({ accion, variante }: { accion: AccionResultado; variante: "primary" | "secondary" }) {
  if (accion.href !== undefined) {
    const Enlace = accion.enlace ?? "a";
    return (
      <Enlace href={accion.href} onClick={accion.onClick} className={buttonClass({ variant: variante })}>
        {accion.etiqueta}
      </Enlace>
    );
  }
  return (
    <Button variant={variante} onClick={accion.onClick}>
      {accion.etiqueta}
    </Button>
  );
}

/** Espera antes de escribir el aviso en la región viva, para que el lector de pantalla ya la tenga registrada. */
const RETRASO_ANUNCIO_MS = 100;

export function PantallaResultado({ tono, titulo, mensaje, primaria, secundaria, redireccion, className }: PantallaResultadoProps) {
  const segundos = redireccion?.segundos ?? 0;
  const [restante, setRestante] = useState(segundos);
  const [detenida, setDetenida] = useState(false);
  const [anunciada, setAnunciada] = useState(false);
  const tituloRef = useRef<HTMLHeadingElement>(null);
  const navegar = useRef(redireccion?.onNavegar);
  const alDetener = useRef(redireccion?.onDetener);
  useEffect(() => {
    navegar.current = redireccion?.onNavegar;
    alDetener.current = redireccion?.onDetener;
  });
  const hayRedireccion = redireccion !== undefined;
  const activa = hayRedireccion && !detenida;

  useEffect(() => {
    tituloRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!hayRedireccion) return;
    const id = setTimeout(() => setAnunciada(true), RETRASO_ANUNCIO_MS);
    return () => clearTimeout(id);
  }, [hayRedireccion]);

  useEffect(() => {
    // El control enfocado pudo desaparecer con la cuenta («Quedarme aquí»): el foco no se pierde en el body.
    if (detenida && (document.activeElement === null || document.activeElement === document.body)) tituloRef.current?.focus();
  }, [detenida]);

  useEffect(() => {
    if (!activa) return;
    let quedan = segundos;
    const id = setInterval(() => {
      quedan -= 1;
      setRestante(quedan);
      if (quedan <= 0) {
        clearInterval(id);
        navegar.current?.();
      }
    }, 1000);
    return () => clearInterval(id);
  }, [activa, segundos]);

  const detener = () => {
    if (!activa) return;
    setDetenida(true);
    alDetener.current?.();
  };

  /** El foco que pone la propia pantalla en el título no es una interacción; cualquier otro sí. */
  const alEnfocar = (e: FocusEvent<HTMLElement>) => {
    if (e.target !== tituloRef.current) detener();
  };

  let anuncio = "";
  if (redireccion && detenida) anuncio = "Detuvimos la redirección. Sigues en esta página.";
  else if (redireccion && anunciada) anuncio = `Te llevaremos a ${redireccion.destino} en ${redireccion.segundos} segundos.`;

  return (
    <section
      data-tono={tono}
      onKeyDown={activa ? detener : undefined}
      onPointerDown={activa ? detener : undefined}
      onFocus={activa ? alEnfocar : undefined}
      className={cn(
        "mx-auto flex max-w-xl flex-col items-center gap-4 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] px-6 py-10 text-center",
        className,
      )}
    >
      <h2 ref={tituloRef} tabIndex={-1} className="text-2xl font-semibold text-[color:var(--di-heading)] focus:outline-none">
        {titulo}
      </h2>
      {mensaje ? <p className="text-sm text-[color:var(--di-text-tertiary)]">{mensaje}</p> : null}
      <div className="flex flex-wrap items-center justify-center gap-3">
        <Accion accion={primaria} variante="primary" />
        {secundaria ? <Accion accion={secundaria} variante="secondary" /> : null}
        {redireccion && !detenida ? (
          <Button variant="ghost" onClick={detener}>
            Quedarme aquí
          </Button>
        ) : null}
      </div>
      {redireccion ? (
        <>
          {!detenida ? (
            <p aria-hidden="true" className="text-xs text-[color:var(--di-text-tertiary)]">
              Redirección en <span data-parte="cuenta">{restante}</span> s
            </p>
          ) : null}
          <p aria-live="polite" className="sr-only">
            {anuncio}
          </p>
        </>
      ) : null}
    </section>
  );
}
