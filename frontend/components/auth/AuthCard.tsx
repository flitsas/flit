"use client";

// Contenedor visual común de las pantallas de autenticación.
import Link from "next/link";
import { BrandLogo } from "@/components/brand/BrandLogo";
import { buttonClass } from "@flit/ui/Button";
import {
  IngresoLayout,
  INGRESO_BOTON_CLS,
  INGRESO_CONTROL_CLS,
  INGRESO_ENLACE_CLS,
  INGRESO_ERROR_CLS,
  INGRESO_ETIQUETA_CLS,
  INGRESO_TITULO_CLS,
} from "@flit/ui/Ingreso";

// Homologación (HU #14511): los formularios de cuenta usan los mismos controles que el Ingreso (@flit/ui/Ingreso).
/** Etiqueta de campo. */
export const AUTH_LABEL_CLASS = INGRESO_ETIQUETA_CLS;
/** Control con ícono a la izquierda (va dentro de un `relative` junto a `INGRESO_ICONO_CLS`). */
export const AUTH_INPUT_CLASS = INGRESO_CONTROL_CLS;
/** Ayuda bajo el campo (política de contraseña). */
export const AUTH_HINT_CLASS = "mt-1.5 text-xs text-[color:var(--di-text-tertiary)]";
/** Mensaje de error del formulario. */
export const AUTH_ERROR_CLASS = INGRESO_ERROR_CLS;
/** Confirmación (correo enviado, contraseña actualizada…). */
export const AUTH_STATUS_CLASS = "text-sm text-[color:var(--di-text-primary)]";
/** Nota secundaria de la confirmación. */
export const AUTH_NOTE_CLASS = "text-xs text-[color:var(--di-text-secondary)]";
/** Botón de enviar. */
export const AUTH_SUBMIT_CLASS = buttonClass({ className: INGRESO_BOTON_CLS });
/** Enlace con aspecto de botón (p. ej. «Ir a iniciar sesión»). */
export const AUTH_LINK_BUTTON_CLASS = buttonClass({ className: "w-full" });

const SUBTITULO_CLS = "-mt-2 mb-0 text-sm text-[color:var(--di-text-secondary)]";

export function AuthCard({
  title,
  subtitle,
  children,
  backHref = "/login",
  variant = "auth",
  marca,
}: {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
  /** Destino del enlace "volver". `null` lo oculta (p. ej. en pantallas internas). */
  backHref?: string | null;
  /**
   * `auth` (por defecto): armazón del Ingreso (panel de marca + tarjeta), HU #14511.
   * `overlay` (HU #10496, AC3): mismo tratamiento desenfocado y consistente de las
   * modales — fondo de la app tenue con `backdrop-blur` y tarjeta con panel dark-aware,
   * para páginas internas como "Cambiar contraseña". Lleva la franja tinta del Modal.
   */
  variant?: "auth" | "overlay";
  /** Textos propios del panel de tinta (solo `auth`); sin ella salen los del ingreso. */
  marca?: React.ReactNode;
}) {
  if (variant === "overlay") {
    return (
      <main className="app-bg min-h-screen">
        <div className="flex min-h-screen items-center justify-center bg-[var(--overlay)] px-4 backdrop-blur-md">
          <div className="w-full max-w-md overflow-clip rounded-[var(--di-radius-modal)] bg-card text-flit-primary shadow-[var(--shadow-modal)] dark:text-foreground">
            {/* Franja tinta del Modal (`.modal__franja` de la propuesta). */}
            <div className="relative overflow-clip bg-[color:var(--di-accent)] px-5 py-4 text-[color:var(--di-on-accent)] sm:px-6 sm:py-5">
              <span
                aria-hidden="true"
                className="pointer-events-none absolute -right-[30px] -top-[60px] h-[120px] w-[120px] rounded-full bg-[color:var(--di-highlight-decor)]"
              />
              <h1 className="relative z-[1] m-0 text-lg font-bold leading-tight text-[color:var(--di-on-accent)]">{title}</h1>
              {subtitle && <p className="relative z-[1] mt-0.5 text-xs opacity-85">{subtitle}</p>}
            </div>
            <div className="p-5 sm:p-6">
              {children}
              {backHref && (
                <Link href={backHref} className={`mt-4 ${INGRESO_ENLACE_CLS}`}>
                  ← Volver
                </Link>
              )}
            </div>
          </div>
        </div>
      </main>
    );
  }

  return (
    <main>
      {/* HU #12419 AC2 — logo de la red (o FLIT sin cambios, AC7) en acceso/recuperación. */}
      <IngresoLayout logo={<BrandLogo variant="white" alt="" />} logoTarjeta={<BrandLogo variant="auto" />} marca={marca}>
        <h1 className={INGRESO_TITULO_CLS}>{title}</h1>
        {subtitle && <p className={SUBTITULO_CLS}>{subtitle}</p>}
        <div>{children}</div>
        {backHref && (
          <Link href={backHref} className={INGRESO_ENLACE_CLS}>
            ← Volver
          </Link>
        )}
      </IngresoLayout>
    </main>
  );
}
