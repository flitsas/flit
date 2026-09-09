// HU #12207 y #12208 (Feature #12201) — formulario de Transferencia de dominio.
// Uso de ejemplo: render(<TransferenciaFormPanel />), responder el régimen y enviar el formulario.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TransferenciaFormPanel } from "../TransferenciaFormPanel";
import { ApiValidationError } from "@/lib/api/types";

const generateTransferenciaDocument = vi.fn();

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  GENERACION_DOCUMENTAL_API_BASE: "/api/v1/admin/generacion-documental",
  generateTransferenciaDocument: (...args: unknown[]) => generateTransferenciaDocument(...args),
}));

/**
 * CF-24 — desde HU #12208 el botón de generar nace deshabilitado: hay que responder antes el
 * control de régimen aplicable. Todos los casos que envían el formulario pasan por aquí.
 */
async function responderRegimen() {
  await userEvent.click(screen.getByLabelText(/ninguna de las anteriores aplica/i));
}

describe("TransferenciaFormPanel — escenario A", () => {
  beforeEach(() => {
    generateTransferenciaDocument.mockReset();
    generateTransferenciaDocument.mockResolvedValue({
      id: "0a1b2c3d-0000-0000-0000-000000000000",
      status: "generated",
      advisories: [],
    });
  });

  /**
   * CF-10 — el aviso de que FLIT no garantiza suficiencia jurídica ni aprobación por el OT es
   * visible sin desplazarse hasta el botón de generar. La comprobación es de ORDEN en el DOM: un
   * aviso al pie, después del botón, se lee cuando ya se emitió el documento.
   */
  it("muestra el aviso normativo antes del botón de generar (CF-10)", () => {
    render(<TransferenciaFormPanel />);

    const aviso = screen.getByTestId("transferencia-aviso-normativo");
    const boton = screen.getByRole("button", { name: /generar documento/i });

    expect(aviso).toHaveAttribute("role", "note");
    expect(within(aviso).getByText(/no garantiza la suficiencia jurídica/i)).toBeInTheDocument();
    expect(within(aviso).getByText(/organismo de tránsito realiza validaciones propias|no equivale a aprobar el trámite/i))
      .toBeInTheDocument();

    // Node.DOCUMENT_POSITION_FOLLOWING = 4: el botón viene DESPUÉS del aviso.
    expect(aviso.compareDocumentPosition(boton) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("envía exactamente un escenario (VB-05) y el bloque de vehículo capturado", async () => {
    render(<TransferenciaFormPanel />);

    await userEvent.type(screen.getByLabelText("Placa"), "abc123");
    await userEvent.type(screen.getByLabelText("Ciudad de firma"), "Medellín");
    await responderRegimen();
    await userEvent.click(screen.getByRole("button", { name: /generar documento/i }));

    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalledTimes(1));

    const payload = generateTransferenciaDocument.mock.calls[0][0];
    expect(payload.escenarios).toEqual(["A"]);
    expect(payload.vehiculo.placa).toBe("ABC123");
    expect(payload.negocio.ciudadFirma).toBe("Medellín");
  });

  /**
   * Las VB bloqueantes vuelven en un 422 con código, campo y mensaje. La interfaz los muestra tal
   * cual: no reescribe el mensaje ni añade el valor tecleado.
   */
  it("pinta los errores bloqueantes con su código y sin el valor capturado", async () => {
    generateTransferenciaDocument.mockRejectedValue(
      new ApiValidationError(
        [
          {
            code: "VB-02",
            field: "vehiculo.placa",
            message: "La placa no tiene un formato válido: se esperan entre 5 y 7 caracteres alfanuméricos.",
          },
          {
            code: "VB-A-07",
            field: "negocio.tituloJuridico",
            message: "El título jurídico del negocio traslaticio es obligatorio y debe corresponder al catálogo.",
          },
        ] as never,
        422,
      ),
    );

    render(<TransferenciaFormPanel />);
    await userEvent.type(screen.getByLabelText("Placa"), "XX");
    await responderRegimen();
    await userEvent.click(screen.getByRole("button", { name: /generar documento/i }));

    const errores = await screen.findByTestId("transferencia-errores");
    expect(within(errores).getByText(/VB-02/)).toBeInTheDocument();
    expect(within(errores).getByText(/VB-A-07/)).toBeInTheDocument();
    expect(errores.textContent).not.toContain("XX");

    // Y el error queda enlazado al campo por aria-describedby (CF-22).
    expect(screen.getByLabelText("Placa")).toHaveAttribute("aria-describedby", "tf-placa-error");
    expect(screen.queryByTestId("transferencia-resultado")).not.toBeInTheDocument();
  });

  /**
   * Las prevalidaciones advisory viajan en el 200 y se muestran como AVISO. Si se pintaran como
   * error, VB-01 o VB-A-08 bloquearían en la práctica algo que la norma no bloquea.
   */
  it("muestra las prevalidaciones advisory como aviso, no como error", async () => {
    generateTransferenciaDocument.mockResolvedValue({
      id: "0a1b2c3d-0000-0000-0000-000000000000",
      status: "generated",
      advisories: [
        { code: "VB-01", field: "vehiculo.placa", message: "La vigencia de la matrícula la verifica el OT." },
        { code: "VB-A-08", field: "negocio.asumeRetencionFuente", message: "El pago de la retención lo acredita el interesado." },
      ],
    });

    render(<TransferenciaFormPanel />);
    await responderRegimen();
    await userEvent.click(screen.getByRole("button", { name: /generar documento/i }));

    const resultado = await screen.findByTestId("transferencia-resultado");
    expect(within(resultado).getByText(/documento generado/i)).toBeInTheDocument();

    const avisos = screen.getByTestId("transferencia-advisories");
    expect(within(avisos).getByText(/VB-01/)).toBeInTheDocument();
    expect(within(avisos).getByText(/VB-A-08/)).toBeInTheDocument();
    expect(screen.queryByTestId("transferencia-errores")).not.toBeInTheDocument();
  });

  /**
   * Remolques y semirremolques están exentos del impuesto sobre vehículos (Ley 488/1998): no se
   * pregunta quién lo asume y no se envía el campo.
   */
  it("en remolques no pide ni envía quién asume el impuesto sobre vehículos", async () => {
    render(<TransferenciaFormPanel />);

    expect(screen.getByLabelText("Asume el impuesto sobre vehículos")).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("Clase"), "SEMIRREMOLQUE");

    expect(screen.queryByLabelText("Asume el impuesto sobre vehículos")).not.toBeInTheDocument();
    expect(screen.getByTestId("transferencia-exencion-impuesto")).toHaveTextContent(/Ley 488 de 1998/);

    await responderRegimen();
    await userEvent.click(screen.getByRole("button", { name: /generar documento/i }));
    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalled());

    expect(generateTransferenciaDocument.mock.calls[0][0].negocio.asumeImpuestoVehiculo).toBeNull();
  });

  /** El levantamiento solo se pregunta si hay gravamen declarado (VB-A-04). */
  it("pregunta por el levantamiento solo cuando se declara gravamen activo", async () => {
    render(<TransferenciaFormPanel />);

    expect(screen.queryByLabelText(/se adjunta el levantamiento/i)).not.toBeInTheDocument();

    await userEvent.click(screen.getByLabelText(/tiene un gravamen o limitación/i));

    expect(screen.getByLabelText(/se adjunta el levantamiento/i)).toBeInTheDocument();

    await responderRegimen();
    await userEvent.click(screen.getByRole("button", { name: /generar documento/i }));
    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalled());

    expect(generateTransferenciaDocument.mock.calls[0][0].gravamen).toEqual({
      gravamenActivo: true,
      tieneLevantamientoOAutorizacion: false,
    });
  });

  /** El campo de descripción solo existe para el título OTRO (anexo §5.4). */
  it("pide la descripción del negocio solo con título OTRO", async () => {
    render(<TransferenciaFormPanel />);

    expect(screen.queryByLabelText("Descripción del negocio")).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText("Título jurídico"), "OTRO");

    expect(screen.getByLabelText("Descripción del negocio")).toBeInTheDocument();
  });

  /** El DV del NIT no se captura: lo calcula el servidor (CF-25 y anexo §5.2). */
  it("no ofrece campo de dígito de verificación", async () => {
    render(<TransferenciaFormPanel />);

    await userEvent.selectOptions(screen.getByLabelText("Tipo de persona", { selector: "#tf-transferente-tipopersona" }), "PJ");

    expect(screen.queryByLabelText(/dígito de verificación/i)).not.toBeInTheDocument();
    expect(screen.getByText(/lo calcula el sistema/i)).toBeInTheDocument();
  });

  /** Accesibilidad (CF-22): cada control tiene su label asociada. */
  it("todos los campos tienen label asociada", () => {
    const { container } = render(<TransferenciaFormPanel />);

    const controles = container.querySelectorAll("input, select");
    expect(controles.length).toBeGreaterThan(10);

    for (const control of controles) {
      const id = control.getAttribute("id");
      expect(id).toBeTruthy();
      expect(container.querySelector(`label[for="${id}"]`)).not.toBeNull();
    }
  });
});
