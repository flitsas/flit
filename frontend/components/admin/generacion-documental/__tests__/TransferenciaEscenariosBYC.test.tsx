// HU #12208 (Feature #12201) — control de régimen aplicable (CF-24 / VB-07) y escenarios B y C.
// Uso de ejemplo: render(<TransferenciaFormPanel />), elegir escenario B y enviar el formulario.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TransferenciaFormPanel } from "../TransferenciaFormPanel";
import { RuesFormPanel } from "../RuesFormPanel";
import { REGIMEN_ESPECIAL_CONDICIONES } from "../transferencia-regimen";

const generateTransferenciaDocument = vi.fn();

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  GENERACION_DOCUMENTAL_API_BASE: "/api/v1/admin/generacion-documental",
  generateTransferenciaDocument: (...args: unknown[]) => generateTransferenciaDocument(...args),
}));

const boton = () => screen.getByRole("button", { name: /generar documento/i });

async function elegirEscenario(escenario: "A" | "B" | "C") {
  await userEvent.click(screen.getByLabelText(new RegExp(`^${escenario} —`)));
}

async function responderRegimen() {
  await userEvent.click(screen.getByLabelText(/ninguna de las anteriores aplica/i));
}

describe("Régimen aplicable a la operación (CF-24 / VB-07)", () => {
  beforeEach(() => {
    generateTransferenciaDocument.mockReset();
    generateTransferenciaDocument.mockResolvedValue({
      id: "0a1b2c3d-0000-0000-0000-000000000000",
      status: "generated",
      advisories: [],
    });
  });

  /**
   * El control es un gate: va antes del selector de escenario porque el anexo §4.0 lo pone antes.
   * Si estuviera después, el usuario habría clasificado su operación en A, B o C antes de saber que
   * su caso no pertenece a ninguno de los tres.
   */
  it("aparece antes del selector de escenario A/B/C", () => {
    render(<TransferenciaFormPanel />);

    const control = screen.getByTestId("transferencia-regimen-aplicable");
    const selector = screen.getByTestId("transferencia-escenario");

    // Node.DOCUMENT_POSITION_FOLLOWING = 4: el selector viene DESPUÉS del control.
    expect(control.compareDocumentPosition(selector) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  /** Las once, una por una, con su artículo: ni resumidas ni agrupadas. */
  it("enumera las 11 condiciones de los arts. 5.3.2.3 a 5.3.2.13 con su artículo", () => {
    render(<TransferenciaFormPanel />);

    const control = screen.getByTestId("transferencia-regimen-aplicable");
    expect(REGIMEN_ESPECIAL_CONDICIONES).toHaveLength(11);

    for (const condicion of REGIMEN_ESPECIAL_CONDICIONES) {
      const opcion = within(control).getByLabelText(new RegExp(condicion.titulo, "i"));
      expect(opcion).toBeInTheDocument();
      expect(control.textContent).toContain(condicion.articulo);
    }

    // Y ofrece la salida que permite continuar.
    expect(within(control).getByLabelText(/ninguna de las anteriores aplica/i)).toBeInTheDocument();
  });

  /**
   * El art. 5.3.2.14 (expedición de la nueva licencia) NO es una condición especial: es el paso
   * final común a todo traspaso. Ofrecerlo bloquearía trámites ordinarios.
   */
  it("no ofrece el art. 5.3.2.14 como condición especial", () => {
    render(<TransferenciaFormPanel />);

    const control = screen.getByTestId("transferencia-regimen-aplicable");
    expect(control.textContent).not.toContain("5.3.2.14");
    expect(control.textContent).not.toContain("nueva licencia de tránsito");
  });

  it("mantiene el botón Generar deshabilitado hasta que se elija una opción", async () => {
    render(<TransferenciaFormPanel />);

    expect(boton()).toBeDisabled();
    expect(screen.getByText(/responde primero el régimen aplicable/i)).toBeInTheDocument();

    await responderRegimen();

    expect(boton()).toBeEnabled();
  });

  /**
   * Declarar una condición bloquea con un mensaje que cita el artículo y explica el motivo. El
   * bloqueo no se comunica solo por color (CF-22): hay `role="alert"`, icono y texto, y el botón
   * queda deshabilitado con su explicación.
   */
  it("declarar una condición especial bloquea citando el artículo, y no solo por color", async () => {
    render(<TransferenciaFormPanel />);

    await userEvent.click(screen.getByLabelText(/vehículo blindado/i));

    const bloqueo = screen.getByTestId("transferencia-regimen-bloqueo");
    expect(bloqueo).toHaveAttribute("role", "alert");
    expect(bloqueo.textContent).toContain("art. 5.3.2.6");
    expect(bloqueo.textContent).toMatch(/no produce ni acredita/i);

    expect(boton()).toBeDisabled();
    expect(boton()).toHaveAttribute("aria-describedby", "tf-generar-ayuda");
    expect(screen.getByText(/impide generar este documento/i)).toBeInTheDocument();

    // Y no se intenta generar.
    await userEvent.click(boton());
    expect(generateTransferenciaDocument).not.toHaveBeenCalled();
  });

  /** La declaración y su fecha viajan al backend, que las conserva en `input_summary`. */
  it("envía la declaración de régimen con su fecha", async () => {
    render(<TransferenciaFormPanel />);

    await responderRegimen();
    await userEvent.click(boton());

    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalledTimes(1));

    const payload = generateTransferenciaDocument.mock.calls[0][0];
    expect(payload.regimenAplicable.ningunaAplica).toBe(true);
    expect(payload.regimenAplicable.condicionesDeclaradas).toEqual([]);
    expect(payload.regimenAplicable.declaredAt).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });

  /** El flujo de Certificado RUES no clasifica ningún traspaso: el control no existe ahí. */
  it("el formulario de Certificado RUES no muestra el control de régimen aplicable", () => {
    render(<RuesFormPanel status="full" />);

    expect(screen.queryByTestId("transferencia-regimen-aplicable")).not.toBeInTheDocument();
    expect(screen.queryByText(/régimen aplicable a la operación/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/ninguna de las anteriores aplica/i)).not.toBeInTheDocument();
  });
});

describe("Escenario B — transferencia unilateral de leasing (art. 5.3.2.2)", () => {
  beforeEach(() => {
    generateTransferenciaDocument.mockReset();
    generateTransferenciaDocument.mockResolvedValue({
      id: "0a1b2c3d-0000-0000-0000-000000000000",
      status: "generated",
      advisories: [],
    });
  });

  /**
   * CF-08 / VB-B-05 — el formulario del escenario B **no tiene** campo de precio. No está oculto ni
   * deshabilitado: no se declara. Y el bloque del adquirente tampoco existe: el locatario no es
   * parte del instrumento (§9.2).
   */
  it("no ofrece campos de precio ni bloque de adquirente", async () => {
    render(<TransferenciaFormPanel />);

    // En A sí existen.
    expect(screen.getByLabelText("Precio en letras")).toBeInTheDocument();
    expect(screen.getByLabelText("Título jurídico")).toBeInTheDocument();

    await elegirEscenario("B");

    expect(screen.queryByLabelText("Precio en letras")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Precio en números (COP)")).not.toBeInTheDocument();
    expect(
      screen.queryByLabelText("Contraprestación (negocios no monetarios)"),
    ).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Título jurídico")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Forma de pago o entrega")).not.toBeInTheDocument();

    // Ningún campo del adquirente: la parte no se captura.
    expect(screen.queryByLabelText("Nombre o razón social", { selector: "#tf-adquirente-nombre" }))
      .not.toBeInTheDocument();
    expect(document.querySelector("#tf-adquirente-numerodoc")).toBeNull();

    // Y se explica por qué, en texto.
    expect(screen.getByTestId("transferencia-sin-precio").textContent).toMatch(
      /acto unilateral: no se declara precio/i,
    );
  });

  it("captura el antecedente de leasing y lo envía sin adquirente ni precio", async () => {
    render(<TransferenciaFormPanel />);

    await responderRegimen();
    await elegirEscenario("B");

    await userEvent.click(screen.getByLabelText(/establecimiento bancario, compañía de financiamiento/i));
    await userEvent.type(screen.getByLabelText("No. del contrato de leasing"), "LSG-2020-000123");
    await userEvent.selectOptions(screen.getByLabelText("Causal de la transferencia"), "AUTOMATICA");
    await userEvent.type(
      screen.getByLabelText("Nombre o razón social del destinatario"),
      "COMPANIA DESTINATARIA DE PRUEBA SAS",
    );
    await userEvent.selectOptions(screen.getByLabelText("Tipo de documento del destinatario"), "NIT");
    await userEvent.type(screen.getByLabelText("Número de documento del destinatario"), "901555444");
    await userEvent.type(screen.getByLabelText("Ciudad de firma"), "Medellín");

    await userEvent.click(boton());
    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalledTimes(1));

    const payload = generateTransferenciaDocument.mock.calls[0][0];
    expect(payload.escenarios).toEqual(["B"]);
    expect(payload.adquirente).toBeUndefined();
    expect(payload.leasing).toEqual({
      transferenteEsEntidadFinanciera: true,
      noContratoLeasing: "LSG-2020-000123",
      tipoOpcionCompra: "AUTOMATICA",
      fechaTerminacion: "",
      locatarioNombre: "COMPANIA DESTINATARIA DE PRUEBA SAS",
      locatarioTipoDoc: "NIT",
      locatarioNoDoc: "901555444",
    });

    // El negocio solo lleva lugar y fecha de firma: ni precio, ni título, ni cargas fiscales.
    expect(payload.negocio).toEqual({ ciudadFirma: "Medellín", fechaFirma: "" });
    expect(payload.negocio.precioLetras).toBeUndefined();
    expect(payload.negocio.precioNumeros).toBeUndefined();
  });

  /** Las VB del escenario B vuelven en el 422 y se enlazan a su campo (CF-09 / CF-22). */
  it("pinta los errores VB-B-* junto a su campo", async () => {
    const { ApiValidationError } = await import("@/lib/api/types");
    generateTransferenciaDocument.mockRejectedValue(
      new ApiValidationError(
        [
          {
            code: "VB-B-02",
            field: "leasing.noContratoLeasing",
            message: "Debe declararse el número del contrato de leasing.",
          },
          {
            code: "VB-B-04",
            field: "leasing.locatarioNoDoc",
            message: "Debe declararse el número de documento del locatario destinatario.",
          },
        ] as never,
        422,
      ),
    );

    render(<TransferenciaFormPanel />);
    await responderRegimen();
    await elegirEscenario("B");
    await userEvent.click(boton());

    const errores = await screen.findByTestId("transferencia-errores");
    expect(within(errores).getByText(/VB-B-02/)).toBeInTheDocument();
    expect(within(errores).getByText(/VB-B-04/)).toBeInTheDocument();

    expect(screen.getByLabelText("No. del contrato de leasing")).toHaveAttribute(
      "aria-describedby",
      "tf-leasing-contrato-error",
    );
    expect(screen.getByLabelText("Número de documento del destinatario")).toHaveAttribute(
      "aria-describedby",
      "tf-leasing-locatario-doc-error",
    );
  });

  /** Accesibilidad (CF-22): en el escenario B también, cada control tiene su label. */
  it("todos los campos del escenario B tienen label asociada", async () => {
    const { container } = render(<TransferenciaFormPanel />);

    await elegirEscenario("B");

    for (const control of container.querySelectorAll("input, select")) {
      const id = control.getAttribute("id");
      expect(id).toBeTruthy();
      expect(container.querySelector(`label[for="${id}"]`)).not.toBeNull();
    }
  });
});

describe("Escenario C — entidad financiera a un tercero (sin exenciones)", () => {
  beforeEach(() => {
    generateTransferenciaDocument.mockReset();
    generateTransferenciaDocument.mockResolvedValue({
      id: "0a1b2c3d-0000-0000-0000-000000000000",
      status: "generated",
      advisories: [],
    });
  });

  /**
   * El escenario C se rige íntegramente por el art. 5.3.2.1: conserva adquirente, título jurídico y
   * precio, y el selector lo dice explícitamente para que nadie lo confunda con el B.
   */
  it("conserva adquirente y negocio, y advierte que no hereda exenciones", async () => {
    render(<TransferenciaFormPanel />);

    await responderRegimen();
    await elegirEscenario("C");

    expect(screen.getByLabelText("Título jurídico")).toBeInTheDocument();
    expect(screen.getByLabelText("Precio en letras")).toBeInTheDocument();
    expect(document.querySelector("#tf-adquirente-numerodoc")).not.toBeNull();

    const selector = screen.getByTestId("transferencia-escenario");
    expect(selector.textContent).toMatch(/no hereda ninguna exención del art. 5.3.2.2/i);

    await userEvent.type(screen.getByLabelText("Ciudad de firma"), "Medellín");
    await userEvent.click(boton());
    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalledTimes(1));

    const payload = generateTransferenciaDocument.mock.calls[0][0];
    expect(payload.escenarios).toEqual(["C"]);
    expect(payload.adquirente).toBeDefined();
    expect(payload.leasing).toBeUndefined();
  });
});
