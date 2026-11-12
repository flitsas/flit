"use client";

import { useState, type InputHTMLAttributes, type ReactNode } from "react";
import { cn } from "./cn";
import { Icono, IconoModulo, type NombreIcono } from "./Icono";
import { rutaIlustracion } from "./ilustraciones";
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
 * En el panel va la escena del carro flotando con sus tarjetas («Traspaso firmado · KLM 482…»), como en el diseño
 * anterior (decisión de Samuel, 2026-11-12). Es ilustrativa y va oculta para lectores de pantalla: muestra qué hace
 * FLIT, no datos reales. Con «menos movimiento», `.di-flotar` deja todo quieto.
 */

const FLOTANTES: ReadonlyArray<{
  titulo: string;
  detalle: string;
  icono: NombreIcono;
  disco: string;
  lugar: string;
  duracion: string;
  retraso: string;
}> = [
  {
    titulo: "Traspaso firmado",
    detalle: "KLM 482 · hace 2 min",
    icono: "check",
    disco: "bg-[color:var(--di-ingreso-acento-2)] text-[color:var(--di-ingreso-fondo)]",
    lugar: "-left-[6%] top-[12%] flex",
    duracion: "6s",
    retraso: "0s",
  },
  {
    titulo: "Identidad validada",
    detalle: "Comprador · en línea",
    icono: "candado",
    disco: "bg-[color:var(--kit-azul-relleno)] text-[color:var(--di-ingreso-tarjeta)]",
    lugar: "-right-[6%] top-[40%] hidden lg:flex",
    duracion: "7.5s",
    retraso: "0.8s",
  },
  {
    titulo: "Radicado en el organismo",
    detalle: "Medellín · hoy",
    icono: "correo",
    disco: "bg-[color:var(--di-alerta-punto)] text-[color:var(--di-ingreso-tarjeta)]",
    lugar: "bottom-[4%] left-[2%] flex",
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
        {/* Escena del carro con las tarjetas flotantes. Las tarjetas se anclan al marco del tamaño de la imagen, no a la
            figura: la figura crece con el panel y, en monitores grandes, se irían a las esquinas lejos del carro. */}
        <figure aria-hidden="true" className="relative m-0 hidden min-h-[260px] flex-1 place-items-center md:grid">
          <div className="relative w-[min(560px,100%)] 2xl:w-[min(680px,100%)]">
            {/* eslint-disable-next-line @next/next/no-img-element -- ilustración decorativa estática */}
            <img
              src={rutaIlustracion("heroe-ingreso")}
              alt=""
              width={1100}
              height={821}
              className="di-flotar h-auto w-full"
              style={{ filter: "drop-shadow(var(--di-ingreso-sombra-escena))" }}
            />
            {FLOTANTES.map(({ titulo, detalle, icono, disco, lugar, duracion, retraso }) => (
              <div
                key={titulo}
                className={`di-flotar absolute items-center gap-2.5 rounded-[9px] border border-[color:var(--di-border)] bg-[color:var(--di-ingreso-tarjeta)] px-3.5 py-2.5 text-[0.8125rem] leading-tight text-[color:var(--di-text-primary)] shadow-[var(--di-ingreso-sombra-tarjeta)] ${lugar}`}
                /* Duración y retraso aparte: con «menos movimiento», .di-flotar apaga la animación y estos no la reviven. */
                style={{ animationDuration: duracion, animationDelay: retraso }}
              >
                <span className={`grid h-[30px] w-[30px] flex-none place-items-center rounded-full ${disco}`}>
                  <Icono nombre={icono} size={16} />
                </span>
                <span>
                  <strong className="block">{titulo}</strong>
                  <small className="block text-xs text-[color:var(--di-ingreso-tarjeta-texto-2)]">{detalle}</small>
                </span>
              </div>
            ))}
          </div>
        </figure>
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
