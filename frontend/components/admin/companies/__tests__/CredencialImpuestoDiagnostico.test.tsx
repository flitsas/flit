// Ola impuesto-fuente-real (diseno.md §5.1) — API key de impuesto de Diagnóstico por compañía. La UI nunca recibe ni
// muestra el valor guardado: solo «Configurada» / «No configurada». Valida 16..256 ASCII imprimible sin espacios.
// Uso de ejemplo: <CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={relee} />
import { beforeEach, describe, expect, it, vi, type Mock } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CredencialImpuestoDiagnostico } from "../tabs/CredencialImpuestoDiagnostico";
import { ApiError } from "@/lib/api/types";

const setTenantProductCredential = vi.fn();
const deleteTenantProductCredential = vi.fn();
const obtenerCredencialImpuestoDiagnostico = vi.fn();
vi.mock("@/lib/api/platform", () => ({
  obtenerCredencialImpuestoDiagnostico: (...a: unknown[]) => obtenerCredencialImpuestoDiagnostico(...a),
  setTenantProductCredential: (...a: unknown[]) => setTenantProductCredential(...a),
  deleteTenantProductCredential: (...a: unknown[]) => deleteTenantProductCredential(...a),
}));

const LLAVE = "llave-sintetica-0123456789";
const MSG_LONGITUD = "La API key debe tener entre 16 y 256 caracteres, sin espacios.";
const campo = () => screen.getByLabelText("API key de impuesto vehicular");
// HU #14711: la captura manual vive en el disclosure plegado «Ingresar manualmente»; los tests de siempre lo despliegan antes.
const expandirManual = () => userEvent.click(screen.getByRole("button", { name: "Ingresar manualmente" }));

describe("CredencialImpuestoDiagnostico", () => {
  let onChanged: Mock<() => Promise<void>>;
  beforeEach(() => {
    setTenantProductCredential.mockReset().mockResolvedValue(undefined);
    deleteTenantProductCredential.mockReset().mockResolvedValue(undefined);
    onChanged = vi.fn().mockResolvedValue(undefined);
  });

  it("muestra «No configurada» con chip de texto y sin formulario abierto", async () => {
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    expect(screen.getByText("No configurada")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Configurar" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Borrar" })).toBeNull();
    expect(screen.queryByLabelText("API key de impuesto vehicular")).toBeNull();
  });

  it("muestra «Configurada» con Reemplazar y Borrar, y nunca un valor", async () => {
    const { container } = render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
    await expandirManual();
    expect(screen.getByText("Configurada")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reemplazar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Borrar" })).toBeInTheDocument();
    expect(container.querySelector("input")).toBeNull();
  });

  it("el campo es password, sin autocompletar ni corrector, vacío y con la ayuda de opcional", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Reemplazar" }));
    const input = campo();
    expect(input).toHaveAttribute("type", "password");
    expect(input).toHaveAttribute("autocomplete", "off");
    expect(input).toHaveAttribute("spellcheck", "false");
    expect(input).toHaveValue("");
    expect(input).toHaveAccessibleDescription(/Opcional\. Sin ella, la consulta de impuesto de esta compañía queda en error «Credencial de impuesto no configurada»\./);
  });

  it("mostrar/ocultar cambia solo lo que se escribe", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    const toggle = screen.getByRole("button", { name: "Mostrar API key" });
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    await user.click(toggle);
    expect(campo()).toHaveAttribute("type", "text");
    expect(screen.getByRole("button", { name: "Ocultar API key" })).toHaveAttribute("aria-pressed", "true");
    await user.click(screen.getByRole("button", { name: "Ocultar API key" }));
    expect(campo()).toHaveAttribute("type", "password");
  });

  it.each([
    ["corta (15)", "a".repeat(15)],
    ["con espacio", "abcdefgh ijklmnop"],
    ["larga (257)", "a".repeat(257)],
    ["no ASCII", "llave-con-eñe-12345678"],
  ])("rechaza en local una llave %s sin llamar a la API", async (_n, valor) => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste(valor);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(setTenantProductCredential).not.toHaveBeenCalled();
    expect(campo()).toHaveAttribute("aria-invalid", "true");
    expect(campo()).toHaveAccessibleDescription(new RegExp(MSG_LONGITUD.replace(/\./g, "\\.")));
  });

  it("acepta 16 y 256 caracteres; guarda, relee, limpia el campo y anuncia el éxito", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste("a".repeat(16));
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(setTenantProductCredential).toHaveBeenCalledWith("t1", "diagnostico", "impuesto_api_key", "a".repeat(16));
    await screen.findByText("API key de impuesto guardada.");
    expect(onChanged).toHaveBeenCalledTimes(1);
    expect(screen.queryByLabelText("API key de impuesto vehicular")).toBeNull();
    const status = screen.getByText("API key de impuesto guardada.");
    expect(status).toHaveAttribute("role", "status");
    expect(status).toHaveAttribute("aria-atomic", "true");
    expect(status).toHaveTextContent("API key de impuesto guardada.");

    await user.click(screen.getByRole("button", { name: "Configurar" }));
    expect(campo()).toHaveValue("");
    await user.click(campo());
    await user.paste("b".repeat(256));
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(setTenantProductCredential).toHaveBeenLastCalledWith("t1", "diagnostico", "impuesto_api_key", "b".repeat(256));
  });

  it("mientras guarda, el botón queda en espera", async () => {
    const user = userEvent.setup();
    let resolver: () => void = () => {};
    setTenantProductCredential.mockReturnValue(new Promise<void>((r) => (resolver = r)));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste(LLAVE);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(screen.getByRole("button", { name: /Guardando/ })).toHaveAttribute("aria-busy", "true");
    resolver();
    expect(await screen.findByText("API key de impuesto guardada.")).toHaveAttribute("role", "status");
  });

  it("el 400 CREDENTIAL_INVALID muestra el literal de longitud, sin eco del valor", async () => {
    const user = userEvent.setup();
    setTenantProductCredential.mockRejectedValue(new ApiError(400, "Valor inválido.", { code: "CREDENTIAL_INVALID" }));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste(LLAVE);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent(MSG_LONGITUD);
    expect(alerta.textContent).not.toContain(LLAVE);
    expect(onChanged).not.toHaveBeenCalled();
  });

  it("otro error al guardar muestra el mensaje de la API", async () => {
    const user = userEvent.setup();
    setTenantProductCredential.mockRejectedValue(new ApiError(404, "La empresa no existe.", { code: "TENANT_NOT_FOUND" }));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste(LLAVE);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("La empresa no existe.");
  });

  it("cancelar cierra y vacía lo escrito", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste(LLAVE);
    await user.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByLabelText("API key de impuesto vehicular")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    expect(campo()).toHaveValue("");
    expect(setTenantProductCredential).not.toHaveBeenCalled();
  });

  it("abierto por sugerencia ofrece «Ahora no» y la llave sigue siendo opcional", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} sugerirAbierto onChanged={onChanged} />);
    expect(campo()).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Ahora no" }));
    expect(screen.queryByLabelText("API key de impuesto vehicular")).toBeNull();
    expect(setTenantProductCredential).not.toHaveBeenCalled();
  });

  it("borrar pide confirmación en alertdialog; cancelar no borra y confirmar borra y relee", async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(window, "confirm");
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Borrar" }));
    const dlg = screen.getByRole("alertdialog", { name: "¿Borrar la API key de impuesto de esta compañía?" });
    await user.click(within(dlg).getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(deleteTenantProductCredential).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Borrar" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Borrar API key" }));
    expect(await screen.findByText("API key de impuesto borrada.")).toHaveAttribute("role", "status");
    expect(deleteTenantProductCredential).toHaveBeenCalledWith("t1", "diagnostico", "impuesto_api_key");
    expect(onChanged).toHaveBeenCalledTimes(1);
    expect(confirmSpy).not.toHaveBeenCalled();
    confirmSpy.mockRestore();
  });

  it("si borrar falla, lo dice", async () => {
    const user = userEvent.setup();
    deleteTenantProductCredential.mockRejectedValue(new ApiError(404, "La empresa no existe.", { code: "TENANT_NOT_FOUND" }));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Borrar" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Borrar API key" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("La empresa no existe.");
    expect(onChanged).not.toHaveBeenCalled();
  });
});

// HU #14711 · AC-DC-13 (y AC-DC-12 en la UI) — «Obtener del proveedor»: la llave se pide al proveedor con el NIT de la empresa.
// La respuesta solo trae metadatos (prefijo, banderas); la UI nunca muestra ni guarda la llave ni el secreto del webhook.
// Uso de ejemplo: <CredencialImpuestoDiagnostico tenantId="t1" configurada={false} webhookSecretConfigurada={null} onChanged={relee} />
describe("CredencialImpuestoDiagnostico · Obtener del proveedor (HU #14711)", () => {
  let onChanged: Mock<() => Promise<void>>;
  const respuesta = (extra: Record<string, unknown> = {}) => ({
    apiKeyPrefix: "AbCdEfGh",
    created: true,
    rotated: false,
    hasWebhookSecret: true,
    changed: true,
    updatedAt: "2026-11-25T10:00:00Z",
    ...extra,
  });
  const obtener = () => screen.getByRole("button", { name: "Obtener del proveedor" });
  const MSG_NIT = "La empresa no tiene un NIT válido; complétalo o ingresa la llave manualmente";
  const MSG_PROVEEDOR = "El proveedor no respondió; intenta de nuevo";

  beforeEach(() => {
    setTenantProductCredential.mockReset().mockResolvedValue(undefined);
    deleteTenantProductCredential.mockReset().mockResolvedValue(undefined);
    obtenerCredencialImpuestoDiagnostico.mockReset().mockResolvedValue(respuesta());
    onChanged = vi.fn().mockResolvedValue(undefined);
  });

  it("vacío: sin credencial explica que se obtiene del proveedor con el NIT y ofrece el botón primario", () => {
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} webhookSecretConfigurada={null} onChanged={onChanged} />);
    expect(obtener()).toBeEnabled();
    expect(obtener()).not.toHaveAttribute("aria-busy");
    expect(screen.getByText(/Se obtiene del proveedor de impuesto con el NIT de la empresa\./)).toBeInTheDocument();
    expect(screen.queryByText(/Llave configurada/)).toBeNull();
  });

  it("la captura manual queda plegada en «Ingresar manualmente» y se despliega con aria-expanded", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    const toggle = screen.getByRole("button", { name: "Ingresar manualmente" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("button", { name: "Configurar" })).toBeNull();
    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    const panel = document.getElementById(toggle.getAttribute("aria-controls") ?? "");
    expect(panel).not.toBeNull();
    expect(within(panel as HTMLElement).getByRole("button", { name: "Configurar" })).toBeInTheDocument();
    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("button", { name: "Configurar" })).toBeNull();
  });

  it("plegar el disclosure con el formulario abierto descarta lo escrito", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    const toggle = screen.getByRole("button", { name: "Ingresar manualmente" });
    await user.click(toggle);
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste(LLAVE);
    await user.click(toggle);
    await user.click(toggle);
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    expect(campo()).toHaveValue("");
  });

  it("cargando: el botón queda inoperable con aria-busy mientras pide la llave", async () => {
    const user = userEvent.setup();
    let resolver: (v: unknown) => void = () => {};
    obtenerCredencialImpuestoDiagnostico.mockReturnValue(new Promise((r) => (resolver = r)));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await user.click(obtener());
    expect(obtener()).toHaveAttribute("aria-busy", "true");
    expect(obtener()).toHaveAttribute("aria-disabled", "true");
    await user.click(obtener());
    expect(obtenerCredencialImpuestoDiagnostico).toHaveBeenCalledTimes(1);
    resolver(respuesta());
    expect(await screen.findByText("Llave configurada · prefijo AbCdEfGh…")).toBeInTheDocument();
    expect(obtener()).not.toHaveAttribute("aria-busy");
  });

  it("lleno: llama al alta sin cuerpo, muestra el prefijo y el secreto de webhook, anuncia y relee el listado", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} webhookSecretConfigurada={null} onChanged={onChanged} />);
    await user.click(obtener());
    expect(obtenerCredencialImpuestoDiagnostico).toHaveBeenCalledWith("t1");
    expect(await screen.findByText("Llave configurada · prefijo AbCdEfGh…")).toBeInTheDocument();
    expect(screen.getByText("Secreto de webhook configurado")).toBeInTheDocument();
    expect(onChanged).toHaveBeenCalledTimes(1);
    const status = screen.getByText("Llave obtenida del proveedor.");
    expect(status).toHaveAttribute("role", "status");
    expect(status).toHaveAttribute("aria-atomic", "true");
    expect(screen.queryByText(/El proveedor reemplazó la llave anterior/)).toBeNull();
  });

  it("lleno sin secreto de webhook dice «Sin secreto de webhook»", async () => {
    const user = userEvent.setup();
    obtenerCredencialImpuestoDiagnostico.mockResolvedValue(respuesta({ hasWebhookSecret: false, created: false, changed: false }));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
    await user.click(obtener());
    expect(await screen.findByText("Sin secreto de webhook")).toBeInTheDocument();
    expect(screen.getByText("Llave configurada · prefijo AbCdEfGh…")).toBeInTheDocument();
  });

  it("con rotated=true muestra el aviso visible de llave reemplazada", async () => {
    const user = userEvent.setup();
    obtenerCredencialImpuestoDiagnostico.mockResolvedValue(respuesta({ rotated: true, created: false }));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
    await user.click(obtener());
    expect(
      await screen.findByText("El proveedor reemplazó la llave anterior; Impuestos se actualiza automáticamente"),
    ).toBeVisible();
  });

  it("sin respuesta del alta, el estado del listado dice «Llave configurada» y el secreto según el listado", () => {
    const { rerender } = render(
      <CredencialImpuestoDiagnostico tenantId="t1" configurada webhookSecretConfigurada onChanged={onChanged} />,
    );
    expect(screen.getByText("Llave configurada")).toBeInTheDocument();
    expect(screen.queryByText(/prefijo/)).toBeNull();
    expect(screen.getByText("Secreto de webhook configurado")).toBeInTheDocument();
    rerender(<CredencialImpuestoDiagnostico tenantId="t1" configurada webhookSecretConfigurada={false} onChanged={onChanged} />);
    expect(screen.getByText("Sin secreto de webhook")).toBeInTheDocument();
    rerender(<CredencialImpuestoDiagnostico tenantId="t1" configurada webhookSecretConfigurada={null} onChanged={onChanged} />);
    expect(screen.queryByText(/secreto de webhook/i)).toBeNull();
  });

  it.each([
    ["NIT_AUSENTE", 422, MSG_NIT],
    ["NIT_AMBIGUO", 422, MSG_NIT],
    ["NIT_IGUAL_A_CABEZA", 422, MSG_NIT],
    ["PRODUCTO_NO_HABILITADO", 409, "Diagnóstico no está habilitado para esta empresa; actívalo antes de obtener la llave"],
    ["PROVEEDOR_NO_CONFIGURADO", 503, "La integración con el proveedor no está configurada en este ambiente"],
    ["PROVEEDOR_NO_DISPONIBLE", 502, MSG_PROVEEDOR],
    ["PROVEEDOR_TOKEN_INVALIDO", 502, "El proveedor rechazó la autorización de este ambiente; revisa la configuración de la integración"],
    ["PROVEEDOR_INTEGRACION_DESHABILITADA", 502, "El proveedor tiene deshabilitada la integración; ingresa la llave manualmente"],
    ["PROVEEDOR_SOLICITUD_INVALIDA", 502, "El proveedor rechazó los datos de la empresa; revisa el NIT o ingresa la llave manualmente"],
  ])("error %s (%i) muestra su mensaje en role=alert y no relee", async (code, status, mensaje) => {
    const user = userEvent.setup();
    obtenerCredencialImpuestoDiagnostico.mockRejectedValue(new ApiError(status, "detalle técnico del servidor", { code }));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await user.click(obtener());
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent(mensaje);
    expect(alerta).not.toHaveTextContent("detalle técnico del servidor");
    expect(onChanged).not.toHaveBeenCalled();
    expect(obtener()).not.toHaveAttribute("aria-busy");
  });

  it("el 429 por límite pide esperar", async () => {
    const user = userEvent.setup();
    obtenerCredencialImpuestoDiagnostico.mockRejectedValue(new ApiError(429, "Too Many Requests"));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await user.click(obtener());
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Se hicieron demasiadas solicitudes al proveedor; espera un momento e intenta de nuevo",
    );
  });

  it("un error sin código conocido usa el mensaje por defecto, y al reintentar se limpia la alerta", async () => {
    const user = userEvent.setup();
    obtenerCredencialImpuestoDiagnostico.mockRejectedValueOnce(new ApiError(500, "", undefined));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await user.click(obtener());
    expect(await screen.findByRole("alert")).toHaveTextContent("No se pudo obtener la llave del proveedor; intenta de nuevo");
    await user.click(obtener());
    await screen.findByText("Llave configurada · prefijo AbCdEfGh…");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("AC-DC-12: nunca pinta ni guarda en el navegador la llave ni el secreto aunque la respuesta los traiga", async () => {
    const user = userEvent.setup();
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    obtenerCredencialImpuestoDiagnostico.mockResolvedValue(
      respuesta({ apiKey: "AbCdEfGh-llave-completa-sintetica", webhookSecret: "secreto-webhook-sintetico" }),
    );
    const { container } = render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await user.click(obtener());
    await screen.findByText("Llave configurada · prefijo AbCdEfGh…");
    expect(container.innerHTML).not.toContain("llave-completa-sintetica");
    expect(container.innerHTML).not.toContain("secreto-webhook-sintetico");
    expect(setItem).not.toHaveBeenCalled();
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
    setItem.mockRestore();
  });

  it("mientras obtiene, la captura manual no se puede usar, y mientras guarda manual no se puede obtener", async () => {
    const user = userEvent.setup();
    let resolver: (v: unknown) => void = () => {};
    obtenerCredencialImpuestoDiagnostico.mockReturnValue(new Promise((r) => (resolver = r)));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(obtener());
    expect(screen.getByRole("button", { name: "Configurar" })).toBeDisabled();
    resolver(respuesta());
    await screen.findByText("Llave configurada · prefijo AbCdEfGh…");

    let resolverPut: () => void = () => {};
    setTenantProductCredential.mockReturnValue(new Promise<void>((r) => (resolverPut = r)));
    // Tras obtener, la llave ya está configurada (estado efectivo, M-3): la captura manual ofrece «Reemplazar».
    await user.click(screen.getByRole("button", { name: "Reemplazar" }));
    await user.click(campo());
    await user.paste(LLAVE);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(obtener()).toBeDisabled();
    resolverPut();
    await screen.findByText("API key de impuesto guardada.");
  });

  // M-3 (code review): el alta y la relectura del listado son pasos distintos; un fallo de la relectura no niega el alta.
  it("si la relectura del listado falla tras obtener, el éxito se mantiene con un aviso no alarmante y sin role=alert", async () => {
    const user = userEvent.setup();
    onChanged.mockRejectedValue(new Error("fallo de red al releer"));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await user.click(obtener());
    expect(
      await screen.findByText("Llave obtenida del proveedor. No se pudo actualizar el listado; recarga la página para verlo."),
    ).toHaveAttribute("role", "status");
    expect(screen.getByText("Llave configurada · prefijo AbCdEfGh…")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByText(/No se pudo obtener la llave/)).toBeNull();
    expect(obtener()).not.toHaveAttribute("aria-busy");
  });

  it("el chip dice «Configurada» tras obtener aunque el listado aún no se haya refrescado", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    expect(screen.getByText("No configurada")).toBeInTheDocument();
    await user.click(obtener());
    await screen.findByText("Llave configurada · prefijo AbCdEfGh…");
    expect(screen.getByText("Configurada")).toBeInTheDocument();
    expect(screen.queryByText("No configurada")).toBeNull();
  });
});

// M-3 (code review, ampliado): la relectura va aparte también al guardar y al borrar a mano, y la captura manual usa el
// estado efectivo de la llave (listado o alta recién hecha).
// Uso de ejemplo: <CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={releeQueFalla} />
describe("CredencialImpuestoDiagnostico · relectura aparte y estado efectivo (M-3)", () => {
  let onChanged: Mock<() => Promise<void>>;
  const AVISO = "No se pudo actualizar el listado; recarga la página para verlo.";
  beforeEach(() => {
    setTenantProductCredential.mockReset().mockResolvedValue(undefined);
    deleteTenantProductCredential.mockReset().mockResolvedValue(undefined);
    obtenerCredencialImpuestoDiagnostico.mockReset().mockResolvedValue({
      apiKeyPrefix: "AbCdEfGh",
      created: true,
      rotated: false,
      hasWebhookSecret: true,
      changed: true,
      updatedAt: "2026-11-25T10:00:00Z",
    });
    onChanged = vi.fn().mockResolvedValue(undefined);
  });
  const obtener = () => screen.getByRole("button", { name: "Obtener del proveedor" });

  it("si la relectura falla tras guardar, el éxito se mantiene con el aviso y sin role=alert", async () => {
    const user = userEvent.setup();
    onChanged.mockRejectedValue(new Error("fallo de red al releer"));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste(LLAVE);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(await screen.findByText(`API key de impuesto guardada. ${AVISO}`)).toHaveAttribute("role", "status");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByText(/No se pudo guardar/)).toBeNull();
  });

  it("si la relectura falla tras borrar, el éxito se mantiene con el aviso y sin role=alert", async () => {
    const user = userEvent.setup();
    onChanged.mockRejectedValue(new Error("fallo de red al releer"));
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Borrar" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Borrar API key" }));
    expect(await screen.findByText(`API key de impuesto borrada. ${AVISO}`)).toHaveAttribute("role", "status");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByText(/No se pudo borrar/)).toBeNull();
  });

  it("tras obtener, sin refresco del listado, la captura manual ofrece «Reemplazar» y «Borrar»", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await user.click(obtener());
    await screen.findByText("Llave configurada · prefijo AbCdEfGh…");
    await expandirManual();
    expect(screen.getByRole("button", { name: "Reemplazar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Borrar" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Configurar" })).toBeNull();
  });

  it("borrar lo obtenido deja la llave como no configurada aunque el listado no se refresque", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    await user.click(obtener());
    await screen.findByText("Llave configurada · prefijo AbCdEfGh…");
    await expandirManual();
    await user.click(screen.getByRole("button", { name: "Borrar" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Borrar API key" }));
    await screen.findByText("API key de impuesto borrada.");
    expect(screen.getByText("No configurada")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Configurar" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Borrar" })).toBeNull();
    expect(screen.queryByText(/prefijo/)).toBeNull();
  });
});
