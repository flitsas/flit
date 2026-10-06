// HU #12967 — pestaña «Productos» de la compañía: muestra el estado real de platform.tenant_products (también si la
// compañía no tiene configuración guardada, el caso de la prueba manual del 2026-10-02) y enciende o apaga al instante.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProductosTab } from "../tabs/ProductosTab";
import type { TenantProduct } from "@/lib/api/platform";

const listTenantProducts = vi.fn();
const setTenantProduct = vi.fn();
vi.mock("@/lib/api/platform", () => ({
  listTenantProducts: (id: string) => listTenantProducts(id),
  setTenantProduct: (id: string, code: string, enabled: boolean) => setTenantProduct(id, code, enabled),
}));

const product = (code: string, name: string, enabled: boolean, extra: Partial<TenantProduct> = {}): TenantProduct => ({
  productCode: code,
  name,
  enabled,
  notes: null,
  updatedAt: null,
  updatedBy: null,
  updatedByEmail: null,
  comingSoon: false,
  ...extra,
});

const switchOf = (name: string) => screen.getByRole("switch", { name: new RegExp(`^${name} ·`) });

describe("ProductosTab", () => {
  beforeEach(() => {
    listTenantProducts.mockReset();
    setTenantProduct.mockReset().mockResolvedValue({});
  });

  it("muestra el estado real de los tres productos, con quién y cuándo los cambió", async () => {
    listTenantProducts.mockResolvedValue([
      product("tramites", "Trámites", true, { updatedAt: "2026-10-02T16:17:00Z", updatedByEmail: "demo@flit.local" }),
      product("comparendos", "Comparendos", true, { comingSoon: true }),
      product("diagnostico", "Diagnóstico", false, { comingSoon: true }),
    ]);
    render(<ProductosTab tenantId="t1" />);

    expect(await screen.findByRole("switch", { name: /^Trámites · Encendido/ })).toBeChecked();
    expect(switchOf("Comparendos")).toBeChecked();
    expect(switchOf("Diagnóstico")).not.toBeChecked();
    expect(screen.getByText(/por demo@flit\.local/)).toBeInTheDocument();
    expect(screen.getAllByText(/Próximamente: todavía no está desplegado/)).toHaveLength(2);
    expect(listTenantProducts).toHaveBeenCalledWith("t1");
  });

  it("encender se aplica al instante y relee el estado guardado", async () => {
    const user = userEvent.setup();
    listTenantProducts
      .mockResolvedValueOnce([product("diagnostico", "Diagnóstico", false)])
      .mockResolvedValueOnce([product("diagnostico", "Diagnóstico", true, { updatedAt: "2026-10-02T17:00:00Z", updatedByEmail: "demo@flit.local" })]);
    render(<ProductosTab tenantId="t1" />);

    await user.click(await screen.findByRole("switch", { name: /^Diagnóstico ·/ }));

    expect(setTenantProduct).toHaveBeenCalledWith("t1", "diagnostico", true);
    expect(await screen.findByRole("switch", { name: /^Diagnóstico · Encendido/ })).toBeChecked();
    expect(listTenantProducts).toHaveBeenCalledTimes(2);
  });

  it("apagar pide confirmación; cancelar no cambia nada y confirmar lo apaga", async () => {
    const user = userEvent.setup();
    listTenantProducts.mockResolvedValue([product("tramites", "Trámites", true)]);
    render(<ProductosTab tenantId="t1" />);

    await user.click(await screen.findByRole("switch", { name: /^Trámites ·/ }));
    const confirm = screen.getByRole("alertdialog");
    expect(within(confirm).getByText("¿Apagar Trámites para esta compañía?")).toBeInTheDocument();
    expect(setTenantProduct).not.toHaveBeenCalled();

    await user.click(within(confirm).getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(setTenantProduct).not.toHaveBeenCalled();

    await user.click(switchOf("Trámites"));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Apagar Trámites" }));
    expect(setTenantProduct).toHaveBeenCalledWith("t1", "tramites", false);
  });

  it("si la API falla al cambiar, lo dice y deja el estado como estaba", async () => {
    const user = userEvent.setup();
    listTenantProducts.mockResolvedValue([product("comparendos", "Comparendos", false)]);
    setTenantProduct.mockRejectedValue(new Error("La empresa no existe."));
    render(<ProductosTab tenantId="t1" />);

    await user.click(await screen.findByRole("switch", { name: /^Comparendos ·/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("La empresa no existe.");
    expect(switchOf("Comparendos")).not.toBeChecked();
  });
});
