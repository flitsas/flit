// La persona de caja (solo `ot_caja`) entra a Portal Persona Natural de SU organismo: el encabezado del hub no pide
// perfil, catálogo ni estado operativo (la API respondería 403 `caja_sin_acceso`), y si la URL trae otro organismo, la
// lleva al suyo.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

const replace = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), replace }), usePathname: () => "/" }));
vi.mock("@/lib/api/admin-ot", () => ({ fetchOtProfile: vi.fn() }));
vi.mock("@/lib/api/admin-companies", () => ({ fetchTransitOffices: vi.fn() }));
vi.mock("@/lib/api/admin-transit-office-tenants", () => ({ fetchTransitOfficesOperationalStatus: vi.fn() }));

import { fetchOtProfile } from "@/lib/api/admin-ot";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";
import { OtHubLayout } from "../OtHubLayout";

function token(claims: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${b64({ alg: "none" })}.${b64(claims)}.`;
}

beforeEach(() => {
  vi.clearAllMocks();
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    token({ tenant_id: "t-1", tenant_name: "Transito de Cali", entity_type: "TRANSIT_OFFICE", roles: [{ id: "x", code: "ot_caja" }] }),
  );
});
afterEach(() => window.localStorage.clear());

describe("OtHubLayout — persona de caja", () => {
  it("no llama a ninguna API de administración y muestra el organismo del token", async () => {
    render(
      <OtHubLayout transitOfficeId="t-1" activeTab="portal-persona-natural" moduleTitle="Portal">
        <p>contenido</p>
      </OtHubLayout>,
    );
    expect(await screen.findByText("Transito de Cali")).toBeInTheDocument();
    expect(fetchOtProfile).not.toHaveBeenCalled();
    expect(fetchTransitOffices).not.toHaveBeenCalled();
    expect(fetchTransitOfficesOperationalStatus).not.toHaveBeenCalled();
    expect(replace).not.toHaveBeenCalled();
  });

  it("si entra con el id de otro organismo, la lleva al suyo", async () => {
    render(
      <OtHubLayout transitOfficeId="otro-organismo" activeTab="portal-persona-natural" moduleTitle="Portal">
        <p>contenido</p>
      </OtHubLayout>,
    );
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/admin/transit-offices/t-1/portal-persona-natural"));
    expect(fetchOtProfile).not.toHaveBeenCalled();
  });
});
