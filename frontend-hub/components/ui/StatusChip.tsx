import type { CSSProperties } from "react";
import { rutaKit } from "@flit/ui/iconos-mapas";

// Estado de un producto en una línea: disponible hoy o próximamente. Con la etiqueta del Manual UI Kit 2026 (la forma
// `.kit-etiqueta` de StatusBadge, con su disco de aviso antes del texto, para no decirlo solo con color). Solo cambia
// la clase: el elemento, el texto y la accesibilidad son los de siempre.
const tono = (t: "success" | "info") =>
  ({
    "--c": `var(--badge-${t}-border)`,
    "--ct": `var(--badge-${t}-fg)`,
    background: `var(--badge-${t}-bg)`,
    color: `var(--badge-${t}-fg)`,
    borderColor: `var(--badge-${t}-border)`,
  }) as CSSProperties;
const CHIP = "kit-etiqueta kit-etiqueta--icono whitespace-nowrap";

export function StatusChip({ status }: { status: "available" | "soon" }) {
  return status === "available" ? (
    <span className={CHIP} style={tono("success")}>
      {/* eslint-disable-next-line @next/next/no-img-element -- disco estático del kit */}
      <img src={rutaKit("aviso-ok")} alt="" aria-hidden="true" width={20} height={20} className="kit-etiqueta__icono" />
      Disponible
    </span>
  ) : (
    <span className={CHIP} style={tono("info")}>
      {/* eslint-disable-next-line @next/next/no-img-element -- disco estático del kit */}
      <img src={rutaKit("aviso-info")} alt="" aria-hidden="true" width={20} height={20} className="kit-etiqueta__icono" />
      Próximamente
    </span>
  );
}
