import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppsSessionError } from "@flit/shell/apps";
import { HubHome } from "../HubHome";

// Inicio del hub cuando su sesión quedó de otro usuario o se cerró desde otro producto: no debe decir «tu empresa no
// tiene productos» con el usuario anterior, sino pedir una sesión nueva al hub.

const fetchMyApps = vi.fn();
vi.mock("@flit/shell/apps", async (original) => ({
  ...(await original<typeof import("@flit/shell/apps")>()),
  fetchMyApps: (signal: AbortSignal) => fetchMyApps(signal),
}));
const reauthenticate = vi.fn();
vi.mock("@flit/auth/client", () => ({ reauthenticate: (o: unknown) => reauthenticate(o) }));

const user = { email: "admin@empresa.local", tenantName: "Empresa", permissions: [], roles: [], isSuperAdmin: false };

describe("HubHome", () => {
  beforeEach(() => {
    fetchMyApps.mockReset();
    reauthenticate.mockReset();
  });

  it("con la sesión rechazada (401) pide una nueva en silencio y no muestra «sin productos»", async () => {
    fetchMyApps.mockRejectedValue(new AppsSessionError());
    reauthenticate.mockReturnValue(true);
    render(<HubHome user={user} tramitesUrl="http://t" apps={null} />);

    await waitFor(() => expect(reauthenticate).toHaveBeenCalledWith({ returnTo: "/?inicio=1", silent: true }));
    expect(screen.queryByText(/no tiene productos habilitados/)).toBeNull();
  });

  it("si la API falla, lo dice y ofrece reintentar en vez de decir que la empresa no tiene productos", async () => {
    fetchMyApps.mockRejectedValue(new Error("500"));
    render(<HubHome user={user} tramitesUrl="http://t" apps={null} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("No pudimos cargar tus productos.");
    expect(screen.queryByText(/no tiene productos habilitados/)).toBeNull();
    expect(reauthenticate).not.toHaveBeenCalled();
  });

  it("sin productos de verdad, lo dice", async () => {
    fetchMyApps.mockResolvedValue([{ code: "plataforma", name: "Plataforma", icon: "layout-grid", url: "http://h", current: true }]);
    render(<HubHome user={user} tramitesUrl="http://t" apps={null} />);

    expect(await screen.findByText("Tu empresa todavía no tiene productos habilitados.")).toBeInTheDocument();
  });
});
