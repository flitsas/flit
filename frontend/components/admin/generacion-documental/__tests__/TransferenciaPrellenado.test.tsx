// HU #12209 (Feature #12201, CF-25 / CF-22) — captura «placa primero», encadenamiento por parte,
// bloqueo anti-pisado y DV calculado en cliente.
// Uso de ejemplo: render(<TransferenciaFormPanel />), escribir la placa y pulsar
// «Consultar la placa en el RUNT».
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TransferenciaFormPanel } from "../TransferenciaFormPanel";
import { ApiError } from "@/lib/api/types";
import type { PrefillField } from "@/lib/api/types-generacion-documental";

const generateTransferenciaDocument = vi.fn();
const prefillVehiculo = vi.fn();
const prefillPersonaJuridica = vi.fn();
const prefillPersonaNatural = vi.fn();

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  GENERACION_DOCUMENTAL_API_BASE: "/api/v1/admin/generacion-documental",
  generateTransferenciaDocument: (...a: unknown[]) => generateTransferenciaDocument(...a),
  prefillVehiculo: (...a: unknown[]) => prefillVehiculo(...a),
  prefillPersonaJuridica: (...a: unknown[]) => prefillPersonaJuridica(...a),
  prefillPersonaNatural: (...a: unknown[]) => prefillPersonaNatural(...a),
}));

/** Las 12 variables de vehículo que el RUNT devuelve. La 13.ª —licencia de tránsito— no está. */
const CAMPOS_RUNT: PrefillField[] = [
  { key: "placa", value: "ABC123" },
  { key: "marca", value: "MAZDA" },
  { key: "linea", value: "CX-30" },
  { key: "modeloAnio", value: "2022" },
  { key: "claseVehiculo", value: "CAMIONETA" },
  { key: "tipoCarroceria", value: "WAGON" },
  { key: "color", value: "GRIS" },
  { key: "noMotor", value: "MTR-0001" },
  { key: "noChasis", value: "CHS-0001" },
  { key: "noSerie", value: "SRE-0001" },
  { key: "servicio", value: "PARTICULAR" },
  { key: "organismoTransito", value: "SECRETARÍA DE MOVILIDAD DE MEDELLÍN" },
];

const ETIQUETAS_VEHICULO: Record<string, string> = {
  placa: "Placa",
  marca: "Marca",
  linea: "Línea",
  modeloAnio: "Año modelo",
  claseVehiculo: "Clase",
  tipoCarroceria: "Carrocería",
  color: "Color(es)",
  noMotor: "Motor No.",
  noChasis: "Chasis / VIN No.",
  noSerie: "Serie No.",
  servicio: "Servicio",
  organismoTransito: "Organismo de tránsito",
};

async function consultarPlaca(placa = "ABC123") {
  await userEvent.type(screen.getByLabelText("Placa"), placa);
  await userEvent.click(screen.getByTestId("tf-consultar-placa"));
  await waitFor(() => expect(prefillVehiculo).toHaveBeenCalled());
}

describe("TransferenciaFormPanel — prellenado del vehículo por placa (CF-25)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    generateTransferenciaDocument.mockResolvedValue({ id: "id-1", status: "generated", advisories: [] });
    prefillVehiculo.mockResolvedValue({ found: true, source: "RUNT", fields: CAMPOS_RUNT });
    prefillPersonaJuridica.mockResolvedValue({ found: false });
    prefillPersonaNatural.mockResolvedValue({ found: false });
  });

  /** AC: 12 de los 13 campos quedan diligenciados y cada uno indica su fuente. */
  it("hidrata 12 de los 13 campos del vehículo y deja la licencia de tránsito manual", async () => {
    render(<TransferenciaFormPanel />);
    await consultarPlaca();

    for (const campo of CAMPOS_RUNT) {
      const input = screen.getByLabelText(ETIQUETAS_VEHICULO[campo.key]) as HTMLInputElement;
      expect(input.value).toBe(campo.value);
      expect(input).toHaveAttribute("data-hidratado", "true");
      // La fuente se declara por campo, en texto, no solo con un color.
      expect(screen.getByTestId(`${input.id}-hidratacion`)).toHaveTextContent(/Dato de RUNT/);
    }

    expect(prefillVehiculo).toHaveBeenCalledWith({ placa: "ABC123" });

    // El 13.º campo: ni bloqueado, ni marcado como hidratado, ni diligenciado.
    const licencia = screen.getByLabelText("Licencia de tránsito No.") as HTMLInputElement;
    expect(licencia.value).toBe("");
    expect(licencia).not.toHaveAttribute("data-hidratado");
    expect(licencia).not.toHaveAttribute("data-bloqueado");
    expect(screen.queryByTestId("tf-licencia-hidratacion")).not.toBeInTheDocument();
  });

  /**
   * Decisión del PO (adenda §15.1), literal: color y carrocería editables sin acción previa; los
   * otros 10 bloqueados, con una acción explícita para liberar uno.
   */
  it("deja color y carrocería editables, bloquea los otros 10 y permite liberar uno", async () => {
    render(<TransferenciaFormPanel />);
    await consultarPlaca();

    expect(screen.getByLabelText("Color(es)")).not.toHaveAttribute("data-bloqueado");
    expect(screen.getByLabelText("Carrocería")).not.toHaveAttribute("data-bloqueado");

    const bloqueados = CAMPOS_RUNT.filter(
      (c) => screen.getByLabelText(ETIQUETAS_VEHICULO[c.key]).getAttribute("data-bloqueado") === "true",
    );
    expect(bloqueados).toHaveLength(10);

    // El color se puede editar sin liberar nada: no hay botón que pulsar antes.
    await userEvent.clear(screen.getByLabelText("Color(es)"));
    await userEvent.type(screen.getByLabelText("Color(es)"), "AZUL");
    expect(screen.getByLabelText("Color(es)")).toHaveValue("AZUL");

    // La marca sí exige la acción explícita.
    const marca = screen.getByLabelText("Marca");
    expect(marca).toHaveAttribute("readonly");
    await userEvent.click(screen.getByRole("button", { name: /liberar marca/i }));
    expect(marca).not.toHaveAttribute("readonly");
    expect(screen.getByTestId("tf-marca-hidratacion")).toHaveTextContent(/editable/i);
  });

  /** AC anti-pisado: gana lo que escribió el usuario; la discrepancia se señala, no se aplica. */
  it("no pisa el valor escrito por el usuario y señala la discrepancia", async () => {
    render(<TransferenciaFormPanel />);

    await userEvent.type(screen.getByLabelText("Motor No."), "MTR-ESCRITO-A-MANO");
    await consultarPlaca();

    expect(screen.getByLabelText("Motor No.")).toHaveValue("MTR-ESCRITO-A-MANO");
    const aviso = screen.getByTestId("tf-motor-discrepancia");
    expect(aviso).toHaveAttribute("role", "status");
    expect(aviso).toHaveTextContent(/RUNT reporta «MTR-0001»/);
    expect(aviso).toHaveTextContent(/Se conservó el valor que escribiste/);
    // El campo no queda bloqueado: sigue siendo captura del usuario.
    expect(screen.getByLabelText("Motor No.")).not.toHaveAttribute("data-bloqueado");

    // Y solo si el usuario lo decide explícitamente se adopta el valor de la fuente.
    await userEvent.click(within(aviso).getByRole("button", { name: /usar el de runt/i }));
    expect(screen.getByLabelText("Motor No.")).toHaveValue("MTR-0001");
    expect(screen.queryByTestId("tf-motor-discrepancia")).not.toBeInTheDocument();
  });

  /** AC «fuente caída»: el formulario sigue utilizable y la generación no se bloquea. */
  it("degrada sin bloquear cuando la fuente responde 502", async () => {
    prefillVehiculo.mockRejectedValue(new ApiError(502, "El proveedor RUNT no está disponible."));

    render(<TransferenciaFormPanel />);
    await consultarPlaca();

    const error = await screen.findByTestId("tf-prefill-vehiculo-error");
    expect(error).toHaveAttribute("role", "alert");
    expect(error).toHaveTextContent(/El proveedor RUNT no está disponible/);
    expect(within(error).getByRole("button", { name: /reintentar la consulta del vehículo/i })).toBeInTheDocument();

    // Todos los campos del vehículo siguen editables: ninguno quedó bloqueado ni hidratado.
    for (const campo of CAMPOS_RUNT) {
      const input = screen.getByLabelText(ETIQUETAS_VEHICULO[campo.key]);
      expect(input).not.toHaveAttribute("readonly");
      expect(input).not.toHaveAttribute("data-hidratado");
    }
    await userEvent.type(screen.getByLabelText("Marca"), "MAZDA");
    expect(screen.getByLabelText("Marca")).toHaveValue("MAZDA");

    // Y no impide generar el documento.
    await userEvent.click(screen.getByLabelText(/ninguna de las anteriores aplica/i));
    const generar = screen.getByRole("button", { name: /generar documento/i });
    expect(generar).toBeEnabled();
    await userEvent.click(generar);
    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalledTimes(1));
  });

  /** Sin antecedente es un 200 con `found:false`: no es error y todo queda manual. */
  it("cuando no hay antecedente deja el bloque manual y lo dice", async () => {
    prefillVehiculo.mockResolvedValue({ found: false });

    render(<TransferenciaFormPanel />);
    await consultarPlaca("ZZZ999");

    expect(screen.getByTestId("tf-prefill-vehiculo-estado")).toHaveTextContent(/no tiene antecedente/i);
    expect(screen.queryByTestId("tf-prefill-vehiculo-error")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Marca")).not.toHaveAttribute("data-hidratado");
  });
});

describe("TransferenciaFormPanel — encadenamiento por parte (CF-25)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    generateTransferenciaDocument.mockResolvedValue({ id: "id-1", status: "generated", advisories: [] });
    prefillVehiculo.mockResolvedValue({ found: false });
    prefillPersonaJuridica.mockResolvedValue({ found: false });
    prefillPersonaNatural.mockResolvedValue({ found: false });
  });

  /**
   * Persona jurídica: directorio de representantes legales y luego RUES. Cuando la fuente efectiva
   * es RUES, el representante legal y su documento quedan manuales: el RUES certifica la FACULTAD
   * de representación, no la persona.
   */
  it("consulta la cadena jurídica y deja manual al representante legal cuando responde RUES", async () => {
    prefillPersonaJuridica.mockResolvedValue({
      found: true,
      source: "RUES",
      dv: "8",
      fields: [
        { key: "nombreRazonSocial", value: "COMPAÑÍA DE PRUEBA S.A.S." },
        { key: "domicilio", value: "BOGOTÁ D.C." },
        { key: "representanteLegal", value: "NO CERTIFICADO POR RUES" },
      ],
    });

    render(<TransferenciaFormPanel />);
    await userEvent.selectOptions(
      screen.getByLabelText("Tipo de persona", { selector: "#tf-transferente-tipopersona" }),
      "PJ",
    );

    // El orden de la cadena se enuncia en pantalla, no solo en el código.
    const cadena = screen.getByTestId("tf-transferente-cadena");
    expect(cadena).toHaveTextContent(/1\.\s*Directorio de representantes legales/);
    expect(cadena).toHaveTextContent(/2\.\s*RUES/);
    expect(
      cadena.textContent!.indexOf("Directorio de representantes legales"),
    ).toBeLessThan(cadena.textContent!.indexOf("RUES"));

    await userEvent.type(
      screen.getByLabelText("Número de documento", { selector: "#tf-transferente-numerodoc" }),
      "890903938",
    );
    await userEvent.click(screen.getByTestId("tf-transferente-consultar"));

    await waitFor(() => expect(prefillPersonaJuridica).toHaveBeenCalledWith({ nit: "890903938" }));
    expect(prefillPersonaNatural).not.toHaveBeenCalled();

    expect(screen.getByLabelText("Nombre o razón social", { selector: "#tf-transferente-nombre" }))
      .toHaveValue("COMPAÑÍA DE PRUEBA S.A.S.");
    // El RL no se hidrata ni se bloquea aunque la respuesta lo trajera.
    const rl = screen.getByLabelText("Representante legal");
    expect(rl).toHaveValue("");
    expect(rl).not.toHaveAttribute("data-hidratado");
    expect(rl).not.toHaveAttribute("readonly");
  });

  /** El DV del NIT se muestra calculado y el usuario no lo captura. */
  it("muestra el DV del NIT sin ofrecer un campo para capturarlo", async () => {
    render(<TransferenciaFormPanel />);
    await userEvent.selectOptions(
      screen.getByLabelText("Tipo de persona", { selector: "#tf-transferente-tipopersona" }),
      "PJ",
    );
    await userEvent.type(
      screen.getByLabelText("Número de documento", { selector: "#tf-transferente-numerodoc" }),
      "890903938",
    );

    expect(screen.getByTestId("tf-transferente-dv")).toHaveTextContent(/DV 8/);
    expect(screen.queryByLabelText(/dígito de verificación/i)).not.toBeInTheDocument();
    // Y no viaja en el payload: el backend es la fuente de verdad.
    await userEvent.click(screen.getByLabelText(/ninguna de las anteriores aplica/i));
    await userEvent.click(screen.getByRole("button", { name: /generar documento/i }));
    await waitFor(() => expect(generateTransferenciaDocument).toHaveBeenCalled());
    expect(generateTransferenciaDocument.mock.calls[0][0].transferente).not.toHaveProperty(
      "digitoVerificacion",
    );
  });

  /**
   * Persona natural: RUNT persona y luego `contact-lookup`, que por contrato nunca devuelve nombre
   * ni documento. Si es la fuente efectiva, el nombre queda manual.
   */
  it("consulta la cadena natural y deja el nombre manual cuando responde contact-lookup", async () => {
    prefillPersonaNatural.mockResolvedValue({
      found: true,
      source: "CONTACT_LOOKUP",
      fields: [
        { key: "nombreRazonSocial", value: "NOMBRE QUE EL CONTACTO NO DEVUELVE" },
        { key: "domicilio", value: "CALI" },
      ],
    });

    render(<TransferenciaFormPanel />);

    const cadena = screen.getByTestId("tf-adquirente-cadena");
    expect(cadena.textContent!.indexOf("RUNT")).toBeLessThan(
      cadena.textContent!.indexOf("Datos de contacto"),
    );

    await userEvent.type(
      screen.getByLabelText("Número de documento", { selector: "#tf-adquirente-numerodoc" }),
      "1020304050",
    );
    await userEvent.click(screen.getByTestId("tf-adquirente-consultar"));

    await waitFor(() =>
      expect(prefillPersonaNatural).toHaveBeenCalledWith({
        documentType: "CC",
        documentNumber: "1020304050",
      }),
    );
    expect(prefillPersonaJuridica).not.toHaveBeenCalled();

    const nombre = screen.getByLabelText("Nombre o razón social", { selector: "#tf-adquirente-nombre" });
    expect(nombre).toHaveValue("");
    expect(nombre).not.toHaveAttribute("data-hidratado");
    expect(
      screen.getByLabelText("Ciudad de domicilio", { selector: "#tf-adquirente-domicilio" }),
    ).toHaveValue("CALI");
  });

  /** Persona natural sin antecedente: el domicilio se captura a mano, sin marca de hidratación. */
  it("deja manual el domicilio de una persona natural sin antecedente", async () => {
    prefillPersonaNatural.mockResolvedValue({ found: false });

    render(<TransferenciaFormPanel />);
    await userEvent.type(
      screen.getByLabelText("Número de documento", { selector: "#tf-adquirente-numerodoc" }),
      "1020304050",
    );
    await userEvent.click(screen.getByTestId("tf-adquirente-consultar"));
    await waitFor(() => expect(prefillPersonaNatural).toHaveBeenCalled());

    const domicilio = screen.getByLabelText("Ciudad de domicilio", { selector: "#tf-adquirente-domicilio" });
    expect(domicilio).not.toHaveAttribute("data-hidratado");
    expect(domicilio).not.toHaveAttribute("readonly");
  });
});

describe("TransferenciaFormPanel — campos siempre manuales y accesibilidad", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    generateTransferenciaDocument.mockResolvedValue({ id: "id-1", status: "generated", advisories: [] });
    prefillVehiculo.mockResolvedValue({ found: true, source: "RUNT", fields: CAMPOS_RUNT });
    prefillPersonaJuridica.mockResolvedValue({ found: false });
    prefillPersonaNatural.mockResolvedValue({ found: false });
  });

  /** AC «campos que siempre son manuales»: ninguno del bloque de negocio se hidrata ni se bloquea. */
  it("ningún campo del negocio queda bloqueado ni marcado como hidratado", async () => {
    render(<TransferenciaFormPanel />);
    await consultarPlaca();

    const negocio = screen.getByTestId("transferencia-negocio");
    const controles = negocio.querySelectorAll("input, select");
    expect(controles.length).toBeGreaterThanOrEqual(9);
    for (const control of controles) {
      expect(control).not.toHaveAttribute("data-hidratado");
      expect(control).not.toHaveAttribute("data-bloqueado");
      expect(control).not.toHaveAttribute("readonly");
    }
  });

  /** CF-22: label asociada, error y estado enlazados por aria-describedby, foco visible. */
  it("cumple los requisitos de accesibilidad del formulario hidratado", async () => {
    const { container } = render(<TransferenciaFormPanel />);
    await consultarPlaca();

    // 1. Toda entrada tiene su label asociada.
    for (const control of container.querySelectorAll("input, select")) {
      const id = control.getAttribute("id");
      expect(id).toBeTruthy();
      expect(container.querySelector(`label[for="${id}"]`)).not.toBeNull();
    }

    // 2. El estado de hidratación se enlaza al campo y se comunica con TEXTO, no solo con color.
    const marca = screen.getByLabelText("Marca");
    expect(marca.getAttribute("aria-describedby")).toContain("tf-marca-hidratacion");
    expect(screen.getByTestId("tf-marca-hidratacion")).toHaveTextContent(/Dato de RUNT · bloqueado/);

    // 3. El bloqueo usa readOnly, no disabled: el campo sigue siendo alcanzable por teclado.
    expect(marca).toHaveAttribute("readonly");
    expect(marca).not.toBeDisabled();
    marca.focus();
    expect(marca).toHaveFocus();

    // 4. El resultado de la consulta se anuncia en una región viva.
    const estado = screen.getByTestId("tf-prefill-vehiculo-estado");
    expect(estado).toHaveAttribute("aria-live", "polite");
    expect(estado).toHaveTextContent(/Datos traídos del RUNT/);

    // 5. Todos los botones tienen texto visible o nombre accesible.
    for (const boton of screen.getAllByRole("button")) {
      expect(boton.getAttribute("aria-label") || boton.textContent?.trim()).toBeTruthy();
    }
  });
});
