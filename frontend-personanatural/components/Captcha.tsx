"use client";

import { useEffect, useRef, useState } from "react";
import { useConfigCaptcha } from "@/components/ConfigPublica";
import type { ProveedorCaptcha } from "@/lib/captcha";

// Verificación de seguridad (captcha) de los endpoints públicos del BFF: el token sale por `onToken` y el formulario lo
// manda en la cabecera X-Captcha-Token. El proveedor y la clave pública del sitio los
// resuelve el SERVIDOR al atender cada petición (CAPTCHA_PROVIDER = turnstile | recaptcha | dev, CAPTCHA_SITE_KEY) y llegan
// por el contexto de `ConfigPublica` (layout): nada se incrusta al compilar (una imagen para todos los ambientes).
// Sin variable, en desarrollo se usa el modo `dev` (sin casilla visible: entrega sola un token fijo, solo válido contra un BFF con
// el captcha deshabilitado); en producción sin proveedor configurado no se entrega ningún token y el formulario no envía.
// Los tokens son de un solo uso: tras cada envío el formulario sube `reinicio` para pedir uno nuevo.

export type { ProveedorCaptcha };

export const TOKEN_MODO_DESARROLLO = "dev-captcha-token";

const SCRIPTS: Record<"turnstile" | "recaptcha", string> = {
  turnstile: "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit",
  recaptcha: "https://www.google.com/recaptcha/api.js?render=explicit",
};

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

export function Captcha({ onToken, reinicio = 0, proveedor: proveedorProp, siteKey: siteKeyProp }: Props) {
  const config = useConfigCaptcha();
  const proveedor = proveedorProp ?? config.proveedor;
  const siteKey = siteKeyProp ?? config.siteKey;
  const contenedor = useRef<HTMLDivElement>(null);
  const widget = useRef<string | number | null>(null);
  const aviso = useRef(onToken);
  const [fallo, setFallo] = useState(false);

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

  // Modo de desarrollo: sin casilla visible; entrega el token fijo al montar y tras cada reinicio.
  useEffect(() => {
    if (proveedor === "dev") aviso.current(TOKEN_MODO_DESARROLLO);
  }, [proveedor, reinicio]);

  if (sinConfigurar || fallo) {
    return (
      <p role="alert" className="text-center text-sm text-alert">
        {fallo ? "No pudimos cargar la verificación de seguridad. Recarga la página e inténtalo de nuevo." : "La verificación de seguridad no está disponible por ahora. Intenta más tarde."}
      </p>
    );
  }

  // Modo de desarrollo: no se dibuja nada; el token fijo se entrega solo (ver el efecto de arriba).
  if (proveedor === "dev") return null;

  return <div ref={contenedor} className="flex min-h-[65px] justify-center" aria-label="Verificación de seguridad" />;
}
