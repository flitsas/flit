import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { DrFlitAssistant } from "../DrFlitAssistant";
import {
  applyContinueSupportCase,
  applyOpenSupportCase,
  createInitialState,
  resetMessageIdSeq,
} from "../dr-flit-conversation";
import type { DrFlitChatResponse, DrFlitSupportCaseDraft } from "../dr-flit-chat-types";
import { clearDrFlitSession, DR_FLIT_SESSION_STORAGE_KEY } from "../dr-flit-session-store";
import {
  createSupportDraft,
  formatBogotaDate,
  resolveAffectedModule,
  toSupportCaseRequest,
  validateAttachmentFile,
  validateSupportDraft,
} from "../dr-flit-support-case";

vi.mock("@/lib/api/dr-flit-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/dr-flit-client")>("@/lib/api/dr-flit-client");
  return { ...actual, postDrFlitChat: vi.fn(), uploadSupportAttachment: vi.fn(), createSupportCase: vi.fn() };
});

import { createSupportCase, postDrFlitChat, uploadSupportAttachment } from "@/lib/api/dr-flit-client";

/**
 * HU #12929 — formulario del caso de soporte con prellenado y adjuntos.
 *
 * Uso de ejemplo:
 *   const draft = createSupportDraft({ name, email, company }, resolveAffectedModule(routeScope));
 *   validateSupportDraft(draft) // {} ⇒ se puede continuar al resumen
 */

function complete(overrides: Partial<DrFlitSupportCaseDraft> = {}): DrFlitSupportCaseDraft {
  return {
    ...createSupportDraft({ name: "Ana Prueba", email: "ana.prueba@example.test", company: "Empresa Demo" }, null, new Date("2026-09-25T15:00:00Z")),
    detalle: "No me deja subir la factura",
    resultadoEsperado: "Que la factura quede cargada",
    titulo: "Error al subir factura",
    frecuencia: "siempre",
    prioridad: "Alta",
    ...overrides,
  };
}

function fakeJwt(payload: Record<string, unknown>): string {
  const enc = (o: unknown) => Buffer.from(JSON.stringify(o)).toString("base64url");
  return `${enc({ alg: "none", typ: "JWT" })}.${enc(payload)}.`;
}

const soporte: DrFlitChatResponse = {
  status: "ok",
  intent: "soporte",
  reply: "Lamento el problema, te ayudo a radicar un caso.",
  citations: [],
  suggestGestionIntent: null,
  usage: { messagesUsedToday: 1, dailyLimit: 30 },
};

describe("HU #12929 — lógica del borrador", () => {
  beforeEach(() => resetMessageIdSeq());

  it("AC1 — prellena nombre, correo y compañía y fija la fecha en hora Colombia", () => {
    const draft = createSupportDraft(
      { name: " Ana Prueba ", email: "ana.prueba@example.test", company: "Empresa Demo" },
      "Usuarios",
      new Date("2026-09-26T02:00:00Z"), // 25/09 21:00 en Bogotá
    );

    expect(draft).toMatchObject({
      nombre: "Ana Prueba",
      email: "ana.prueba@example.test",
      compania: "Empresa Demo",
      fecha: "25/09/2026",
      detalle: "",
      frecuencia: "",
      prioridad: "",
      adjuntar: null,
      attachments: [],
      affectedModule: "Usuarios",
    });
    expect(formatBogotaDate(new Date("2026-09-26T02:00:00Z"))).toBe("25/09/2026");
  });

  it("AC2 — marca los requeridos faltantes", () => {
    const errors = validateSupportDraft(createSupportDraft({}, null));

    expect(Object.keys(errors).sort()).toEqual(
      ["compania", "detalle", "email", "frecuencia", "nombre", "prioridad", "resultadoEsperado", "titulo"].sort(),
    );
    expect(validateSupportDraft(complete())).toEqual({});
    expect(validateSupportDraft(complete({ email: "no-es-correo" })).email).toBe("El correo no es válido.");
    expect(validateSupportDraft(complete({ titulo: "x".repeat(201) })).titulo).toMatch(/200/);
  });

  it("AC2 — con faltantes no avanza al resumen; completo, sí", () => {
    const open = applyOpenSupportCase(createInitialState(), createSupportDraft({}, null));

    expect(open.phase).toBe("collecting_support_case");
    expect(applyContinueSupportCase(open).phase).toBe("collecting_support_case");
    expect(applyContinueSupportCase({ ...open, supportDraft: complete() }).phase).toBe("confirming_support_case");
  });

  it.each([
    ["captura.png", 1024, 0, null],
    ["foto.JPEG", 1024, 4, null],
    ["reporte.pdf", 20 * 1024 * 1024, 0, null],
    ["programa.exe", 10, 0, /no es un tipo permitido/],
    ["sin-extension", 10, 0, /no es un tipo permitido/],
    ["enorme.pdf", 20 * 1024 * 1024 + 1, 0, /pesa más de 20 MB/],
    ["vacio.png", 0, 0, /está vacío/],
    ["sexto.png", 10, 5, /máximo 5 archivos/],
  ])("AC4 — %s (%i bytes, %i adjuntos previos)", (name, size, count, expected) => {
    const result = validateAttachmentFile({ name, size }, count);
    if (expected === null) expect(result).toBeNull();
    else expect(result).toMatch(expected);
  });

  it("módulo afectado: mejor esfuerzo por el módulo activo; sin coincidencia lo decide el backend", () => {
    expect(resolveAffectedModule("/|validaciones")).toBe("Validación de Identidad");
    expect(resolveAffectedModule("/|reportes-detallados")).toBe("Reportes");
    expect(resolveAffectedModule("/admin/companies|admin-companies")).toBe("Administradores");
    expect(resolveAffectedModule("/|tramites")).toBeNull();
    expect(resolveAffectedModule(undefined)).toBeNull();
  });

  it("el cuerpo del POST solo lleva lo confirmado: sin adjuntos si respondió que no", () => {
    const draft = complete({
      telefono: "  ",
      adjuntar: false,
      attachments: [{ id: "a1", filename: "a.png", sizeBytes: 10 }],
    });

    expect(toSupportCaseRequest(draft)).toEqual({
      nombre: "Ana Prueba",
      email: "ana.prueba@example.test",
      telefono: null,
      compania: "Empresa Demo",
      detalle: "No me deja subir la factura",
      resultadoEsperado: "Que la factura quede cargada",
      frecuencia: "siempre",
      titulo: "Error al subir factura",
      prioridad: "Alta",
      affectedModule: null,
      attachmentIds: [],
    });
    expect(toSupportCaseRequest({ ...draft, adjuntar: true }).attachmentIds).toEqual(["a1"]);
  });
});

describe("HU #12929 — formulario en el panel", () => {
  beforeEach(() => {
    vi.stubGlobal("open", vi.fn());
    vi.mocked(postDrFlitChat).mockReset().mockResolvedValue(soporte);
    vi.mocked(uploadSupportAttachment).mockReset();
    vi.mocked(createSupportCase).mockReset();
    window.localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt({ sub: "u1", tenant_id: "t1", tenant_name: "Empresa Demo S.A.S" }));
    clearDrFlitSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
    clearDrFlitSession();
  });

  async function openForm() {
    // applyAccept: false — el input declara accept, pero aquí se prueba el rechazo del propio formulario.
    const user = userEvent.setup({ applyAccept: false });
    render(
      <DrFlitAssistant
        displayName="Ana"
        routeScope="/|validaciones"
        supportContact={{ name: "Ana Prueba", email: "ana.prueba@example.test" }}
      />,
    );
    await user.click(screen.getByRole("button", { name: "Abrir DR. FLIT" }));
    await user.type(screen.getByRole("textbox"), "no me deja subir la factura{enter}");
    const form = await screen.findByRole("form", { name: "Formulario del caso de soporte" });
    return { user, form };
  }

  it("AC1 — la intención soporte abre el formulario con nombre, correo, compañía y fecha", async () => {
    const { form } = await openForm();

    expect(screen.getByText("Lamento el problema, te ayudo a radicar un caso.")).toBeInTheDocument();
    expect(within(form).getByLabelText(/^Nombre/)).toHaveValue("Ana Prueba");
    expect(within(form).getByLabelText(/^Correo/)).toHaveValue("ana.prueba@example.test");
    expect(within(form).getByLabelText(/^Compañía/)).toHaveValue("Empresa Demo S.A.S");
    expect(within(form).getByText(`Caso de soporte · ${formatBogotaDate()}`)).toBeInTheDocument();
  });

  it("AC2 — sin los requeridos no avanza, marca los campos y no llama a ningún endpoint", async () => {
    const { user, form } = await openForm();

    await user.click(within(form).getByRole("button", { name: "Continuar" }));

    expect(within(form).getByRole("alert")).toHaveTextContent("Revisa los campos marcados");
    expect(within(form).getByLabelText(/^Título del caso/)).toHaveAttribute("aria-invalid", "true");
    expect(within(form).getByLabelText(/^Detalle del error/)).toHaveAttribute("aria-invalid", "true");
    expect(within(form).getByLabelText(/^Resultado esperado/)).toHaveAttribute("aria-invalid", "true");
    expect(within(form).getByText("Elige la prioridad.")).toBeInTheDocument();
    expect(within(form).getByLabelText(/^Título del caso/)).toHaveFocus();
    expect(screen.getByRole("form", { name: "Formulario del caso de soporte" })).toBeInTheDocument();
    expect(uploadSupportAttachment).not.toHaveBeenCalled();
    expect(createSupportCase).not.toHaveBeenCalled();
  });

  it("AC3 — sube los adjuntos y en sessionStorage solo queda su id", async () => {
    vi.mocked(uploadSupportAttachment).mockResolvedValue({ id: "0199a000-0000-7000-8000-00000000f001", filename: "captura.png", sizeBytes: 2048 });
    const { user, form } = await openForm();

    await user.click(within(form).getByText("Sí"));
    const png = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47, 1, 2, 3])], "captura.png", { type: "image/png" });
    await user.upload(within(form).getByLabelText("Elegir archivos"), png);

    const list = await within(form).findByRole("list", { name: "Archivos adjuntos" });
    expect(within(list).getByText("captura.png")).toBeInTheDocument();
    expect(uploadSupportAttachment).toHaveBeenCalledWith(png);
    await waitFor(() => {
      const stored = window.sessionStorage.getItem(DR_FLIT_SESSION_STORAGE_KEY) ?? "";
      expect(stored).toContain("0199a000-0000-7000-8000-00000000f001");
    });
    const stored = window.sessionStorage.getItem(DR_FLIT_SESSION_STORAGE_KEY) ?? "";
    expect(stored).not.toMatch(/base64|data:image/);

    await user.click(within(list).getByRole("button", { name: "Quitar captura.png" }));
    expect(within(form).queryByRole("list", { name: "Archivos adjuntos" })).not.toBeInTheDocument();
  });

  it("AC4 — rechaza localmente un tipo no permitido o un archivo muy grande sin llamar al backend", async () => {
    const { user, form } = await openForm();
    await user.click(within(form).getByText("Sí"));
    const input = within(form).getByLabelText("Elegir archivos");

    const big = new File(["x"], "enorme.pdf", { type: "application/pdf" });
    Object.defineProperty(big, "size", { value: 20 * 1024 * 1024 + 1 });
    await user.upload(input, big);
    expect(await within(form).findByRole("alert")).toHaveTextContent("«enorme.pdf» pesa más de 20 MB.");

    const exe = new File(["MZ"], "programa.exe", { type: "application/octet-stream" });
    await user.upload(input, exe);
    await waitFor(() =>
      expect(within(form).getByRole("alert")).toHaveTextContent("«programa.exe» no es un tipo permitido."),
    );

    expect(uploadSupportAttachment).not.toHaveBeenCalled();
  });

  it("AC4 — si el backend rechaza el archivo, el formulario muestra su motivo", async () => {
    vi.mocked(uploadSupportAttachment).mockRejectedValue(new Error("El contenido del archivo no corresponde a su tipo."));
    const { user, form } = await openForm();
    await user.click(within(form).getByText("Sí"));

    await user.upload(within(form).getByLabelText("Elegir archivos"), new File(["no-es-png"], "falso.png", { type: "image/png" }));

    expect(await within(form).findByRole("alert")).toHaveTextContent("El contenido del archivo no corresponde a su tipo.");
    expect(within(form).queryByRole("list", { name: "Archivos adjuntos" })).not.toBeInTheDocument();
  });

  it("Cancelar descarta el borrador y vuelve al menú", async () => {
    const { user, form } = await openForm();

    await user.click(within(form).getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByRole("form", { name: "Formulario del caso de soporte" })).not.toBeInTheDocument();
    expect(screen.getByText(/no radiqué ningún caso/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Buscar por placa/i })).toBeInTheDocument();
  });
});
