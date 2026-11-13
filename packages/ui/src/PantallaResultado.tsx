"use client";

import { useEffect, useRef, useState, type ElementType, type ReactNode } from "react";
import { cn } from "./cn";
import { Button, buttonClass } from "./Button";

/**
 * Pantalla de resultado del Manual UI Kit 2026 (HU #14562 · UI-1, AC18/AC19): tono, título, mensaje, acción primaria y
 * secundaria y, opcional, una redirección automática con «Quedarme aquí» (WCAG 2.2.1). La región aria-live polite
 * anuncia la redirección una vez y su cancelación; la cuenta visible no se repite en la región ni mueve el foco. El
 * temporizador se limpia al desmontar y al detener. Navegar lo hace quien la usa (`onNavegar`).
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

export function PantallaResultado({ tono, titulo, mensaje, primaria, secundaria, redireccion, className }: PantallaResultadoProps) {
  const segundos = redireccion?.segundos ?? 0;
  const [restante, setRestante] = useState(segundos);
  const [detenida, setDetenida] = useState(false);
  const navegar = useRef(redireccion?.onNavegar);
  useEffect(() => {
    navegar.current = redireccion?.onNavegar;
  });
  const activa = redireccion !== undefined && !detenida;

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
    setDetenida(true);
    redireccion?.onDetener?.();
  };

  return (
    <section
      data-tono={tono}
      className={cn(
        "mx-auto flex max-w-xl flex-col items-center gap-4 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] px-6 py-10 text-center",
        className,
      )}
    >
      <h2 className="text-2xl font-semibold text-[color:var(--di-heading)]">{titulo}</h2>
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
            {detenida
              ? "Detuvimos la redirección. Sigues en esta página."
              : `Te llevaremos a ${redireccion.destino} en ${redireccion.segundos} segundos.`}
          </p>
        </>
      ) : null}
    </section>
  );
}
