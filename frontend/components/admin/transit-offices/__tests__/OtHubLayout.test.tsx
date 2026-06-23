// HU #10236 — Layout hub con pestañas de navegación.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OtHubLayout } from "../OtHubLayout";

const mockPush = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush }),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn().mockResolvedValue([
    {
      id: "ot-1",
      code: "11001",
      name: "Bogotá",
      departmentCode: "11",
      cityCode: "11001",
    },
  ]),
}));

describe("OtHubLayout — HU #10236", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC2 renderiza pestañas de módulos OT", () => {
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="tramites" moduleTitle="Test OT">
        <p>Contenido módulo</p>
      </OtHubLayout>,
    );
    expect(screen.getByRole("tab", { name: "Trámites" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Webhooks" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Reglas" })).toBeInTheDocument();
    expect(screen.getByText("Contenido módulo")).toBeInTheDocument();
  });

  it("AC2 cambia de módulo al seleccionar pestaña", async () => {
    const user = userEvent.setup();
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="tramites" moduleTitle="Test OT">
        <p>Contenido</p>
      </OtHubLayout>,
    );
    await user.click(screen.getByRole("tab", { name: "Reglas" }));
    expect(mockPush).toHaveBeenCalledWith("/admin/transit-offices/ot-1/rules");
  });

  it("AC5 tablist accesible", () => {
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="documents" moduleTitle="Test OT">
        <p>Docs</p>
      </OtHubLayout>,
    );
    expect(screen.getByRole("tablist", { name: "Módulos de administración OT" })).toBeInTheDocument();
  });
});
