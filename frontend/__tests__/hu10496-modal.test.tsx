import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Building2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";

/**
 * HU #10496 — Componente Modal unificado: ancho estándar (AC1), blur de backdrop
 * consistente (AC1), contraste legible en dark (AC2) y tratamiento desenfocado de
 * la página de cambiar contraseña (AC3).
 */

const FE_ROOT = path.resolve(__dirname, "..");
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), "utf8");

describe("HU #10496 — Modal (AC1 ancho + blur, AC2 contraste dark)", () => {
  it("happy path: renderiza role=dialog, título asociado, cuerpo y botón cerrar", () => {
    const onClose = vi.fn();
    render(
      <Modal open onClose={onClose} icon={Building2} title="Crear compañía">
        <p>Cuerpo del formulario</p>
      </Modal>,
    );
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    // El título está asociado vía aria-labelledby.
    const labelledBy = dialog.getAttribute("aria-labelledby");
    expect(labelledBy).toBeTruthy();
    expect(document.getElementById(labelledBy!)?.textContent).toBe("Crear compañía");
    expect(screen.getByText("Cuerpo del formulario")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("AC1: el backdrop aplica blur consistente y el panel usa ancho estándar (no angosto) por defecto", () => {
    render(
      <Modal open onClose={vi.fn()} title="Estándar">
        <p>x</p>
      </Modal>,
    );
    const dialog = screen.getByRole("dialog");
    expect(dialog.className).toContain("backdrop-blur-md");
    expect(dialog.className).toMatch(/fixed inset-0/);
    const panel = dialog.firstElementChild as HTMLElement;
    // md por defecto → max-w-lg (estándar, no el max-w-md angosto anterior).
    expect(panel.className).toContain("max-w-lg");
  });

  it("AC1: size='sm' usa el ancho compacto y size='lg' el amplio", () => {
    const { rerender } = render(
      <Modal open onClose={vi.fn()} size="sm" title="sm">
        <p>x</p>
      </Modal>,
    );
    expect((screen.getByRole("dialog").firstElementChild as HTMLElement).className).toContain(
      "max-w-md",
    );
    rerender(
      <Modal open onClose={vi.fn()} size="lg" title="lg">
        <p>x</p>
      </Modal>,
    );
    expect((screen.getByRole("dialog").firstElementChild as HTMLElement).className).toContain(
      "max-w-2xl",
    );
  });

  it("AC2: el panel fija color de texto y fondo legibles en dark", () => {
    render(
      <Modal open onClose={vi.fn()} title="Contraste">
        <p>x</p>
      </Modal>,
    );
    const panel = screen.getByRole("dialog").firstElementChild as HTMLElement;
    expect(panel.className).toContain("dark:text-white");
    expect(panel.className).toContain("dark:bg-[#0B0F14]");
  });

  it("edge: cuando busy=true, ni el botón cerrar ni Escape disparan onClose", () => {
    const onClose = vi.fn();
    render(
      <Modal open onClose={onClose} busy title="Ocupado">
        <p>x</p>
      </Modal>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).not.toHaveBeenCalled();
  });

  it("contrato: Escape cierra cuando no está ocupado y open=false no renderiza nada", () => {
    const onClose = vi.fn();
    const { rerender } = render(
      <Modal open onClose={onClose} title="Cerrable">
        <p>x</p>
      </Modal>,
    );
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
    rerender(
      <Modal open={false} onClose={onClose} title="Cerrable">
        <p>x</p>
      </Modal>,
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});

describe("HU #10496 — migración de modales al componente Modal", () => {
  const migrated = [
    "components/admin/companies/CreateCompanyDialog.tsx",
    "components/admin/companies/EditCompanyDialog.tsx",
    "components/admin/companies/CompanyStatusDialog.tsx",
    "components/admin/companies/SaveConfigDialog.tsx",
    "components/admin/documents/CreateDocumentTypeDialog.tsx",
    "components/admin/documents/DocumentInUseDialog.tsx",
    "components/admin/transit-offices/CreateTransitOfficeTenantDialog.tsx",
    "components/atom/modules/RbacAdmin.tsx",
  ];

  it("los diálogos migrados usan el componente Modal", () => {
    for (const f of migrated) {
      const src = read(f);
      expect(src).toMatch(/import \{ Modal \}|import \{[^}]*\bModal\b[^}]*\}/);
      expect(src).toMatch(/<Modal[\s>]/);
    }
  });

  it("los diálogos migrados ya no repiten el overlay boilerplate (fixed inset-0 + backdrop-blur)", () => {
    for (const f of migrated) {
      const src = read(f);
      expect(src).not.toMatch(/fixed inset-0[^"]*backdrop-blur/);
    }
  });
});

describe("HU #10496 — AC3: página cambiar contraseña con fondo desenfocado", () => {
  it("AuthCard expone la variante 'overlay' con app-bg + backdrop-blur y panel dark-aware", () => {
    const src = read("components/auth/AuthCard.tsx");
    expect(src).toMatch(/variant\?:\s*"auth"\s*\|\s*"overlay"/);
    expect(src).toMatch(/app-bg/);
    expect(src).toMatch(/backdrop-blur-md/);
    expect(src).toMatch(/dark:bg-\[#0B0F14\]/);
  });

  it("la página de cambiar contraseña usa variant='overlay'", () => {
    const src = read("app/profile/change-password/page.tsx");
    expect(src).toMatch(/variant="overlay"/);
  });
});
