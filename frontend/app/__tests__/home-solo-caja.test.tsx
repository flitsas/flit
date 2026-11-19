// El cajero (solo ot_caja) nunca se queda en «/» en blanco: cualquier ruta no permitida termina en su Caja, también cuando
// otra guarda lo devuelve a «/» con la marca `?acceso=denegado` y la limpia con su propio router.replace("/").
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, waitFor } from "@testing-library/react";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

const replace = vi.fn();
let query = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace }),
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(query),
}));
vi.mock("@/hooks/useAuthGate", () => ({ useAuthGate: () => ({ authed: true, hydrated: true, logout: vi.fn() }) }));
vi.mock("@/hooks/useAccessibleModules", () => ({
  useAccessibleModules: () => ({ modules: [], loading: false, ready: true, error: null }),
}));
vi.mock("@/components/atom/Shell", () => ({ Shell: ({ children }: { children: React.ReactNode }) => <div>{children}</div> }));

import Page from "../page";

function token(roles: string[]): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${b64({ alg: "none" })}.${b64({ tenant_id: "t-1", entity_type: "TRANSIT_OFFICE", roles: roles.map((code) => ({ id: code, code })) })}.`;
}
const CAJA = "/admin/transit-offices/t-1/portal-persona-natural?tab=caja";

beforeEach(() => {
  replace.mockClear();
  query = "";
  window.localStorage.setItem(TOKEN_STORAGE_KEY, token(["ot_caja"]));
});
afterEach(() => window.localStorage.clear());

describe("inicio del cajero", () => {
  it("«/» en frío lo lleva a su Caja", async () => {
    render(<Page />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith(CAJA));
  });

  it("con la marca de acceso denegado no limpia la URL a «/»: va directo a su Caja", async () => {
    query = "acceso=denegado&modulo=admin";
    render(<Page />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith(CAJA));
    expect(replace).not.toHaveBeenCalledWith("/", expect.anything());
  });

  it("si otra guarda lo devuelve a «/», el efecto vuelve a dispararse y lo lleva a su Caja", async () => {
    query = "acceso=denegado";
    const { rerender } = render(<Page />);
    await waitFor(() => expect(replace).toHaveBeenCalled());
    replace.mockClear();
    query = "";
    rerender(<Page />);
    await waitFor(() => expect(replace).toHaveBeenLastCalledWith(CAJA));
  });
});
