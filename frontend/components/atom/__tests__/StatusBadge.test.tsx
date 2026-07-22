// HU #10844 — StatusBadge por tone semántico: el color se resuelve desde la paleta única
// (variables CSS de globals.css), no con colores crudos por dominio.
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusBadge } from "../StatusBadge";
import { activeTone, enabledTone, procedureTypeTone } from "../statusTones";

describe("StatusBadge — tone semántico", () => {
  it("aplica las variables CSS de la paleta según el tone", () => {
    render(<StatusBadge tone="success" label="Activa" />);
    const badge = screen.getByRole("status", { name: "Estado: Activa" });
    expect(badge.style.background).toContain("--badge-success-bg");
    expect(badge.style.color).toContain("--badge-success-fg");
    expect(badge.style.borderColor).toContain("--badge-success-border");
  });

  it("cada tone referencia su propia variable", () => {
    for (const tone of ["success", "warning", "danger", "info", "neutral"] as const) {
      const { unmount } = render(<StatusBadge tone={tone} label={tone} />);
      const badge = screen.getByRole("status");
      expect(badge.style.background).toBe(`var(--badge-${tone}-bg)`);
      unmount();
    }
  });
});

describe("statusTones — mapeo estado → tone", () => {
  it("activeTone: activo=success, inactivo=danger", () => {
    expect(activeTone(true)).toBe("success");
    expect(activeTone(false)).toBe("danger");
  });
  it("enabledTone: inactivo es neutral (suave)", () => {
    expect(enabledTone(false)).toBe("neutral");
  });
  it("procedureTypeTone mapea publicado=success, borrador=info, archivado=neutral", () => {
    expect(procedureTypeTone("published")).toBe("success");
    expect(procedureTypeTone("draft")).toBe("info");
    expect(procedureTypeTone("archived")).toBe("neutral");
  });
});
