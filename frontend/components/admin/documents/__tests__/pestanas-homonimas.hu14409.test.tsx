// HU #14409 AC5 — retirar la pestaña Documentos de la ficha de compañía no toca a sus homónimas: la de
// la consola documental por trámite y la del hub del organismo (la de Plataforma, Tipos de trámite, se
// prueba en TiposTramitePanel.test.tsx, donde están sus dobles).
//
// Uso de ejemplo:
//   pnpm exec vitest run components/admin/documents/__tests__/pestanas-homonimas.hu14409.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { OT_HUB_TABS } from "@/components/admin/transit-offices/ot-nav";
import { DocumentProcedureTabs } from "../DocumentProcedureTabs";

vi.mock("@/components/admin/documents/tabs/RequirementsTab", () => ({ RequirementsTab: () => <div>panel-requisitos</div> }));
vi.mock("@/components/admin/documents/tabs/OtOverridesTab", () => ({ OtOverridesTab: () => <div>panel-overrides</div> }));
vi.mock("@/components/admin/documents/tabs/MatrixPreviewTab", () => ({ MatrixPreviewTab: () => <div>panel-matriz</div> }));

describe("HU #14409 AC5 — pestañas homónimas de Documentos", () => {
  it("el hub del organismo conserva Documentos, entre Reglas y Requisitos", () => {
    const etiquetas = OT_HUB_TABS.map((t) => t.label);
    const i = etiquetas.indexOf("Documentos");

    expect(OT_HUB_TABS[i]).toMatchObject({ id: "documents", segment: "documents" });
    expect(etiquetas.slice(i - 1, i + 2)).toEqual(["Reglas", "Documentos", "Requisitos"]);
  });

  it("la consola documental por trámite sigue abriendo en Documentos", () => {
    render(<DocumentProcedureTabs procedureTypeId="tipo-1" />);

    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Documentos",
      "Overrides OT",
      "Matriz resuelta",
    ]);
    expect(screen.getByRole("tab", { name: "Documentos" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("panel-requisitos")).toBeInTheDocument();
  });
});
