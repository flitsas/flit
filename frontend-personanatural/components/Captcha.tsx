"use client";

import { useEffect, useId, useRef, useState } from "react";

// Verificación de seguridad (captcha) de los endpoints públicos del BFF: el token sale por `onToken` y el formulario lo
// manda en la cabecera X-Captcha-Token. El proveedor se elige por variable de entorno (sin inventar claves):
//   NEXT_PUBLIC_CAPTCHA_PROVIDER = turnstile | recaptcha | dev
//   NEXT_PUBLIC_CAPTCHA_SITE_KEY = clave pública del sitio (turnstile y recaptcha)
// Sin variable, en desarrollo se usa el modo `dev` (una casilla que entrega un token fijo, solo válido contra un BFF con
// el captcha deshabilitado); en producción sin proveedor configurado no se entrega ningún token y el formulario no envía.
// Los tokens son de un solo uso: tras cada envío el formulario sube `reinicio` para pedir uno nuevo.

export type ProveedorCaptcha = "turnstile" | "recaptcha" | "dev" | "ninguno";

export const TOKEN_MODO_DESARROLLO = "dev-captcha-token";

const SCRIPTS: Record<"turnstile" | "recaptcha", string> = {
  turnstile: "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit",
  recaptcha: "https://www.google.com/recaptcha/api.js?render=explicit",
};

export function proveedorConfigurado(): ProveedorCaptcha {
  const valor = process.env.NEXT_PUBLIC_CAPTCHA_PROVIDER;
  if (valor === "turnstile" || valor === "recaptcha" || valor === "dev") return valor;
  return process.env.NODE_ENV === "production" ? "ninguno" : "dev";
}

interface WidgetApi {
  render: (el: HTMLElement, opciones: Record<string, unknown>) => string | number;
  reset: (id?: string | number) => void;
  remove?: (id?: string | number) => void;
  ready?: (cb: () => void) => void;
}

function apiDe(proveedor: "turnstile" | "recaptcha"): WidgetApi | undefined {
  const w = window as unknown as { turnstile?: WidgetApi; grecaptcha?: WidgetApi };
  return proveedor === "turnstile" ? w.turnstile : w.grecaptcha;
}

const cargas = new Map<string, Promise<void>>();

function cargarScript(src: string): Promise<void> {
  const previa = cargas.get(src);
  if (previa) return previa;
  const promesa = new Promise<void>((resolver, rechazar) => {
    const script = document.createElement("script");
    script.src = src;
    script.async = true;
    script.defer = true;
    script.onload = () => resolver();
    script.onerror = () => {
      cargas.delete(src);
      rechazar(new Error("captcha_no_carga"));
    };
    document.head.appendChild(script);
  });
  cargas.set(src, promesa);
  return promesa;
}

interface Props {
  onToken: (token: string | null) => void;
  /** Súbelo para descartar el token usado y pedir uno nuevo. */
  reinicio?: number;
  proveedor?: ProveedorCaptcha;
  siteKey?: string;
}

export function Captcha({ onToken, reinicio = 0, proveedor = proveedorConfigurado(), siteKey = process.env.NEXT_PUBLIC_CAPTCHA_SITE_KEY }: Props) {
  const idAyuda = useId();
  const contenedor = useRef<HTMLDivElement>(null);
  const widget = useRef<string | number | null>(null);
  const aviso = useRef(onToken);
  const [fallo, setFallo] = useState(false);
  // La casilla del modo de desarrollo vale solo para el `reinicio` en el que se marcó: al subirlo vuelve a quedar sin marcar.
  const [marcadoEn, setMarcadoEn] = useState<number | null>(null);
  const marcado = marcadoEn === reinicio;

  useEffect(() => {
    aviso.current = onToken;
  }, [onToken]);

  const externo = proveedor === "turnstile" || proveedor === "recaptcha";
  const sinConfigurar = proveedor === "ninguno" || (externo && !siteKey);

  // Widget externo: se carga el script del proveedor y se dibuja una vez.
  useEffect(() => {
    if (!externo || !siteKey) return;
    let vivo = true;
    cargarScript(SCRIPTS[proveedor])
      .then(() => {
        const api = apiDe(proveedor);
        const dibujar = () => {
          if (!vivo || !contenedor.current || !api || widget.current !== null) return;
          widget.current = api.render(contenedor.current, {
            sitekey: siteKey,
            callback: (token: string) => aviso.current(token),
            "expired-callback": () => aviso.current(null),
            "error-callback": () => {
              aviso.current(null);
              setFallo(true);
            },
          });
        };
        if (api?.ready) api.ready(dibujar);
        else dibujar();
      })
      .catch(() => {
        if (vivo) setFallo(true);
      });
    return () => {
      vivo = false;
      if (widget.current !== null) apiDe(proveedor)?.remove?.(widget.current);
      widget.current = null;
    };
  }, [externo, proveedor, siteKey]);

  // Reinicio tras un envío: el token usado ya no sirve.
  useEffect(() => {
    if (reinicio === 0) return;
    if (externo && widget.current !== null) apiDe(proveedor)?.reset(widget.current);
    aviso.current(null);
  }, [reinicio, externo, proveedor]);

  if (sinConfigurar || fallo) {
    return (
      <p role="alert" className="text-center text-sm text-alert">
        {fallo ? "No pudimos cargar la verificación de seguridad. Recarga la página e inténtalo de nuevo." : "La verificación de seguridad no está disponible por ahora. Intenta más tarde."}
      </p>
    );
  }

  if (proveedor === "dev") {
    return (
      <div className="flex flex-col items-center gap-1">
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="h-5 w-5 accent-primary"
            checked={marcado}
            aria-describedby={idAyuda}
            onChange={(e) => {
              setMarcadoEn(e.target.checked ? reinicio : null);
              onToken(e.target.checked ? TOKEN_MODO_DESARROLLO : null);
            }}
          />
          Verificación de seguridad
        </label>
        <p id={idAyuda} className="text-xs text-foreground/70">Modo de desarrollo: sin proveedor de captcha configurado.</p>
      </div>
    );
  }

  return <div ref={contenedor} className="flex min-h-[65px] justify-center" aria-label="Verificación de seguridad" />;
}
