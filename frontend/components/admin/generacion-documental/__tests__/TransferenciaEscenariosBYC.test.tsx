// HU #12208 (Feature #12201) — escenarios B y C, y el régimen aplicable ya retirado de la
// pantalla: se declara por debajo (decisión del PO, 2026-09-09).
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

describe("Régimen aplicable — retirado de la pantalla (decisión del PO, 2026-09-09)", () => {
  beforeEach(() => {
    generateTransferenciaDocument.mockReset();
    generateTransferenciaDocument.mockResolvedValue({
      id: "0a1b2c3d-0000-0000-0000-000000000000",
      status: "generated",
      advisories: [],
    });
  });

  /**
   * Lo que se retiró no era un campo más: era la declaración del USUARIO de que su operación no es
   * ninguno de los once traspasos especiales de los arts. 5.3.2.3 a 5.3.2.13. Ahora la hace el
   * sistema por él. Estos casos fijan las dos mitades del cambio —no se ve, y se envía igual— para
   * que ninguna de las dos se pierda por accidente.
   */
  it("la sección ya no se muestra, ni entera ni a trozos", () => {
    render(<TransferenciaFormPanel />);

    expect(screen.queryByTestId("transferencia-regimen-aplicable")).not.toBeInTheDocument();
    expect(screen.queryByText(/régimen aplicable a la operación/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/ninguna de las anteriores aplica/i)).not.toBeInTheDocument();

    // Y ni una de las once condiciones queda suelta por la pantalla.
    for (const condicion of REGIMEN_ESPECIAL_CONDICIONES) {
      expect(screen.queryByLabelText(new RegExp(condicion.titulo, "i"))).not.toBeInTheDocument();
    }
  });

  it("el botón Generar ya no espera una respuesta que nadie va a dar", () => {
    render(<TransferenciaFormPanel />);

    // Antes nacía deshabilitado con «responde primero el régimen aplicable».
    expect(boton()).toBeEnabled();
    expect(screen.queryByText(/responde primero el régimen aplicable/i)).not.toBeInTheDocument();
  });

  /**
   * La mitad que no se ve: el cuerpo sigue llevando la declaración. Si esto se rompiera, el
   * servidor rechazaría CADA generación con VB-07 —«no respondida»— y el formulario no tendría
   * dónde decírselo al usuario, porque el control que lo explicaba ya no está.
   */
  it("envía «ninguna aplica» por debajo, con su fecha", async () => {
    render(<TransferenciaFormPanel />);

    await userEvent.click(boton());

    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalledTimes(1));

    const payload = generateTransferenciaDocument.mock.calls[0][0];
    expect(payload.regimenAplicable.ningunaAplica).toBe(true);
    expect(payload.regimenAplicable.condicionesDeclaradas).toEqual([]);
    expect(payload.regimenAplicable.declaredAt).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });

  /**
   * El catálogo normativo se sigue vigilando aunque hoy no lo pinte nadie: `VB-07` lo evalúa en el
   * servidor con los mismos códigos, y es lo que permite devolver el control a la pantalla sin
   * reconstruirlo. El art. 5.3.2.14 sigue fuera —es el paso final común a todo traspaso, no una
   * condición especial—.
   */
  it("conserva íntegro el catálogo de las once condiciones", () => {
    expect(REGIMEN_ESPECIAL_CONDICIONES).toHaveLength(11);
    expect(REGIMEN_ESPECIAL_CONDICIONES.map((c) => c.articulo)).not.toContain("art. 5.3.2.14");
  });

  it("el formulario de Certificado RUES tampoco lo muestra", () => {
    render(<RuesFormPanel status="ready" />);

    expect(screen.queryByTestId("transferencia-regimen-aplicable")).not.toBeInTheDocument();
    expect(screen.queryByText(/régimen aplicable a la operación/i)).not.toBeInTheDocument();
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
