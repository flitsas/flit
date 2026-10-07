import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PasoDatos } from "@/app/verificacion/[token]/_components/PasoDatos";
import { createHttpClient, createMockClient, getManualCaptureClient } from "@/lib/captura-manual/client";
import { RENDERED_CONSENT_TEXT_VERSION } from "@/lib/captura-manual/consent";
import { ManualCaptureError, terminalKindOf, type ManualCaptureClient } from "@/lib/captura-manual/types";

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

afterEach(() => vi.unstubAllEnvs());

describe("cliente por defecto", () => {
  it("sin variable es el HTTP real: hace fetch a la ruta pública", async () => {
    vi.stubEnv("NEXT_PUBLIC_MANUAL_CAPTURE_MOCK", "");
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(json(404, { code: "not_found", message: "x" }));
    await expect(getManualCaptureClient().getManualCapture("abc")).rejects.toBeInstanceOf(ManualCaptureError);
    expect(String(fetchSpy.mock.calls[0][0])).toContain("/api/v1/public/manual-capture/abc");
    fetchSpy.mockRestore();
  });

  it("«false» tampoco activa el simulado; solo «true»", async () => {
    vi.stubEnv("NEXT_PUBLIC_MANUAL_CAPTURE_MOCK", "false");
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(json(404, { code: "not_found", message: "x" }));
    await getManualCaptureClient()
      .getManualCapture("abc")
      .catch(() => undefined);
    expect(fetchSpy).toHaveBeenCalled();
    fetchSpy.mockRestore();
  });

  it("con NEXT_PUBLIC_MANUAL_CAPTURE_MOCK=true usa el simulado (sin fetch)", async () => {
    vi.stubEnv("NEXT_PUBLIC_MANUAL_CAPTURE_MOCK", "true");
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const view = await getManualCaptureClient().getManualCapture("ok");
    expect(view.consentTextVersion).toBe(RENDERED_CONSENT_TEXT_VERSION);
    expect(fetchSpy).not.toHaveBeenCalled();
    fetchSpy.mockRestore();
  });

  it("el simulado conserva los tokens mágicos", async () => {
    const c = createMockClient(0);
    await expect(c.getManualCapture("vencido")).rejects.toMatchObject({ status: 410, code: "expirada" });
    await expect(c.getManualCapture("reemplazado")).rejects.toMatchObject({ status: 410, code: "reemplazado" });
    await expect(c.getManualCapture("usado")).rejects.toMatchObject({ status: 409, code: "estado_invalido" });
    await expect(c.getManualCapture("invalido")).rejects.toMatchObject({ status: 404 });
    await expect(c.getManualCapture("red")).rejects.toBeInstanceOf(TypeError);
    await expect(c.postConsent("consent-falla", { accepted: true, textVersion: "v" })).rejects.toMatchObject({
      status: 500,
    });
  });
});

describe("cliente HTTP real: contrato del backend", () => {
  function setup(res: Response) {
    const fetchImpl = vi.fn().mockResolvedValue(res);
    return { fetchImpl, client: createHttpClient(fetchImpl as unknown as typeof fetch) };
  }
  const files = () => {
    const b = (t: string) => new Blob(["x"], { type: t });
    return { rostro: b("image/jpeg"), anverso: b("image/jpeg"), reverso: b("image/jpeg"), firma: b("image/png") };
  };
  const headerNames = (init: { headers?: Record<string, string> }) =>
    Object.keys(init.headers ?? {}).map((h) => h.toLowerCase());

  it("GET con productName null y sin cabeceras de auth/tenant", async () => {
    const { fetchImpl, client } = setup(
      json(200, {
        fullName: "P",
        documentType: "CC",
        documentNumber: "1",
        productName: null,
        expiresAt: "2030-01-01T00:00:00+00:00",
        consentTextVersion: "manual-ley1581-v2",
      }),
    );
    const view = await client.getManualCapture("a b/c");
    expect(view.productName).toBeNull();
    const [url, init] = fetchImpl.mock.calls[0];
    expect(String(url)).toContain("/api/v1/public/manual-capture/a%20b%2Fc");
    expect(headerNames(init)).not.toContain("authorization");
    expect(headerNames(init)).not.toContain("x-tenant-id");
  });

  it("POST consent: método, ruta, JSON {accepted,textVersion}, sin auth", async () => {
    const { fetchImpl, client } = setup(new Response(null, { status: 204 }));
    await client.postConsent("tok", { accepted: true, textVersion: "manual-ley1581-v2" });
    const [url, init] = fetchImpl.mock.calls[0];
    expect(String(url)).toMatch(/\/api\/v1\/public\/manual-capture\/tok\/consent$/);
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({ accepted: true, textVersion: "manual-ley1581-v2" });
    expect(init.headers["Content-Type"]).toBe("application/json");
    expect(headerNames(init)).not.toContain("authorization");
    expect(headerNames(init)).not.toContain("x-tenant-id");
  });

  it("POST submit: ruta /submit, multipart sin Content-Type manual ni auth", async () => {
    const { fetchImpl, client } = setup(json(200, { status: "pendiente_revision_manual" }));
    await expect(client.submit("tok", files())).resolves.toEqual({ status: "pendiente_revision_manual" });
    const [url, init] = fetchImpl.mock.calls[0];
    expect(String(url)).toMatch(/\/api\/v1\/public\/manual-capture\/tok\/submit$/);
    expect(init.method).toBe("POST");
    expect(init.body).toBeInstanceOf(FormData);
    expect(headerNames(init)).not.toContain("content-type");
    expect(headerNames(init)).not.toContain("authorization");
    expect(headerNames(init)).not.toContain("x-tenant-id");
  });

  it.each([
    [400, "version_texto_invalida", "La versión del texto de consentimiento no es la vigente."],
    [404, "not_found", "Enlace de captura no encontrado."],
    [409, "consentimiento_requerido", "Falta aceptar el consentimiento."],
    [409, "estado_invalido", "El enlace ya no admite capturas."],
    [410, "expirada", "El enlace de captura expiró."],
    [413, "archivo_demasiado_grande", "Una imagen supera el tamaño máximo permitido."],
    [415, "tipo_no_soportado", "Formato de imagen no admitido (JPEG, PNG o WebP; la firma, PNG)."],
    [422, "archivo_requerido", "Faltan imágenes: se requieren rostro, anverso y reverso."],
    [422, "firma_requerida", "La firma es obligatoria."],
  ])("parsea el cuerpo {code,message} del backend: %s %s", async (status, code, message) => {
    const { client } = setup(json(status, { code, message }));
    await expect(client.submit("tok", files())).rejects.toMatchObject({ status, code, message });
  });

  it("un error sin cuerpo JSON (p. ej. 413 del proxy) se convierte en ManualCaptureError genérico", async () => {
    const { client } = setup(new Response("<html>too large</html>", { status: 413 }));
    await expect(client.submit("tok", files())).rejects.toMatchObject({ status: 413, code: "error" });
  });
});

describe("terminalKindOf discrimina el 409 por code", () => {
  it.each([
    [404, "not_found", "not_found"],
    [410, "expirada", "expirada"],
    [410, "reemplazado", "reemplazado"],
    [409, "estado_invalido", "estado_invalido"],
    [409, "consentimiento_requerido", null],
    [409, "otro", null],
    [400, "version_texto_invalida", null],
    [413, "archivo_demasiado_grande", null],
    [415, "tipo_no_soportado", null],
    [422, "firma_requerida", null],
    [500, "error", null],
  ])("%s %s -> %s", (status, code, expected) => {
    expect(terminalKindOf(new ManualCaptureError(status, code, "x"))).toBe(expected);
  });

  it("un error que no es ManualCaptureError no es terminal", () => {
    expect(terminalKindOf(new TypeError("Failed to fetch"))).toBeNull();
  });
});

describe("PasoDatos ante errores del consentimiento", () => {
  const view = {
    fullName: "P",
    documentType: "CC",
    documentNumber: "1",
    productName: null,
    expiresAt: "2030-01-01T00:00:00Z",
    consentTextVersion: RENDERED_CONSENT_TEXT_VERSION,
  };
  function run(err: Error) {
    const client: ManualCaptureClient = {
      getManualCapture: vi.fn(),
      postConsent: vi.fn().mockRejectedValue(err),
      submit: vi.fn(),
    };
    const onTerminal = vi.fn();
    const onDone = vi.fn();
    render(<PasoDatos token="t" view={view} client={client} onDone={onDone} onTerminal={onTerminal} />);
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Iniciar verificación" }));
    return { onTerminal, onDone };
  }

  it("409 estado_invalido en el consentimiento lleva a la pantalla terminal", async () => {
    const { onTerminal, onDone } = run(new ManualCaptureError(409, "estado_invalido", "x"));
    await waitFor(() => expect(onTerminal).toHaveBeenCalledWith("estado_invalido"));
    expect(onDone).not.toHaveBeenCalled();
  });

  it("400 version_texto_invalida pide recargar y bloquea el botón", async () => {
    run(new ManualCaptureError(400, "version_texto_invalida", "x"));
    expect(await screen.findByRole("alert")).toHaveTextContent("recarga la página");
    expect(screen.getByRole("button", { name: "Iniciar verificación" })).toBeDisabled();
  });
});
