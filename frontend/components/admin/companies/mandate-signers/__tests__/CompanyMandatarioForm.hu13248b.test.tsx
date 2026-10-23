// HU #13248b (F9 #13245) — rediseño del formulario del mandatario: modal normal (el de los demás módulos) en pasos numerados que
// solo muestra lo que aplica, resumen fijo al guardar, selector de organismos (chips o buscador) y errores
// por campo sin perder lo escrito. Un solo formulario para compañía, Super Admin y hub OT.

import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiValidationError } from "@/lib/api/types";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";

vi.mock("@/lib/api/admin-mandate-signers", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-mandate-signers")>()),
  fetchCompanyAssociableCompanies: vi
    .fn()
    .mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100, aplicaSoloASuCompania: true }),
  fetchOtAssociableCompanies: vi
    .fn()
    .mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100, aplicaSoloASuCompania: false }),
}));
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

import { CompanyMandatarioForm } from "../CompanyMandatarioForm";

const OFICINAS = [{ transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" }];
const MUCHAS_OFICINAS = Array.from({ length: 12 }, (_, i) => ({
  transitOfficeId: `ot-${i + 1}`,
  code: `0500${i + 1}`,
  name: `Organismo ${String(i + 1).padStart(2, "0")}`,
}));

function signer(overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: "ms-1",
    transitOfficeId: "ot-1",
    fullName: "Ana Restrepo",
    documentType: "CC",
    documentNumber: "1020304050",
    integrityHash: "a".repeat(64),
    email: "ana@ejemplo.com",
    userId: null,
    identityStatus: "pending",
    signatureVaultId: null,
    registeredAt: "2026-08-01T10:00:00Z",
    isActive: true,
    companyTenantIds: ["t-1"],
    transitOfficeIds: ["ot-1"],
    signerModel: "natural",
    signatureMethod: "biometria",
    ...overrides,
  };
}

const onSubmit = vi.fn();
function renderForm(props: Partial<React.ComponentProps<typeof CompanyMandatarioForm>> = {}) {
  return render(
    <CompanyMandatarioForm
      tenantId="t-1"
      offices={OFICINAS}
      editing={null}
      initialOfficeIds={["ot-1"]}
      onCancel={vi.fn()}
      onSubmit={onSubmit}
      {...props}
    />,
  );
}
const radio = (name: string) => screen.getByRole("radio", { name });
const resumen = () => screen.getByTestId("mandatario-resumen");
const titulosDePasos = () => screen.getAllByRole("heading", { level: 3 }).map((h) => h.textContent);

beforeEach(() => {
  vi.clearAllMocks();
  onSubmit.mockResolvedValue({ id: "ms-2", integrityHash: "b".repeat(64) });
});

describe("HU #13248b AC1 — modal en pasos numerados", () => {
  it("es un modal normal centrado (componente Modal) con el título del alta", () => {
    renderForm();
    const dialogo = screen.getByRole("dialog", { name: "Registrar mandatario" });
    expect(dialogo.tagName).not.toBe("ASIDE");
    expect(dialogo.className).toMatch(/items-center justify-center/);
  });

  it("Persona natural: ¿Quién firma? · Datos · ¿Cómo firma? · Vigencia · Dónde aplica, numerados", () => {
    renderForm();
    expect(titulosDePasos()).toEqual(["1¿Quién firma?", "2Datos", "3¿Cómo firma?", "4Vigencia", "5Dónde aplica"]);
  });

  it("las tres opciones de ¿Quién firma? son tarjetas con una línea de ayuda", () => {
    renderForm();
    const grupo = screen.getByRole("radiogroup", { name: "Modelo del mandatario" });
    expect(within(grupo).getAllByRole("radio")).toHaveLength(3);
    expect(within(grupo).getByText("Firma con su identidad")).toBeInTheDocument();
    expect(within(grupo).getByText("La entidad, con su NIT")).toBeInTheDocument();
    expect(within(grupo).getByText("El PDF sin firma")).toBeInTheDocument();
  });

  it("Persona jurídica solo pide entidad y NIT; salta ¿Cómo firma? y Vigencia y renumera", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Persona jurídica"));
    expect(titulosDePasos()).toEqual(["1¿Quién firma?", "2Datos", "3Dónde aplica"]);
    expect(screen.getByLabelText("Nombre de la entidad")).toBeInTheDocument();
    expect(screen.getByLabelText("NIT")).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Correo/)).not.toBeInTheDocument();
    expect(screen.queryByRole("radiogroup", { name: "Forma de firma" })).not.toBeInTheDocument();
  });

  it("Formato en blanco no pide datos", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Formato en blanco"));
    expect(screen.getByTestId("mandatario-formato-blanco-nota")).toBeInTheDocument();
    expect(screen.queryByLabelText("Nombre completo")).not.toBeInTheDocument();
    expect(titulosDePasos()).toEqual(["1¿Quién firma?", "2Datos", "3Dónde aplica"]);
  });

  it("Vigencia es un segmentado Fija | Rango; el rango muestra las dos fechas en línea", async () => {
    const user = userEvent.setup();
    renderForm();
    const grupo = screen.getByRole("radiogroup", { name: "Vigencia" });
    expect(within(grupo).getAllByRole("radio")).toHaveLength(2);
    expect(radio("Fija")).toBeChecked();
    await user.click(radio("Rango de fechas"));
    const inicio = screen.getByLabelText("Fecha de inicio");
    const fin = screen.getByLabelText("Fecha de fin");
    expect(inicio.closest(".grid")).toBe(fin.closest(".grid"));
  });

  it("el correo explica «aquí le llega el enlace» con Validación de identidad", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Validación de identidad"));
    expect(screen.getByText(/Aquí le llega el enlace/)).toBeInTheDocument();
    expect(screen.getByLabelText("Correo (obligatorio)")).toBeRequired();
  });

  it("la consecuencia nombra el correo escrito: «le enviaremos el enlace a …»", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.type(screen.getByLabelText(/^Correo/), "ana@ejemplo.com");
    await user.click(radio("Validación de identidad"));
    expect(screen.getByTestId("mandatario-aviso-validacion")).toHaveTextContent(
      "Al guardar le enviaremos el enlace a ana@ejemplo.com.",
    );
  });

  it("al editar, el estado de la validación propia y «Reenviar validación» van en ¿Cómo firma?", () => {
    renderForm({ editing: signer(), onResend: vi.fn() });
    const paso = screen.getByRole("heading", { name: /¿Cómo firma\?/ }).closest("section")!;
    expect(within(paso).getByTestId("mandatario-validacion-estado")).toHaveTextContent("En curso");
    expect(within(paso).getByRole("button", { name: /Reenviar validación/ })).toBeInTheDocument();
  });

  it("Escape cierra el panel", async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    renderForm({ onCancel });
    await user.keyboard("{Escape}");
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});

describe("HU #13248b AC1 — pie fijo con el resumen de lo que pasará al guardar", () => {
  it("alta con Validación de identidad: modelo · forma · vigencia · organismos · se enviará la validación", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Validación de identidad"));
    expect(resumen()).toHaveTextContent(
      "Persona natural · Validación de identidad · Vigencia fija · 1 organismo · se enviará la validación",
    );
  });

  it("con Baúl de firmas no promete ninguna validación", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Baúl de firmas"));
    expect(resumen()).toHaveTextContent("Persona natural · Baúl de firmas · Vigencia fija · 1 organismo");
    expect(resumen()).not.toHaveTextContent(/validación$/);
  });

  it("cambiar el documento: «la validación anterior deja de contar»", async () => {
    const user = userEvent.setup();
    renderForm({ editing: signer() });
    const doc = screen.getByLabelText("Número de documento");
    await user.clear(doc);
    await user.type(doc, "99999");
    expect(resumen()).toHaveTextContent("la validación anterior deja de contar");
  });

  it("pasar de Baúl a Validación de identidad: «pasará a validación de identidad»", async () => {
    const user = userEvent.setup();
    renderForm({ editing: signer({ signatureMethod: "baul", signatureVaultId: "sig-1" }) });
    await user.click(radio("Validación de identidad"));
    expect(resumen()).toHaveTextContent("pasará a validación de identidad");
  });

  it("Persona jurídica: el resumen no habla de forma de firma ni de vigencia", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Persona jurídica"));
    expect(resumen()).toHaveTextContent("Persona jurídica · 1 organismo");
    expect(resumen()).not.toHaveTextContent(/Vigencia/);
  });

  it("Cancelar y Guardar están en el pie del panel", () => {
    renderForm();
    expect(screen.getByRole("button", { name: "Cancelar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Guardar" })).toBeInTheDocument();
  });
});

describe("HU #13248b AC2 — organismos", () => {
  it("con 8 o menos organismos se muestran como chips conmutables, sin buscador", async () => {
    const user = userEvent.setup();
    renderForm({ offices: MUCHAS_OFICINAS.slice(0, 8), initialOfficeIds: [] });
    expect(screen.queryByLabelText("Buscar organismo por nombre o código")).not.toBeInTheDocument();
    await user.click(screen.getByRole("checkbox", { name: "Organismo 03" }));
    expect(screen.getByRole("checkbox", { name: "Organismo 03" })).toBeChecked();
    expect(screen.getByText("1 seleccionado")).toBeInTheDocument();
  });

  it("con más de 8 usa el selector con buscador y lista completa (sin paginación)", async () => {
    const user = userEvent.setup();
    renderForm({ offices: MUCHAS_OFICINAS, initialOfficeIds: [] });
    const lista = screen.getByTestId("mandatario-organismos-lista");
    expect(within(lista).getAllByRole("checkbox")).toHaveLength(12);
    await user.type(screen.getByLabelText("Buscar organismo por nombre o código"), "Organismo 1");
    // 10, 11 y 12 (y no 01..09).
    expect(within(lista).getAllByRole("checkbox")).toHaveLength(3);
    await user.click(screen.getByRole("button", { name: "Seleccionar las filtradas" }));
    expect(screen.getByText("3 seleccionados")).toBeInTheDocument();
  });

  it("sin organismo no guarda y lo dice junto al campo", async () => {
    const user = userEvent.setup();
    renderForm({ initialOfficeIds: [] });
    await user.type(screen.getByLabelText("Nombre completo"), "Ana");
    await user.type(screen.getByLabelText("Número de documento"), "1");
    await user.click(radio("Baúl de firmas"));
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByText(/Elige al menos un organismo/)).toBeInTheDocument();
  });
});

describe("HU #13248b AC1 — variantes", () => {
  it("hub OT: mismo formulario, organismo fijo y compañías asociadas con selector del OT", async () => {
    renderForm({
      variant: "hub",
      tenantId: undefined,
      restrictToOfficeIds: ["ot-1"],
      ownerCompanyIds: ["t-propia"],
    });
    expect(screen.getByText("El mandatario se registra en este organismo.")).toBeInTheDocument();
    expect(await screen.findByTestId("mandatario-asociadas-ot")).toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(radio("Baúl de firmas"));
    expect(screen.getByTestId("mandatario-hub-firma-nota")).toBeInTheDocument();
  });

  it("Admin de Compañía sin red: «aplica solo a su compañía» y no envía compañías asociadas", async () => {
    const user = userEvent.setup();
    renderForm();
    expect(await screen.findByText("Este mandatario aplica solo a su compañía")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
    await user.type(screen.getByLabelText("Número de documento"), "1020304050");
    await user.click(radio("Baúl de firmas"));
    expect(screen.queryByTestId("mandatario-asociadas-ot")).not.toBeInTheDocument();
  });
});

describe("HU #13248b AC3 — errores por campo sin perder lo escrito", () => {
  it("422 con campo: el error queda junto al campo y el formulario conserva todo", async () => {
    onSubmit.mockRejectedValue(
      new ApiValidationError([{ field: "email", message: "El correo no es válido.", value: null }], 422),
    );
    const user = userEvent.setup();
    renderForm();
    await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
    await user.type(screen.getByLabelText("Número de documento"), "1020304050");
    await user.type(screen.getByLabelText(/^Correo/), "ana@ejemplo.com");
    await user.click(radio("Validación de identidad"));
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(screen.getByText("El correo no es válido.")).toBeInTheDocument());
    expect(screen.getByLabelText(/^Correo/)).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Nombre completo")).toHaveValue("Ana Restrepo");
    expect(screen.getByLabelText("Número de documento")).toHaveValue("1020304050");
    expect(radio("Validación de identidad")).toBeChecked();
    expect(screen.getByRole("dialog", { name: "Registrar mandatario" })).toBeInTheDocument();
  });
});
