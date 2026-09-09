// Feature #12201 — captura del NIT, revisión previa y generación del Certificado RUES (CF-04).
//
// Estos casos cubren el hueco que dejó la descomposición: la HU #12203 es `[BACKEND]` y ninguna
// HU pidió el formulario, así que la pestaña mostraba encabezado y ningún control.
//
// Las respuestas simuladas siguen el esquema `StandaloneRuesPreviewResult` de
// `contracts/openapi/core-api.v1.yaml`: el array se llama `campos`, sus elementos solo traen
// `key` y `value`, y `error` viaja con HTTP 200. La primera versión de este fichero inventó un
// `fields` con `label` que no existe, y por eso los diez casos pasaban mientras la pantalla
// reventaba con un TypeError en runtime.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RuesGeneracionForm } from "../RuesGeneracionForm";
import { previewRuesCompany, generateRuesDocument } from "@/lib/api/admin-generacion-documental";
import { ApiValidationError } from "@/lib/api/types";
import type { StandaloneRuesPreviewResult } from "@/lib/api/types-generacion-documental";

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  previewRuesCompany: vi.fn(),
  generateRuesDocument: vi.fn(),
}));

const previewMock = vi.mocked(previewRuesCompany);
const generateMock = vi.mocked(generateRuesDocument);

const HALLAZGO: StandaloneRuesPreviewResult = {
  found: true,
  nit: "900123456",
  campos: [
    { key: "rues_razon_social", value: "TRANSPORTES ACME S.A.S." },
    { key: "rues_estado", value: null },
  ],
};

async function consultar(nit = "900123456") {
  await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), nit);
  await userEvent.click(screen.getByRole("button", { name: /revisar antes de generar/i }));
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("RuesGeneracionForm — la pestaña tiene por fin dónde escribir y qué pulsar", () => {
  it("expone el campo de NIT y las dos acciones", () => {
    render(<RuesGeneracionForm />);

    expect(screen.getByLabelText(/NIT de la compañía/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /revisar antes de generar/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /generar certificado/i })).toBeInTheDocument();
  });

  it("sin NIT ambas acciones están deshabilitadas", () => {
    render(<RuesGeneracionForm />);

    expect(screen.getByRole("button", { name: /revisar antes de generar/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /generar certificado/i })).toBeDisabled();
  });
});

describe("RuesGeneracionForm — revisión previa (no persiste nada)", () => {
  it("consulta con el NIT sin espacios y traduce las claves del RUES a etiquetas", async () => {
    previewMock.mockResolvedValue(HALLAZGO);

    render(<RuesGeneracionForm />);
    await consultar("  900123456  ");

    await waitFor(() => expect(previewMock).toHaveBeenCalledWith("900123456"));
    expect(await screen.findByText("TRANSPORTES ACME S.A.S.")).toBeInTheDocument();
    // La clave cruda del contrato no se le enseña al usuario.
    expect(screen.getByText("Razón social")).toBeInTheDocument();
    expect(screen.queryByText("rues_razon_social")).not.toBeInTheDocument();
    // Un campo sin valor se pinta como guion, no como "null" ni como hueco mudo.
    expect(screen.getByText("—")).toBeInTheDocument();
    // La revisión previa no genera: el endpoint de generación no se toca.
    expect(generateMock).not.toHaveBeenCalled();
  });

  it("una clave que el mapa no conoce se muestra legible, no cruda ni desaparecida", async () => {
    previewMock.mockResolvedValue({
      found: true,
      nit: "900123456",
      campos: [{ key: "rues_campo_nuevo_del_proveedor", value: "algo" }],
    });

    render(<RuesGeneracionForm />);
    await consultar();

    expect(await screen.findByText("Campo nuevo del proveedor")).toBeInTheDocument();
  });

  it("no enseña el JSON crudo de actividades", async () => {
    previewMock.mockResolvedValue({
      found: true,
      nit: "900123456",
      campos: [
        { key: "rues_razon_social", value: "TRANSPORTES ACME S.A.S." },
        { key: "rues_actividades_json", value: '[{"code":"H4923"}]' },
      ],
    });

    render(<RuesGeneracionForm />);
    await consultar();

    expect(await screen.findByText("TRANSPORTES ACME S.A.S.")).toBeInTheDocument();
    expect(screen.queryByText(/H4923/)).not.toBeInTheDocument();
  });

  it("un NIT sin coincidencia lo dice, y no finge una tabla vacía", async () => {
    previewMock.mockResolvedValue({ found: false, nit: "999999999", campos: [] });

    render(<RuesGeneracionForm />);
    await consultar("999999999");

    expect(await screen.findByText(/no tiene coincidencia en el RUES/i)).toBeInTheDocument();
    expect(screen.queryByText(/datos que quedarán congelados/i)).not.toBeInTheDocument();
  });

  it("una respuesta sin `campos` no tumba la pantalla", async () => {
    // Regresión del TypeError que vio el PO: el tipo decía `fields` y el servidor mandaba
    // `campos`, así que `preview.fields.map` reventaba. Aquí falta el array por completo.
    previewMock.mockResolvedValue({ found: true, nit: "900123456" } as StandaloneRuesPreviewResult);

    render(<RuesGeneracionForm />);
    await expect(consultar()).resolves.not.toThrow();

    await waitFor(() => expect(previewMock).toHaveBeenCalled());
    expect(screen.queryByText(/datos que quedarán congelados/i)).not.toBeInTheDocument();
  });

  it("editar el NIT invalida la revisión previa en pantalla", async () => {
    previewMock.mockResolvedValue(HALLAZGO);

    render(<RuesGeneracionForm />);
    await consultar();
    expect(await screen.findByText("TRANSPORTES ACME S.A.S.")).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "7");

    expect(screen.queryByText("TRANSPORTES ACME S.A.S.")).not.toBeInTheDocument();
  });
});

describe("RuesGeneracionForm — una avería del proveedor no es un veredicto sobre el NIT", () => {
  it("`provider_unavailable` llega con HTTP 200 y se anuncia como avería, no como NIT inexistente", async () => {
    previewMock.mockResolvedValue({
      found: false,
      nit: "900123456",
      campos: [],
      error: "provider_unavailable",
    });

    render(<RuesGeneracionForm />);
    await consultar();

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent(/no es un problema del NIT/i);
    // Lo que NO puede pasar: culpar al número que escribió el usuario.
    expect(screen.queryByText(/no tiene coincidencia en el RUES/i)).not.toBeInTheDocument();
  });

  it("`provider_not_found` dice que reintentar no sirve", async () => {
    previewMock.mockResolvedValue({
      found: false,
      nit: "900123456",
      campos: [],
      error: "provider_not_found",
    });

    render(<RuesGeneracionForm />);
    await consultar();

    expect(await screen.findByRole("alert")).toHaveTextContent(/no se resuelve reintentando/i);
  });
});

describe("RuesGeneracionForm — generación", () => {
  it("genera y remite al historial, dejando claro que no devuelve el PDF", async () => {
    generateMock.mockResolvedValue({ id: "0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b", status: "generated" });

    render(<RuesGeneracionForm />);
    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "900123456");
    await userEvent.click(screen.getByRole("button", { name: /generar certificado/i }));

    await waitFor(() => expect(generateMock).toHaveBeenCalledWith("900123456"));

    const aviso = await screen.findByRole("status");
    expect(aviso).toHaveTextContent(/nunca el PDF/i);
    expect(screen.getByRole("link", { name: /ir al historial/i })).toHaveAttribute(
      "href",
      "/admin/generacion-documental/historial",
    );
  });

  it("un 422 se muestra por campo y NUNCA repite el valor capturado", async () => {
    generateMock.mockRejectedValue(
      new ApiValidationError(
        [{ field: "nit", message: "El NIT no tiene coincidencia en RUES.", value: "900123456" }],
        422,
      ),
    );

    render(<RuesGeneracionForm />);
    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "900123456");
    await userEvent.click(screen.getByRole("button", { name: /generar certificado/i }));

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("El NIT no tiene coincidencia en RUES.");

    // Control positivo: el extractor sí ve el contenido de la alerta, así que la ausencia del
    // valor capturado significa algo. El NIT aparece en el input —eso es lo que el usuario
    // escribió—, pero no debe reaparecer dentro del mensaje de error.
    expect(alerta).toHaveTextContent("nit");
    expect(alerta.textContent ?? "").not.toContain("900123456");
  });

  it("un fallo inesperado no deja la pantalla muda", async () => {
    generateMock.mockRejectedValue(new Error("boom"));

    render(<RuesGeneracionForm />);
    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "900123456");
    await userEvent.click(screen.getByRole("button", { name: /generar certificado/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no se pudo generar el certificado/i);
  });
});
