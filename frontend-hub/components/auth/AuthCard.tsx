"use client";

import Link from "next/link";
import { BrandLogo } from "@flit/brand/BrandLogo";
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

// Contenedor de las pantallas de recuperación del hub (mismo que el de Trámites, components/auth/AuthCard.tsx).
// Homologación (HU #14511): va sobre el armazón del Ingreso, con los mismos controles del login.
export function AuthCard({ title, subtitle, children, backHref = "/login" }: { title: string; subtitle?: string; children: React.ReactNode; backHref?: string | null }) {
  return (
    <main>
      <IngresoLayout logo={<BrandLogo variant="white" alt="" />} logoTarjeta={<BrandLogo variant="auto" />}>
        <h1 className={INGRESO_TITULO_CLS}>{title}</h1>
        {subtitle && <p className="-mt-2 mb-0 text-sm text-[color:var(--di-text-secondary)]">{subtitle}</p>}
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

export const LABEL_CLASS = INGRESO_ETIQUETA_CLS;
export const INPUT_CLASS = INGRESO_CONTROL_CLS;
export const HINT_CLASS = "mt-1.5 text-xs text-[color:var(--di-text-tertiary)]";
export const ERROR_CLASS = INGRESO_ERROR_CLS;
export const STATUS_CLASS = "text-sm text-[color:var(--di-text-primary)]";
export const SUBMIT_CLASS = buttonClass({ className: INGRESO_BOTON_CLS });
export const LINK_BUTTON_CLASS = buttonClass({ className: "w-full" });
