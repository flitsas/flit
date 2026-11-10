"use client";

import { useState, type InputHTMLAttributes, type ReactNode } from "react";
import { Check, Eye, EyeOff, Lock, Mail, type LucideIcon } from "lucide-react";
import { cn } from "./cn";
import { rutaIlustracion } from "./ilustraciones";

/**
 * Pantalla de ingreso del prototipo «Diseño Inteligente» (rama `propuesta`, `vIngreso` y `.ingreso*` de
 * propuesta.css), HU #14485. Es solo el marco visual: panel de marca en tinta (igual en los dos temas) a la izquierda
 * y la tarjeta del formulario a la derecha. Cada login (Trámites y hub) mete adentro su propio formulario, con su
 * lógica, sus textos y sus mensajes de siempre.
 *
 * Las tarjetas flotantes («Traspaso firmado · KLM 482…») son ilustrativas: muestran qué hace FLIT, no datos reales.
 * Del prototipo NO se trae el aviso de «Modo de muestra», que solo existe allá.
 */

const FLOTANTES: ReadonlyArray<{
  titulo: string;
  detalle: string;
  Icono: LucideIcon;
  icono: string;
  lugar: string;
  duracion: string;
  retraso: string;
}> = [
  {
    titulo: "Traspaso firmado",
    detalle: "KLM 482 · hace 2 min",
    Icono: Check,
    icono: "bg-[color:var(--di-ingreso-acento-2)] text-[color:var(--di-ingreso-fondo)]",
    lugar: "left-0 top-[12%] flex",
    duracion: "6s",
    retraso: "0s",
  },
  {
    titulo: "Identidad validada",
    detalle: "Comprador · en línea",
    Icono: Lock,
    icono: "bg-[color:var(--di-ingreso-azul)] text-[color:var(--di-ingreso-tarjeta)]",
    lugar: "right-0 top-[40%] hidden lg:flex",
    duracion: "7.5s",
    retraso: "0.8s",
  },
  {
    titulo: "Radicado en el organismo",
    detalle: "Medellín · hoy",
    Icono: Mail,
    icono: "bg-[color:var(--di-alerta-punto)] text-[color:var(--di-ingreso-tarjeta)]",
    lugar: "bottom-[4%] left-[8%] flex",
    duracion: "6.8s",
    retraso: "1.6s",
  },
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
  children,
}: {
  /** Logo sobre el panel de tinta (variante blanca). */
  logo: ReactNode;
  /** Logo de la tarjeta, según el tema. */
  logoTarjeta?: ReactNode;
  /** Debajo del pie de certificación (p. ej. el enlace de políticas). */
  pie?: ReactNode;
  /** Contenido de la tarjeta: título y formulario o avisos. */
  children: ReactNode;
}) {
  return (
    <div className="grid min-h-screen w-full grid-cols-1 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
      <div
        className="relative flex flex-col gap-6 overflow-clip p-[clamp(28px,4.5vw,64px)] text-[color:var(--di-ingreso-texto)]"
        style={{
          background:
            "radial-gradient(60% 50% at 85% 10%, color-mix(in srgb, var(--di-ingreso-acento-2) 30%, transparent), transparent 70%)," +
            "radial-gradient(70% 60% at 0% 100%, color-mix(in srgb, var(--di-ingreso-azul) 38%, transparent), transparent 70%)," +
            "var(--di-ingreso-fondo)",
        }}
      >
        {/* Aro decorativo de la derecha (.ingreso__marca::after). */}
        <span
          aria-hidden="true"
          className="pointer-events-none absolute -right-[320px] top-[18%] h-[640px] w-[640px] rounded-full border-[1.5px]"
          style={{ borderColor: "color-mix(in srgb, var(--di-ingreso-acento-2) 45%, transparent)" }}
        />
        <div className="relative z-[1] self-start [&_img]:h-14 [&_img]:w-auto">{logo}</div>
        <div className="relative z-[1]">
          <p className="mb-2 text-xs font-bold uppercase tracking-[0.08em] text-[color:var(--di-ingreso-acento-2)]">
            Plataforma de trámites de tránsito
          </p>
          <p className="m-0 text-[clamp(2rem,3.4vw,3rem)] font-extrabold leading-[1.08] tracking-[-0.01em]">
            Tus trámites vehiculares
            <span
              className="block bg-clip-text text-transparent"
              style={{ backgroundImage: "linear-gradient(90deg, var(--di-ingreso-acento-1), var(--di-ingreso-acento-2))" }}
            >
              en un solo lugar
            </span>
          </p>
          <p className="mt-3.5 max-w-[46ch] text-base text-[color:var(--di-ingreso-texto-2)]">
            Traspasos, matrículas, comparendos y el estado de cada vehículo, conectados con las fuentes oficiales y
            firmados en línea.
          </p>
        </div>
        <figure aria-hidden="true" className="relative z-[1] m-0 hidden min-h-[260px] flex-1 place-items-center md:grid">
          {/* eslint-disable-next-line @next/next/no-img-element -- ilustración decorativa estática */}
          <img
            src={rutaIlustracion("heroe-ingreso")}
            alt=""
            width={1100}
            height={821}
            className="di-flotar h-auto w-[min(560px,100%)]"
            style={{ filter: "drop-shadow(var(--di-ingreso-sombra-escena))" }}
          />
          {FLOTANTES.map(({ titulo, detalle, Icono, icono, lugar, duracion, retraso }) => (
            <div
              key={titulo}
              className={`di-flotar absolute items-center gap-2.5 rounded-xl bg-[color:var(--di-ingreso-tarjeta)] px-3.5 py-2.5 text-[0.8125rem] leading-tight text-[color:var(--di-ingreso-fondo)] shadow-[var(--di-ingreso-sombra-tarjeta)] ${lugar}`}
              /* Duración y retraso aparte: con «menos movimiento», .di-flotar apaga la animación y estos no la reviven. */
              style={{ animationDuration: duracion, animationDelay: retraso }}
            >
              <span className={`grid h-[30px] w-[30px] flex-none place-items-center rounded-full ${icono}`}>
                <Icono className="h-4 w-4" strokeWidth={2.4} />
              </span>
              <span>
                <strong className="block">{titulo}</strong>
                <small className="block text-xs text-[color:var(--di-ingreso-tarjeta-texto-2)]">{detalle}</small>
              </span>
            </div>
          ))}
        </figure>
        <ul aria-label="Productos de FLIT" className="relative z-[1] m-0 flex list-none flex-wrap gap-2.5 p-0">
          {PRODUCTOS.map(([nombre, ilus]) => (
            <li
              key={ilus}
              className="inline-flex items-center gap-2 rounded-full border py-1.5 pl-1.5 pr-3.5 text-[0.8125rem] font-semibold"
              style={{
                background: "color-mix(in srgb, var(--di-ingreso-texto) 10%, transparent)",
                borderColor: "color-mix(in srgb, var(--di-ingreso-texto) 16%, transparent)",
              }}
            >
              {/* eslint-disable-next-line @next/next/no-img-element -- ilustración decorativa estática */}
              <img
                src={rutaIlustracion(ilus)}
                alt=""
                aria-hidden="true"
                className="h-7 w-7 rounded-full bg-[color:var(--di-ingreso-tarjeta)] p-0.5"
              />
              <span>{nombre}</span>
            </li>
          ))}
        </ul>
        <div className="relative z-[1] grid gap-1 text-xs text-[color:var(--di-ingreso-texto-2)]">
          <p className="m-0">FLIT S.A.S. · Medellín, Colombia · Certificada ISO/IEC 27001:2022</p>
          {pie}
        </div>
      </div>

      <div className="grid place-items-center bg-[color:var(--di-surface)] px-[clamp(16px,5vw,64px)] py-8">
        <div className="grid w-[min(440px,100%)] gap-[18px] rounded-[20px] border border-[color:var(--di-border)] bg-[color:var(--di-surface-elevated)] p-[clamp(24px,3vw,40px)] shadow-[var(--shadow-modal)]">
          {logoTarjeta ? <div className="[&_img]:h-[34px] [&_img]:w-auto">{logoTarjeta}</div> : null}
          {children}
        </div>
      </div>
    </div>
  );
}

/** Título de la tarjeta de ingreso (`.ingreso__tarjeta h2`). */
export const INGRESO_TITULO_CLS = "m-0 text-2xl font-bold text-[color:var(--di-heading)]";
/** Etiqueta de campo (`.campo label`). */
export const INGRESO_ETIQUETA_CLS = "mb-1.5 block text-[0.8125rem] font-semibold text-[color:var(--di-text-primary)]";
/** Control con ícono a la izquierda (`.control-icono .control`). */
export const INGRESO_CONTROL_CLS =
  "w-full min-h-11 rounded-xl border border-[color:var(--di-border-control)] bg-[color:var(--di-surface-elevated)] " +
  "py-2.5 pl-[42px] pr-3.5 text-base text-[color:var(--di-text-primary)] outline-none transition " +
  "placeholder:text-[color:var(--di-text-tertiary)] focus:border-[color:var(--di-action)] " +
  "focus:ring-2 focus:ring-[color:var(--di-accent-soft)]";
/** Ícono dentro del control (`.control-icono svg`). */
export const INGRESO_ICONO_CLS =
  "pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[color:var(--di-text-tertiary)]";
/** Enlace de texto (`.enlace`). */
export const INGRESO_ENLACE_CLS =
  "inline-flex min-h-11 items-center text-sm font-medium text-[color:var(--di-action)] underline underline-offset-[3px]";
/** Mensaje de error del formulario (`.ingreso__error`). */
export const INGRESO_ERROR_CLS =
  "m-0 flex items-center gap-2 rounded-xl bg-[color:var(--di-danger-soft)] px-3.5 py-2.5 text-[0.8125rem] font-semibold text-[color:var(--di-danger)]";
/** Aviso de acceso (bloqueo, rol desactivado, dominio propio): misma caja suave del error. */
export const INGRESO_AVISO_CLS = "flex gap-3 rounded-xl p-5";
/** Botón de enviar (`.ingreso__boton`), sobre `buttonClass()`. */
export const INGRESO_BOTON_CLS = "w-full min-h-[50px] text-base";

/**
 * Campo de contraseña con el botón para verla (`.clave` de propuesta.css): el candado a la izquierda y, dentro del
 * control a la derecha, un botón redondo de 40 px que alterna entre `password` y `text`. Es `type="button"` para no
 * enviar el formulario. Recibe los mismos atributos del `input` (id, value, onChange, autoComplete…), salvo `type`.
 */
export function CampoClave({ className, ...props }: Omit<InputHTMLAttributes<HTMLInputElement>, "type">) {
  const [visible, setVisible] = useState(false);
  const Icono = visible ? EyeOff : Eye;
  return (
    <div className="relative">
      <Lock className={INGRESO_ICONO_CLS} />
      <input {...props} type={visible ? "text" : "password"} className={cn(INGRESO_CONTROL_CLS, "pr-[52px]", className)} />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? "Ocultar contraseña" : "Mostrar contraseña"}
        aria-pressed={visible}
        className="absolute right-1 top-1/2 grid h-10 w-10 -translate-y-1/2 place-items-center rounded-full border-0 bg-transparent text-[color:var(--di-text-tertiary)] transition hover:bg-[color:var(--di-accent-soft)] hover:text-[color:var(--di-action)]"
      >
        <Icono className="h-[18px] w-[18px]" aria-hidden="true" />
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
