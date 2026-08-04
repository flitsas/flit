import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SwitchToggle } from "@/components/ui/SwitchToggle";

/**
 * HU #10497 — Controles: limpieza de botones (sin icono "+" ni iconografía
 * superflua, AC1) y switch de activar/desactivar (SwitchToggle, AC2).
 */

const FE_ROOT = path.resolve(__dirname, "..");
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), "utf8");

/** Todo el árbol de UI, para invariantes que deben valer en la plataforma entera. */
function collectTsx(dir: string): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(dir)) {
    if (entry === "node_modules" || entry === ".next") continue;
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) out.push(...collectTsx(full));
    else if (entry.endsWith(".tsx")) out.push(full);
  }
  return out;
}

const uiFiles = [
  ...collectTsx(path.join(FE_ROOT, "components")),
  ...collectTsx(path.join(FE_ROOT, "app")),
];
const uiContents = uiFiles.map((f) => readFileSync(f, "utf8"));

describe("HU #10497 — SwitchToggle (AC2)", () => {
  it("happy path: es un control role=switch con nombre accesible y refleja checked", () => {
    render(<SwitchToggle checked onChange={vi.fn()} label="Desactivar FLIT SAS" />);
    const sw = screen.getByRole("switch", { name: "Desactivar FLIT SAS" });
    expect(sw).toBeChecked();
    // El tooltip vive en el label contenedor.
    expect(screen.getByTitle("Desactivar FLIT SAS")).toBeInTheDocument();
  });

  it("contrato: al alternar invoca onChange con el nuevo valor", () => {
    const onChange = vi.fn();
    render(<SwitchToggle checked={false} onChange={onChange} label="Activar Acme" />);
    fireEvent.click(screen.getByRole("switch", { name: "Activar Acme" }));
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it("edge: deshabilitado no dispara onChange", async () => {
    const onChange = vi.fn();
    render(<SwitchToggle checked={false} onChange={onChange} label="Activar X" disabled />);
    const sw = screen.getByRole("switch", { name: "Activar X" });
    expect(sw).toBeDisabled();
    // userEvent respeta el estado disabled (a diferencia de fireEvent).
    await userEvent.click(sw);
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("HU #10497 — AC2: Compañías y Documentos usan SwitchToggle para activar/desactivar", () => {
  const tables = [
    "components/admin/companies/CompanyListTable.tsx",
    "components/admin/documents/DocumentTypeListTable.tsx",
  ];
  it("ambas tablas renderizan SwitchToggle (no un botón de texto activar/desactivar)", () => {
    for (const f of tables) {
      const src = read(f);
      expect(src).toMatch(/import \{ SwitchToggle \}/);
      expect(src).toMatch(/<SwitchToggle/);
    }
  });
});

describe("HU #10497 — AC1: botones sin icono '+' (iconografía superflua)", () => {
  // Botones de acción primaria / de agregar que llevaban <Plus/> y se limpiaron.
  const cleaned = [
    "components/atom/modules/Usuarios.tsx",
    "components/atom/DashboardGrid.tsx",
    "components/admin/transit-offices/OtUsersSection.tsx",
    "components/atom/modules/RbacAdmin.tsx",
    "components/superadmin/wizard/Step4Pasos.tsx",
    "components/admin/transit-offices/RuleFormPanel.tsx",
    "components/admin/documents/panels/OrderOverrideForm.tsx",
    "components/admin/documents/tabs/RequirementsTab.tsx",
    // Últimos botones de crear/agregar que conservaban el "+" genérico.
    // HU #11178/#11179 retiraron CompanyDeedsSection y LegalRepresentativeDetailModal; sus botones
    // ("Cargar escritura", "Asociar escritura", "Agregar empresa") viven ahora en el acordeón.
    "components/admin/companies/legal-representatives/RepresentativeCompaniesAccordion.tsx",
    "components/admin/companies/legal-representatives/LegalRepresentativesFormPanel.tsx",
    "components/atom/modules/PrevalidacionesModule.tsx",
  ];

  it("ninguno renderiza ya el icono <Plus /> ni lo importa", () => {
    for (const f of cleaned) {
      const src = read(f);
      expect(src).not.toMatch(/<Plus[\s/>]/);
      expect(src).not.toMatch(/\bPlus\b(?=[^"']*from "lucide-react")/);
    }
  });

  it("los botones conservan su etiqueta de texto (nombre accesible intacto)", () => {
    expect(read("components/atom/modules/Usuarios.tsx")).toMatch(/Invitar usuario/);
    expect(read("components/atom/modules/RbacAdmin.tsx")).toMatch(/Nuevo módulo/);
    expect(read("components/atom/modules/RbacAdmin.tsx")).toMatch(/Nuevo rol/);
    expect(read("components/atom/DashboardGrid.tsx")).toMatch(/Iniciar Nuevo Traspaso/);
    expect(read("components/admin/documents/tabs/RequirementsTab.tsx")).toMatch(/Agregar/);
    expect(read("components/admin/companies/legal-representatives/RepresentativeCompaniesAccordion.tsx"))
      .toMatch(/Asociar escritura/);
    expect(read("components/admin/companies/legal-representatives/RepresentativeCompaniesAccordion.tsx"))
      .toMatch(/Agregar empresa/);
    expect(read("components/atom/modules/PrevalidacionesModule.tsx"))
      .toMatch(/Nueva prevalidación/);
  });

  it("no queda ningún <Plus /> en toda la UI", () => {
    const offenders = uiFiles.filter((_, i) => /<Plus[\s/>]/.test(uiContents[i]));
    expect(offenders).toEqual([]);
  });
});

describe("HU #10844 — AC6: botones de crear con icono semántico (sin el '+' genérico) vía CreateButton", () => {
  // Los 7 usos restantes del icono genérico Plus, migrados a un icono semántico por acción.
  const migrated = [
    "app/admin/documents/page.tsx",
    "app/admin/companies/page.tsx",
    "app/admin/transit-offices/page.tsx",
    "components/admin/transit-offices/TransitOfficesList.tsx",
    "components/atom/modules/_reportes/scheduling/SchedulesSection.tsx",
    "components/atom/modules/_reportes/scheduling/AlertsSection.tsx",
  ];

  it("ninguno usa ya el icono genérico <Plus /> ni lo importa (sí variantes como FilePlus/BellPlus)", () => {
    for (const f of migrated) {
      const src = read(f);
      expect(src).not.toMatch(/<Plus[\s/>]/);
      expect(src).not.toMatch(/\bPlus\b(?=[^"']*from "lucide-react")/);
    }
  });

  it("cada CTA de creación usa un icono semántico por acción", () => {
    expect(read("app/admin/companies/page.tsx")).toMatch(/icon=\{Building2\}/);
    expect(read("app/admin/documents/page.tsx")).toMatch(/icon=\{FilePlus\}/);
    expect(read("app/admin/transit-offices/page.tsx")).toMatch(/icon=\{Landmark\}/);
    expect(read("components/atom/modules/_reportes/scheduling/SchedulesSection.tsx")).toMatch(/icon=\{CalendarPlus\}/);
    expect(read("components/atom/modules/_reportes/scheduling/AlertsSection.tsx")).toMatch(/icon=\{BellPlus\}/);
    expect(read("components/admin/transit-offices/TransitOfficesList.tsx")).toMatch(/icon:\s*Landmark/);
  });

  it("los CTA de creación conservan su etiqueta de texto (nombre accesible intacto)", () => {
    expect(read("app/admin/companies/page.tsx")).toMatch(/Crear compañía/);
    expect(read("app/admin/documents/page.tsx")).toMatch(/Crear documento/);
    expect(read("app/admin/transit-offices/page.tsx")).toMatch(/Activa un organismo desde la fila/);
    expect(read("components/atom/modules/_reportes/scheduling/SchedulesSection.tsx")).toMatch(/Nuevo informe/);
    expect(read("components/atom/modules/_reportes/scheduling/AlertsSection.tsx")).toMatch(/Nueva alerta/);
  });
});
