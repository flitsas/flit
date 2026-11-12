"use client";

import { useState, type InputHTMLAttributes, type ReactNode } from "react";
import { cn } from "./cn";
import { Icono, IconoModulo, type NombreIcono } from "./Icono";
import {
  INGRESO_MARCA_ANTETITULO_CLS,
  INGRESO_MARCA_TITULO_CLS,
  INGRESO_MARCA_TEXTO_CLS,
  INGRESO_TITULO_CLS,
  INGRESO_ETIQUETA_CLS,
  INGRESO_CONTROL_CLS,
  INGRESO_ICONO_CLS,
  INGRESO_ENLACE_CLS,
  INGRESO_ERROR_CLS,
  INGRESO_AVISO_CLS,
  INGRESO_BOTON_CLS,
} from "./ingreso-clases";

/**
 * Pantalla de ingreso del Manual UI Kit 2026 (propuesta-2, «ingreso sobrio»). Es solo el marco visual: a la izquierda
 * el panel de marca en tinta plana (#162744, igual en los dos temas) con el logo, el titular y un trámite de ejemplo
 * avanzando; a la derecha la tarjeta del formulario (radio 9 px, borde, sin sombra). Cada login (Trámites y hub) mete
 * adentro su propio formulario, con su lógica, sus textos y sus mensajes de siempre.
 *
 * El trámite de ejemplo («Traspaso · KLM 482…») es ilustrativo y va oculto para lectores de pantalla: muestra qué hace
 * FLIT, no datos reales.
 */

const PASOS_EJEMPLO: ReadonlyArray<[string, "hecho" | "actual" | ""]> = [
  ["Documentos", "hecho"],
  ["Identidad", "hecho"],
  ["Firmas", "actual"],
  ["Radicación", ""],
];

const HECHOS_EJEMPLO: ReadonlyArray<[NombreIcono, string, string]> = [
  ["pluma", "Traspaso firmado por el vendedor", "Hace 2 min"],
  ["escudo", "Identidad validada", "Comprador, en línea"],
  ["radicacion", "Listo para radicar en el organismo", "Medellín"],
];

const PRODUCTOS = [
  ["Trámites", "tramites"],
  ["Comparendos", "comparendos"],
  ["Diagnóstico", "diagnostico"],
] as const;

export function IngresoLayout({
  logo,
  logoTarjeta,
  pie,
  marca,
  children,
}: {
  /** Logo sobre el panel de tinta (variante blanca). */
  logo: ReactNode;
  /** Logo de la tarjeta, según el tema. */
  logoTarjeta?: ReactNode;
  /** Debajo del pie de certificación (p. ej. el enlace de políticas). */
  pie?: ReactNode;
  /**
   * Textos del panel de tinta. Si se omite, van los del ingreso. Las pantallas que ya traen sus propios textos
   * (activar cuenta) los pasan aquí para no perderlos; se pintan con las clases `INGRESO_MARCA_*`.
   */
  marca?: ReactNode;
  /** Contenido de la tarjeta: título y formulario o avisos. */
  children: ReactNode;
}) {
  return (
    <div className="grid min-h-screen w-full grid-cols-1 min-[961px]:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
      <div
        className="relative flex flex-col gap-6 p-[clamp(28px,4.5vw,64px)] text-[color:var(--di-ingreso-texto)]"
        style={{ background: "var(--di-ingreso-fondo)" }}
      >
        <div className="self-start [&_img]:h-[52px] [&_img]:w-auto">{logo}</div>
        <div>
          {marca ?? (
            <>
              <p className={INGRESO_MARCA_ANTETITULO_CLS}>Plataforma de trámites de tránsito</p>
              <p className={INGRESO_MARCA_TITULO_CLS}>
                Tus trámites vehiculares
                <span className="block">en un solo lugar</span>
              </p>
              <p className={INGRESO_MARCA_TEXTO_CLS}>
                Traspasos, matrículas, comparendos y el estado de cada vehículo, conectados con las fuentes oficiales y
                firmados en línea.
              </p>
            </>
          )}
        </div>
        {/* Trámite de ejemplo (`.ingreso__panel`): placa, estado, pasos y hechos recientes. Decorativo. */}
        <div
          aria-hidden="true"
          className="hidden w-[min(460px,100%)] gap-4 rounded-[9px] bg-[color:var(--di-ingreso-tarjeta)] p-5 text-[color:var(--di-text-primary)] md:grid"
        >
          <div className="flex items-center gap-3">
            <span className="rounded-[6px] border-2 border-[color:var(--kit-tinta)] bg-[#ffd200] px-2 py-0.5 font-[family-name:var(--font-placa)] text-lg font-bold tracking-[0.04em] text-[#111]">
              KLM 482
            </span>
            <span className="min-w-0 flex-1 leading-tight">
              <strong className="block">Traspaso</strong>
              <small className="block text-xs text-[color:var(--di-ingreso-tarjeta-texto-2)]">Mazda CX-30 · Medellín</small>
            </span>
            <span className="rounded-full bg-[#ffe9df] px-3 py-1 text-xs font-bold text-[#bd3a00]">En firma</span>
          </div>
          <ol className="m-0 grid list-none grid-cols-4 gap-2 p-0 text-xs">
            {PASOS_EJEMPLO.map(([paso, estado]) => (
              <li
                key={paso}
                className={cn(
                  "border-t-4 pt-1.5",
                  estado === "hecho" && "border-[#4f7121] text-[#4f7121]",
                  estado === "actual" && "border-[color:var(--kit-azul-relleno)] font-bold text-[color:var(--kit-azul-relleno)]",
                  estado === "" && "border-[#c9d6ee] text-[color:var(--di-ingreso-tarjeta-texto-2)]",
                )}
              >
                {paso}
              </li>
            ))}
          </ol>
          <ul className="m-0 grid list-none gap-3 p-0">
            {HECHOS_EJEMPLO.map(([icono, titulo, detalle]) => (
              <li key={titulo} className="flex items-center gap-3 text-sm leading-tight">
                <Icono nombre={icono} disco size={36} />
                <span>
                  <strong className="block font-semibold">{titulo}</strong>
                  <small className="block text-xs text-[color:var(--di-ingreso-tarjeta-texto-2)]">{detalle}</small>
                </span>
              </li>
            ))}
          </ul>
        </div>
        <ul aria-label="Productos de FLIT" className="m-0 mt-auto flex list-none flex-wrap gap-2.5 p-0">
          {PRODUCTOS.map(([nombre, clave]) => (
            <li
              key={clave}
              className="inline-flex items-center gap-2 rounded-full border border-[#ffffff40] bg-[#ffffff14] py-1.5 pl-1.5 pr-3.5 text-[0.8125rem] font-semibold"
            >
              <IconoModulo clave={clave} disco size={28} className="bg-white" />
              <span>{nombre}</span>
            </li>
          ))}
        </ul>
        <div className="grid gap-1 text-xs text-[color:var(--di-ingreso-texto-2)]">
          <p className="m-0">FLIT S.A.S. · Medellín, Colombia · Certificada ISO/IEC 27001:2022</p>
          {pie}
        </div>
      </div>

      <div className="grid place-items-center bg-[color:var(--di-surface)] px-[clamp(16px,5vw,64px)] py-8">
        <div className="grid w-[min(440px,100%)] gap-[18px] rounded-[9px] border border-[color:var(--di-border)] bg-[color:var(--di-surface-elevated)] p-[clamp(24px,3vw,40px)] shadow-none">
          {logoTarjeta ? <div className="[&_img]:h-[34px] [&_img]:w-auto">{logoTarjeta}</div> : null}
          {children}
        </div>
      </div>
    </div>
  );
}

export {
  INGRESO_MARCA_ANTETITULO_CLS,
  INGRESO_MARCA_TITULO_CLS,
  INGRESO_MARCA_TEXTO_CLS,
  INGRESO_TITULO_CLS,
  INGRESO_ETIQUETA_CLS,
  INGRESO_CONTROL_CLS,
  INGRESO_ICONO_CLS,
  INGRESO_ENLACE_CLS,
  INGRESO_ERROR_CLS,
  INGRESO_AVISO_CLS,
  INGRESO_BOTON_CLS,
};

/**
 * Campo de contraseña con el botón para verla (`.clave` de propuesta.css): el candado a la izquierda y, dentro del
 * control a la derecha, un botón redondo de 40 px que alterna entre `password` y `text`. Es `type="button"` para no
 * enviar el formulario. Recibe los mismos atributos del `input` (id, value, onChange, autoComplete…), salvo `type`.
 */
export function CampoClave({ className, ...props }: Omit<InputHTMLAttributes<HTMLInputElement>, "type">) {
  const [visible, setVisible] = useState(false);
  return (
    <div className="relative">
      <Icono nombre="candado" className={INGRESO_ICONO_CLS} />
      <input {...props} type={visible ? "text" : "password"} className={cn(INGRESO_CONTROL_CLS, "pr-[52px]", className)} />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? "Ocultar contraseña" : "Mostrar contraseña"}
        aria-pressed={visible}
        className={cn(
          "absolute right-1 top-1/2 grid h-10 w-10 -translate-y-1/2 place-items-center rounded-full border-0 bg-transparent transition hover:bg-[color:var(--kit-hover)] hover:text-[color:var(--di-action)]",
          visible ? "text-[color:var(--di-action)]" : "text-[color:var(--di-text-tertiary)]",
        )}
      >
        <Icono nombre="ojo" size={18} />
      </button>
    </div>
  );
}

/**
 * Línea de ayuda bajo el botón de enviar (`.ingreso__ayuda`): a quién escribir si no se puede entrar. Va dentro del
 * formulario, así solo sale en esa vista y no en los avisos de bloqueo.
 */
export function IngresoAyuda() {
  return (
    <p className="m-0 text-center text-xs text-[color:var(--di-text-tertiary)]">
      ¿Problemas para entrar? Escribe a{" "}
      <a href="mailto:soporte@flitsas.com" className="text-[color:var(--di-action)]">
        soporte@flitsas.com
      </a>
    </p>
  );
}
