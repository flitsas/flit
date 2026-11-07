// HU #14386 AC3 y AC4 — el enlace y el QR usan el dominio principal activo y, sin él, la dirección del punto.
// Uso de ejemplo: <EnlaceQrDialog codigo="OT01" nombre="Organismo" transitOfficeId="id" onClose={cerrar} />
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { getPortalDominios, type DominioPortal } from "@/lib/api/admin-ot-portal-dominio";
import { EnlaceQrDialog } from "../EnlaceQrDialog";

vi.mock("@/lib/api/admin-ot-portal-dominio", async (original) => ({
  ...(await original<typeof import("@/lib/api/admin-ot-portal-dominio")>()),
  getPortalDominios: vi.fn(),
}));

const dominio = (host: string, estado: string, principal = false) =>
  ({ id: host, host, estado, principal, motivo: null, dns: [], rowVersion: 1 }) as unknown as DominioPortal;

const consulta = vi.mocked(getPortalDominios);
const campo = () => screen.getByLabelText("Dirección del canal") as HTMLInputElement;

function abrir() {
  render(<EnlaceQrDialog codigo="OT01" nombre="Organismo" transitOfficeId="org-1" onClose={vi.fn()} />);
}

describe("EnlaceQrDialog (#14386)", () => {
  beforeEach(() => {
    consulta.mockReset();
    vi.stubEnv("NEXT_PUBLIC_CANAL_OT_ORIGEN", "https://portal.example.test");
  });

  it("AC3: con dominios activos usa el principal y lista los demás", async () => {
    consulta.mockResolvedValue([
      dominio("otro.organismo.test", "activo"),
      dominio("principal.organismo.test", "activo", true),
      dominio("pendiente.organismo.test", "pendiente"),
    ]);
    abrir();
    await waitFor(() => expect(campo().value).toBe("https://principal.organismo.test"));
    expect(screen.getByRole("img", { name: /principal\.organismo\.test/ })).toBeInTheDocument();
    expect(screen.getByText("https://otro.organismo.test")).toBeInTheDocument();
    expect(screen.queryByText(/pendiente\.organismo\.test/)).not.toBeInTheDocument();
  });

  it("AC4: sin dominio activo usa la dirección del punto", async () => {
    consulta.mockResolvedValue([dominio("pendiente.organismo.test", "pendiente")]);
    abrir();
    await waitFor(() => expect(consulta).toHaveBeenCalled());
    await waitFor(() => expect(campo().value).toContain("/o/OT01"));
    expect(campo().value).not.toContain("pendiente.organismo.test");
    expect(screen.queryByText("Otras direcciones activas del organismo")).not.toBeInTheDocument();
  });

  it("AC4: si la consulta falla también queda la dirección del punto", async () => {
    consulta.mockRejectedValue(new Error("caída"));
    abrir();
    await waitFor(() => expect(screen.queryByText(/Buscando si el organismo/)).not.toBeInTheDocument());
    expect(campo().value).toContain("/o/OT01");
  });
});
