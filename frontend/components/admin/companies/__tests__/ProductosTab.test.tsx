// HU #12967 — pestaña «Productos» de la compañía: muestra el estado real de platform.tenant_products (también si la
// compañía no tiene configuración guardada, el caso de la prueba manual del 2026-10-02) y enciende o apaga al instante.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProductosTab } from "../tabs/ProductosTab";
import type { TenantProduct } from "@/lib/api/platform";
import { ApiError } from "@/lib/api/types";

const listTenantProducts = vi.fn();
const setTenantProduct = vi.fn();
const setTenantProductCredential = vi.fn();
const deleteTenantProductCredential = vi.fn();
const listMyApps = vi.fn();
const getTenantProductProvisioningPreview = vi.fn();
const retryTenantProductProvisioning = vi.fn();
vi.mock("@/lib/api/platform", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/platform")>()),
  listTenantProducts: (id: string) => listTenantProducts(id),
  setTenantProduct: (id: string, code: string, enabled: boolean) => setTenantProduct(id, code, enabled),
  setTenantProductCredential: (...a: unknown[]) => setTenantProductCredential(...a),
  deleteTenantProductCredential: (...a: unknown[]) => deleteTenantProductCredential(...a),
  listMyApps: () => listMyApps(),
  getTenantProductProvisioningPreview: (...a: unknown[]) => getTenantProductProvisioningPreview(...a),
  retryTenantProductProvisioning: (...a: unknown[]) => retryTenantProductProvisioning(...a),
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

const switchOf = (name: string) => screen.getByRole("switch", { name: new RegExp(`^${name}$`) });

describe("ProductosTab", () => {
  beforeEach(() => {
    listTenantProducts.mockReset();
    setTenantProduct.mockReset().mockResolvedValue({});
    setTenantProductCredential.mockReset().mockResolvedValue(undefined);
    deleteTenantProductCredential.mockReset().mockResolvedValue(undefined);
    listMyApps.mockReset().mockResolvedValue([]);
    getTenantProductProvisioningPreview.mockReset().mockResolvedValue({
      legalName: "Compañía Demo",
      taxIdBase: "900123456",
      nitStatus: "VALIDO",
      contactEmail: "nombre@empresa.com",
      blocking: null,
    });
    retryTenantProductProvisioning.mockReset().mockResolvedValue(undefined);
  });

  it("muestra el estado real de los tres productos, con quién y cuándo los cambió", async () => {
    listTenantProducts.mockResolvedValue([
      product("tramites", "Trámites", true, { updatedAt: "2026-10-02T16:17:00Z", updatedByEmail: "demo@flit.local" }),
      product("comparendos", "Comparendos", true, { comingSoon: true }),
      product("diagnostico", "Diagnóstico", false, { comingSoon: true }),
    ]);
    render(<ProductosTab tenantId="t1" />);

    expect(await screen.findByRole("switch", { name: /^Trámites$/ })).toBeChecked();
    // El estado es un chip con icono + texto, no parte del título.
    expect(screen.getAllByText("Encendido")).toHaveLength(2);
    expect(screen.getAllByText("Apagado")).toHaveLength(1);
    expect(screen.getAllByText("Próximamente")).toHaveLength(2);
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

    await user.click(await screen.findByRole("switch", { name: /^Diagnóstico$/ }));

    expect(setTenantProduct).toHaveBeenCalledWith("t1", "diagnostico", true);
    expect(await screen.findByRole("switch", { name: /^Diagnóstico$/ })).toBeChecked();
    expect(listTenantProducts).toHaveBeenCalledTimes(2);
  });

  it("apagar pide confirmación; cancelar no cambia nada y confirmar lo apaga", async () => {
    const user = userEvent.setup();
    listTenantProducts.mockResolvedValue([product("tramites", "Trámites", true)]);
    render(<ProductosTab tenantId="t1" />);

    await user.click(await screen.findByRole("switch", { name: /^Trámites$/ }));
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

    await user.click(await screen.findByRole("switch", { name: /^Comparendos$/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("La empresa no existe.");
    expect(switchOf("Comparendos")).not.toBeChecked();
  });

  describe("API key de impuesto de Diagnóstico (ola impuesto-fuente-real, diseno.md §5.1)", () => {
    const campo = () => screen.queryByLabelText("API key de impuesto vehicular");

    it("solo Diagnóstico muestra el estado de la llave, sin valor", async () => {
      listTenantProducts.mockResolvedValue([
        product("tramites", "Trámites", true, { impuestoApiKeyConfigurada: null }),
        product("diagnostico", "Diagnóstico", true, { impuestoApiKeyConfigurada: true }),
      ]);
      render(<ProductosTab tenantId="t1" />);
      const region = await screen.findByRole("group", { name: "API key de impuesto de Diagnóstico" });
      expect(within(region).getByText("Configurada")).toBeInTheDocument();
      expect(screen.getAllByRole("group", { name: /API key de impuesto/ })).toHaveLength(1);
      expect(campo()).toBeNull();
    });

    it("al encender Diagnóstico sin llave abre el formulario con «Ahora no» (opcional, no bloquea)", async () => {
      const user = userEvent.setup();
      listTenantProducts
        .mockResolvedValueOnce([product("diagnostico", "Diagnóstico", false, { impuestoApiKeyConfigurada: false })])
        .mockResolvedValueOnce([product("diagnostico", "Diagnóstico", true, { impuestoApiKeyConfigurada: false })]);
      render(<ProductosTab tenantId="t1" />);
      expect(await screen.findByText("No configurada")).toBeInTheDocument();
      expect(campo()).toBeNull();

      await user.click(switchOf("Diagnóstico"));

      expect(await screen.findByLabelText("API key de impuesto vehicular")).toHaveValue("");
      expect(switchOf("Diagnóstico")).toBeChecked();
      await user.click(screen.getByRole("button", { name: "Ahora no" }));
      expect(campo()).toBeNull();
      expect(setTenantProductCredential).not.toHaveBeenCalled();
    });

    it("al encender Diagnóstico con llave ya configurada no abre el formulario", async () => {
      const user = userEvent.setup();
      listTenantProducts
        .mockResolvedValueOnce([product("diagnostico", "Diagnóstico", false, { impuestoApiKeyConfigurada: true })])
        .mockResolvedValueOnce([product("diagnostico", "Diagnóstico", true, { impuestoApiKeyConfigurada: true })]);
      render(<ProductosTab tenantId="t1" />);
      await user.click(await screen.findByRole("switch", { name: /^Diagnóstico$/ }));
      expect(await screen.findByRole("switch", { name: /^Diagnóstico$/ })).toBeChecked();
      expect(campo()).toBeNull();
    });

    it("guardar la llave relee los productos y pasa a «Configurada»", async () => {
      const user = userEvent.setup();
      listTenantProducts
        .mockResolvedValueOnce([product("diagnostico", "Diagnóstico", true, { impuestoApiKeyConfigurada: false })])
        .mockResolvedValueOnce([product("diagnostico", "Diagnóstico", true, { impuestoApiKeyConfigurada: true })]);
      render(<ProductosTab tenantId="t1" />);
      await user.click(await screen.findByRole("button", { name: "Configurar" }));
      await user.click(screen.getByLabelText("API key de impuesto vehicular"));
      await user.paste("llave-sintetica-0123456789");
      await user.click(screen.getByRole("button", { name: "Guardar" }));
      expect(setTenantProductCredential).toHaveBeenCalledWith("t1", "diagnostico", "impuesto_api_key", "llave-sintetica-0123456789");
      expect(await screen.findByText("Configurada")).toBeInTheDocument();
      expect(listTenantProducts).toHaveBeenCalledTimes(2);
    });
  });

  // HU #14695 · M19 — Impuestos: diálogo de activación, suspensión con texto propio y estado de aprovisionamiento.
  describe("Impuestos (HU #14695, M19)", () => {
    const prov = (status: string, action: string | null, message: string | null = null) =>
      ({
        status,
        reason: null,
        message,
        action,
        signatureConfirmed: null,
        updatedAt: "2026-11-24T15:00:00Z",
      }) as TenantProduct["provisioning"];
    const impuestos = (enabled: boolean, extra: Partial<TenantProduct> = {}) =>
      product("impuestos", "Impuestos", enabled, { comingSoon: true, provisioning: null, ...extra });
    const grupo = () => screen.getByRole("group", { name: "Conexión con el proveedor de Impuestos" });

    it("encender Impuestos abre el diálogo y solo se aplica al confirmar", async () => {
      const user = userEvent.setup();
      listTenantProducts
        .mockResolvedValueOnce([impuestos(false)])
        .mockResolvedValueOnce([impuestos(true, { provisioning: prov("PENDIENTE", null) })]);
      render(<ProductosTab tenantId="t1" />);

      await user.click(await screen.findByRole("switch", { name: /^Impuestos$/ }));
      const d = screen.getByRole("dialog", { name: "Activar Impuestos" });
      expect(setTenantProduct).not.toHaveBeenCalled();
      expect(await within(d).findByText("900123456")).toBeInTheDocument();

      await user.click(within(d).getByRole("button", { name: "Activar Impuestos" }));
      expect(setTenantProduct).toHaveBeenCalledWith("t1", "impuestos", true);
      expect(await screen.findByRole("switch", { name: /^Impuestos$/ })).toBeChecked();
      expect(screen.queryByRole("dialog", { name: "Activar Impuestos" })).toBeNull();
      expect(within(grupo()).getByText("Pendiente")).toBeInTheDocument();
    });

    it("cancelar el diálogo no enciende nada", async () => {
      const user = userEvent.setup();
      listTenantProducts.mockResolvedValue([impuestos(false)]);
      render(<ProductosTab tenantId="t1" />);
      await user.click(await screen.findByRole("switch", { name: /^Impuestos$/ }));
      const d = screen.getByRole("dialog", { name: "Activar Impuestos" });
      await within(d).findByText("900123456");
      await user.click(within(d).getByRole("button", { name: "Cancelar" }));
      expect(screen.queryByRole("dialog", { name: "Activar Impuestos" })).toBeNull();
      expect(setTenantProduct).not.toHaveBeenCalled();
      expect(switchOf("Impuestos")).not.toBeChecked();
    });

    it("el 409 PROVISIONING_PREREQUISITE_MISSING se muestra en el diálogo, que no se cierra", async () => {
      const user = userEvent.setup();
      listTenantProducts.mockResolvedValue([impuestos(false)]);
      setTenantProduct.mockRejectedValue(
        new ApiError(409, "Falta un requisito para activar el producto", { code: "PROVISIONING_PREREQUISITE_MISSING" }),
      );
      render(<ProductosTab tenantId="t1" />);
      await user.click(await screen.findByRole("switch", { name: /^Impuestos$/ }));
      const d = screen.getByRole("dialog", { name: "Activar Impuestos" });
      await within(d).findByText("900123456");
      await user.click(within(d).getByRole("button", { name: "Activar Impuestos" }));
      expect(await within(d).findByRole("alert")).toHaveTextContent("La compañía no tiene un Administrador de Compañía activo");
      expect(screen.getByRole("dialog", { name: "Activar Impuestos" })).toBeInTheDocument();
      expect(switchOf("Impuestos")).not.toBeChecked();
    });

    it("apagar Impuestos usa la confirmación de suspensión con su texto", async () => {
      const user = userEvent.setup();
      listTenantProducts.mockResolvedValue([impuestos(true, { provisioning: prov("LISTO", null) })]);
      render(<ProductosTab tenantId="t1" />);
      await user.click(await screen.findByRole("switch", { name: /^Impuestos$/ }));
      const c = screen.getByRole("alertdialog");
      expect(within(c).getByText("¿Suspender Impuestos para esta compañía?")).toBeInTheDocument();
      expect(
        within(c).getByText(
          "Nadie de la compañía podrá entrar ni crear liquidaciones. Los lotes en curso terminan y el historial se conserva. Al volver a encenderlo, sigue con las mismas credenciales.",
        ),
      ).toBeInTheDocument();
      await user.click(within(c).getByRole("button", { name: "Suspender Impuestos" }));
      expect(setTenantProduct).toHaveBeenCalledWith("t1", "impuestos", false);
    });

    it("el estado de aprovisionamiento usa la URL de Impuestos de me/apps y se anuncia en el tab", async () => {
      listTenantProducts.mockResolvedValue([impuestos(true, { provisioning: prov("REQUIERE_CREDENCIALES", "CARGAR_CREDENCIALES") })]);
      listMyApps.mockResolvedValue([{ code: "impuestos", name: "Impuestos", icon: "receipt", url: "https://impuestos.test", current: false }]);
      render(<ProductosTab tenantId="t1" />);
      expect(await screen.findByRole("link", { name: /Cargar credenciales/ })).toHaveAttribute(
        "href",
        "https://impuestos.test/backoffice/empresas/t1",
      );
      const anuncio = screen.getByTestId("anuncio-aprovisionamiento");
      expect(anuncio).toHaveAttribute("role", "status");
      expect(anuncio).toHaveAttribute("aria-atomic", "true");
      expect(anuncio).toHaveTextContent("Conexión con el proveedor: Requiere credenciales");
    });

    it("si me/apps falla, el estado se pinta igual sin enlace", async () => {
      listTenantProducts.mockResolvedValue([impuestos(true, { provisioning: prov("LISTO_SIN_WEBHOOK", "CARGAR_SECRETO") })]);
      listMyApps.mockRejectedValue(new Error("x"));
      render(<ProductosTab tenantId="t1" />);
      expect(await within(await screen.findByRole("group", { name: "Conexión con el proveedor de Impuestos" })).findByText("Cargar secreto: hazlo desde Impuestos › Backoffice")).toBeInTheDocument();
      expect(screen.queryByRole("link")).toBeNull();
    });

    it("Reintentar relee los productos", async () => {
      const user = userEvent.setup();
      listTenantProducts
        .mockResolvedValueOnce([impuestos(true, { provisioning: prov("ERROR", "REINTENTAR", "Motivo demo") })])
        .mockResolvedValueOnce([impuestos(true, { provisioning: prov("PENDIENTE", null) })]);
      render(<ProductosTab tenantId="t1" />);
      await user.click(await screen.findByRole("button", { name: "Reintentar" }));
      expect(retryTenantProductProvisioning).toHaveBeenCalledWith("t1", "impuestos");
      expect(await screen.findByText("Conexión con el proveedor: Pendiente")).toBeInTheDocument();
      expect(listTenantProducts).toHaveBeenCalledTimes(2);
    });

    it("con onEditarCompania, «Corregir NIT» lo llama", async () => {
      const user = userEvent.setup();
      const onEditarCompania = vi.fn();
      listTenantProducts.mockResolvedValue([impuestos(true, { provisioning: prov("REQUIERE_REVISION_NIT", "CORREGIR_NIT") })]);
      render(<ProductosTab tenantId="t1" onEditarCompania={onEditarCompania} />);
      await user.click(await screen.findByRole("button", { name: "Corregir NIT" }));
      expect(onEditarCompania).toHaveBeenCalledTimes(1);
    });

    it("Impuestos apagado y sin reporte no muestra la subtarjeta ni consulta me/apps sin Impuestos", async () => {
      listTenantProducts.mockResolvedValue([impuestos(false)]);
      const { unmount } = render(<ProductosTab tenantId="t1" />);
      await screen.findByRole("switch", { name: /^Impuestos$/ });
      expect(screen.queryByRole("group", { name: "Conexión con el proveedor de Impuestos" })).toBeNull();
      unmount();

      listMyApps.mockClear();
      listTenantProducts.mockResolvedValue([product("tramites", "Trámites", true)]);
      render(<ProductosTab tenantId="t1" />);
      await screen.findByRole("switch", { name: /^Trámites$/ });
      expect(listMyApps).not.toHaveBeenCalled();
    });
  });
});
