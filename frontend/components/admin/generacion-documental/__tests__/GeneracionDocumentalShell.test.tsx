// HU-01 (Feature #12201) — CF-01 (tres pestañas que cargan su panel sin recargar) y R12
// (el layout niega el acceso a quien no tiene el módulo entre sus módulos accesibles).
// Uso de ejemplo: render(<AdminGeneracionDocumentalLayout>…</AdminGeneracionDocumentalLayout>)
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  push: vi.fn(),
  useAccessibleModules: vi.fn(),
  useAuthGate: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mocks.push }),
  usePathname: () => "/admin/generacion-documental",
}));
vi.mock("@/hooks/useAccessibleModules", () => ({ useAccessibleModules: mocks.useAccessibleModules }));
vi.mock("@/hooks/useAuthGate", () => ({ useAuthGate: mocks.useAuthGate }));
vi.mock("@/components/atom/Shell", () => ({
  Shell: ({ children }: { children: React.ReactNode }) => <div data-testid="shell">{children}</div>,
}));

import AdminGeneracionDocumentalLayout from "@/app/admin/generacion-documental/layout";
import { GeneracionDocumentalTabs } from "../GeneracionDocumentalTabs";
import { GENERACION_DOCUMENTAL_TABS } from "../generacion-documental-nav";

function moduleState(codes: string[], overrides: Record<string, unknown> = {}) {
  return {
    modules: codes.map((code, i) => ({ id: String(i), code, name: code, sortOrder: i, actions: [] })),
    loading: false,
    error: null,
    ready: true,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.useAuthGate.mockReturnValue({ authed: true, hydrated: true, logout: vi.fn() });
});

describe("GeneracionDocumentalTabs (CF-01)", () => {
  it("expone las cuatro pestañas del módulo con semántica de tablist", () => {
    render(<GeneracionDocumentalTabs activeId="rues" />);
    const tabs = screen.getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual([
      "Certificado RUES",
      "Transferencia",
      "Carga masiva",
      "Historial",
    ]);
    // Cuatro desde HU #12224: la carga masiva es una forma de generar, no una vista de
    // detalle, y sin pestaña propia el XLSX no tenía por dónde entrar a la aplicación.
    expect(GENERACION_DOCUMENTAL_TABS).toHaveLength(4);
  });

  it("marca la pestaña activa con aria-selected", () => {
    render(<GeneracionDocumentalTabs activeId="historial" />);
    expect(screen.getByRole("tab", { name: "Historial" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Transferencia" })).toHaveAttribute("aria-selected", "false");
  });

  it("navega con el router del App Router (sin recarga de página)", async () => {
    render(<GeneracionDocumentalTabs activeId="rues" />);
    await userEvent.click(screen.getByRole("tab", { name: "Historial" }));
    expect(mocks.push).toHaveBeenCalledWith("/admin/generacion-documental/historial");
  });

  it("las pestañas son alcanzables por teclado", async () => {
    render(<GeneracionDocumentalTabs activeId="rues" />);
    await userEvent.tab();
    expect(screen.getByRole("tab", { name: "Certificado RUES" })).toHaveFocus();
  });
});

describe("Layout del módulo — gate por módulos accesibles (R12)", () => {
  it("con el módulo accesible renderiza el contenido de la pestaña", () => {
    mocks.useAccessibleModules.mockReturnValue(moduleState(["tramites", "generacion-documental"]));
    render(
      <AdminGeneracionDocumentalLayout>
        <p>contenido del módulo</p>
      </AdminGeneracionDocumentalLayout>,
    );
    expect(screen.getByText("contenido del módulo")).toBeInTheDocument();
  });

  it("sin el módulo muestra acceso restringido y NO monta la vista del módulo", () => {
    mocks.useAccessibleModules.mockReturnValue(moduleState(["tramites"]));
    render(
      <AdminGeneracionDocumentalLayout>
        <p>contenido del módulo</p>
      </AdminGeneracionDocumentalLayout>,
    );
    expect(screen.queryByText("contenido del módulo")).not.toBeInTheDocument();
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Acceso restringido");
  });

  it("mientras los módulos no han resuelto no decide: ni contenido ni denegación", () => {
    mocks.useAccessibleModules.mockReturnValue(moduleState([], { loading: true, ready: false }));
    render(
      <AdminGeneracionDocumentalLayout>
        <p>contenido del módulo</p>
      </AdminGeneracionDocumentalLayout>,
    );
    expect(screen.queryByText("contenido del módulo")).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveAttribute("aria-busy", "true");
  });

  it("sin sesión hidratada no renderiza nada", () => {
    mocks.useAuthGate.mockReturnValue({ authed: false, hydrated: false, logout: vi.fn() });
    mocks.useAccessibleModules.mockReturnValue(moduleState([]));
    const { container } = render(
      <AdminGeneracionDocumentalLayout>
        <p>contenido del módulo</p>
      </AdminGeneracionDocumentalLayout>,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
