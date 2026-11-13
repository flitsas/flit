import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CabeceraLote } from "@/components/tablero/CabeceraLote";
import { grilla } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <CabeceraLote grilla={grilla} volver="/" puedeCerrar confirmando={abierto} … /> en el tablero.
// HU #14562 · m-13 (auditoría de UI, spec §1.5): en el modal «¿Cerrar el lote?», «Cancelar» va en la variante alerta.

vi.mock("next/link", () => ({ default: ({ href, children, ...r }: { href: string; children: React.ReactNode }) => <a href={href} {...r}>{children}</a> }));

describe("CabeceraLote — HU #14562 m-13", () => {
  it("«Cancelar» del modal de cierre usa la variante alerta del kit", () => {
    render(
      <CabeceraLote
        grilla={grilla({ estado: "EN_PROCESO" })}
        volver="/"
        puedeCerrar
        cerrando={false}
        confirmando
        onPedirCierre={vi.fn()}
        onCancelarCierre={vi.fn()}
        onConfirmarCierre={vi.fn()}
      />,
    );
    const cancelar = within(screen.getByRole("dialog", { name: "¿Cerrar el lote?" })).getByRole("button", { name: "Cancelar" });
    const clases = cancelar.className.split(/\s+/);
    expect(clases).toContain("bg-[color:var(--kit-naranja-boton)]");
    expect(clases).not.toContain("bg-[color:var(--kit-tinta)]");
  });
});
