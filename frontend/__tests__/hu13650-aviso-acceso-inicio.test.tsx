import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";

/**
 * HU #13650 (Feature #13102, ADR-0072) — el acceso por URL a un módulo sin permiso vuelve al inicio con el aviso
 * «No tienes acceso a este módulo.»; con el permiso se ve el módulo sin aviso.
 */

const h = vi.hoisted(() => ({
  search: "m=reportes",
  replace: vi.fn(),
  codes: ["tramites"] as string[],
  token: "" as string,
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: h.replace, push: vi.fn() }),
  useSearchParams: () => new URLSearchParams(h.search),
  usePathname: () => "/",
}));
vi.mock("@/hooks/useAuthGate", () => ({ useAuthGate: () => ({ authed: true, hydrated: true, logout: vi.fn() }) }));
vi.mock("@/hooks/useAccessibleModules", () => ({
  useAccessibleModules: () => ({ modules: h.codes.map((code) => ({ code })), loading: false, ready: true, error: null }),
}));
vi.mock("@/lib/api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/client")>()),
  getToken: () => h.token,
}));
vi.mock("@/lib/telemetry", () => ({ trackModuleView: vi.fn() }));
vi.mock("@/components/atom/Shell", () => ({
  Shell: ({ children }: { children: React.ReactNode }) => <div data-testid="shell">{children}</div>,
}));
vi.mock("@/components/atom/modules/Reportes", () => ({ Reportes: () => <div data-testid="modulo-Reportes" /> }));
vi.mock("@/components/atom/modules/Dashboard", () => ({ Dashboard: () => <div data-testid="modulo-Dashboard" /> }));
vi.mock("@/components/atom/modules/OtDashboard", () => ({ OtDashboard: () => <div data-testid="modulo-OtDashboard" /> }));
vi.mock("@/components/atom/modules/Tramites", () => ({ Tramites: () => <div data-testid="modulo-Tramites" /> }));
vi.mock("@/components/atom/modules/Validaciones", () => ({ Validaciones: () => <div data-testid="modulo-Validaciones" /> }));
vi.mock("@/components/atom/modules/HistorialPlaca", () => ({ HistorialPlaca: () => <div data-testid="modulo-HistorialPlaca" /> }));
vi.mock("@/components/atom/modules/Usuarios", () => ({ Usuarios: () => <div data-testid="modulo-Usuarios" /> }));
vi.mock("@/components/atom/modules/Ayuda", () => ({ Ayuda: () => <div data-testid="modulo-Ayuda" /> }));
vi.mock("@/components/atom/modules/RbacAdmin", () => ({ RbacAdmin: () => <div data-testid="modulo-RbacAdmin" /> }));
vi.mock("@/components/atom/modules/Auditoria", () => ({ Auditoria: () => <div data-testid="modulo-Auditoria" /> }));
vi.mock("@/components/atom/modules/LogQx", () => ({ LogQx: () => <div data-testid="modulo-LogQx" /> }));
vi.mock("@/components/atom/modules/IctLogs", () => ({ IctLogs: () => <div data-testid="modulo-IctLogs" /> }));
vi.mock("@/components/atom/modules/IctReports", () => ({ IctReports: () => <div data-testid="modulo-IctReports" /> }));
vi.mock("@/components/atom/modules/IctTrazabilidad", () => ({ IctTrazabilidad: () => <div data-testid="modulo-IctTrazabilidad" /> }));

import HomePage from "@/app/page";

function jwt(payload: Record<string, unknown>): string {
  const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString("base64url");
  return `${b64({ alg: "none" })}.${b64({ sub: "u1", roles: [], permissions: [], ...payload })}.sig`;
}

beforeEach(() => {
  vi.clearAllMocks();
  h.token = jwt({});
  h.codes = ["tramites"];
});

describe("HU #13650 — aviso de acceso denegado en el inicio", () => {
  it("AC1: sin el permiso de Reportes, /?m=reportes vuelve al inicio y muestra el aviso", async () => {
    h.search = "m=reportes";
    render(<HomePage />);

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No tienes acceso a este módulo.");
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/?m=dashboard", { scroll: false }));
    expect(screen.queryByTestId("modulo-Reportes")).not.toBeInTheDocument();
  });

  it("AC2 (destino del middleware): la marca ?acceso=denegado muestra el mismo aviso y se limpia de la URL", async () => {
    h.search = "acceso=denegado&modulo=admin-companies";
    render(<HomePage />);

    expect(await screen.findByRole("alert")).toHaveTextContent("No tienes acceso a este módulo.");
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/", { scroll: false }));
  });

  it("AC3: con el permiso ve el módulo, sin aviso", async () => {
    h.codes = ["tramites", "reportes"];
    h.search = "m=reportes";
    render(<HomePage />);

    expect(await screen.findByTestId("modulo-Reportes")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(h.replace).not.toHaveBeenCalled();
  });

  it("el aviso se puede cerrar con el botón (accesible por teclado)", async () => {
    h.search = "m=reportes";
    render(<HomePage />);

    const cerrar = await screen.findByRole("button", { name: "Cerrar aviso de acceso denegado" });
    cerrar.click();
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });
});
