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
vi.mock("@/lib/api/platform", () => ({
  setTenantProductCredential: (...a: unknown[]) => setTenantProductCredential(...a),
  deleteTenantProductCredential: (...a: unknown[]) => deleteTenantProductCredential(...a),
}));

const LLAVE = "llave-sintetica-0123456789";
const MSG_LONGITUD = "La API key debe tener entre 16 y 256 caracteres, sin espacios.";
const campo = () => screen.getByLabelText("API key de impuesto vehicular");

describe("CredencialImpuestoDiagnostico", () => {
  let onChanged: Mock<() => Promise<void>>;
  beforeEach(() => {
    setTenantProductCredential.mockReset().mockResolvedValue(undefined);
    deleteTenantProductCredential.mockReset().mockResolvedValue(undefined);
    onChanged = vi.fn().mockResolvedValue(undefined);
  });

  it("muestra «No configurada» con chip de texto y sin formulario abierto", () => {
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
    expect(screen.getByText("No configurada")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Configurar" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Borrar" })).toBeNull();
    expect(screen.queryByLabelText("API key de impuesto vehicular")).toBeNull();
  });

  it("muestra «Configurada» con Reemplazar y Borrar, y nunca un valor", () => {
    const { container } = render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
    expect(screen.getByText("Configurada")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reemplazar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Borrar" })).toBeInTheDocument();
    expect(container.querySelector("input")).toBeNull();
  });

  it("el campo es password, sin autocompletar ni corrector, vacío y con la ayuda de opcional", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada onChanged={onChanged} />);
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
    await user.click(screen.getByRole("button", { name: "Configurar" }));
    await user.click(campo());
    await user.paste(LLAVE);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("La empresa no existe.");
  });

  it("cancelar cierra y vacía lo escrito", async () => {
    const user = userEvent.setup();
    render(<CredencialImpuestoDiagnostico tenantId="t1" configurada={false} onChanged={onChanged} />);
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
    await user.click(screen.getByRole("button", { name: "Borrar" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Borrar API key" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("La empresa no existe.");
    expect(onChanged).not.toHaveBeenCalled();
  });
});
