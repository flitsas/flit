// HU #11058 — Precarga de las compañías al editar un representante legal.
//
// Esto NO es solo comodidad: el formulario reenvía la lista COMPLETA de compañías y el backend hace
// upsert con lo que reciba, así que un campo que llegue en blanco se persiste como null. Antes solo se
// precargaban NIT y razón social, de modo que cada edición del representante borraba silenciosamente
// el correo, la dirección, la ciudad y el teléfono de todas sus compañías.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LegalRepresentativesFormPanel } from "../LegalRepresentativesFormPanel";
import type {
  AssignableProcedureType,
  LegalRepresentativeItem,
} from "@/lib/api/admin-legal-representatives";

const PROC_TYPES: AssignableProcedureType[] = [
  { id: "019f8195-fed1-770a-98ae-295ed59b53d4", code: "TRASPASO_STANDARD", name: "Traspaso" },
];

/** Representante con DOS compañías, ambas con contacto completo. */
const ITEM: LegalRepresentativeItem = {
  id: "rep-1",
  representedCompanyId: "co-1",
  companyDocumentNumber: "900123456-7",
  companyName: "Comercializadora XYZ",
  documentType: "CC",
  documentNumber: "1098765432",
  firstLastName: "Gómez",
  secondLastName: "Ruiz",
  name: "Ana",
  email: "ana@xyz.co",
  address: "Calle 1",
  city: "Medellín",
  phone: "3001112233",
  signatureVaultId: null,
  identityValidationRef: null,
  hasSignatureOrIdentity: false,
  procedureTypeIds: [],
  companies: [
    {
      id: "co-1",
      nit: "900123456-7",
      name: "Comercializadora XYZ",
      deeds: [],
      email: "contacto@xyz.co",
      address: "Carrera 50 #10-20",
      city: "Medellín",
      phone: "6041234567",
    },
    {
      id: "co-2",
      nit: "901987654-3",
      name: "Inversiones ABC",
      deeds: [],
      email: "hola@abc.co",
      address: "Avenida 80 #5-15",
      city: "Bogotá",
      phone: "6017654321",
    },
  ],
  isActive: true,
  createdAt: "2026-06-01T00:00:00Z",
  updatedAt: null,
};

function renderPanel(
  editing: LegalRepresentativeItem | null,
  onSubmit = vi.fn().mockResolvedValue({ id: "rep-1", signals: [] }),
) {
  render(
    <LegalRepresentativesFormPanel
      open
      editing={editing}
      procedureTypes={PROC_TYPES}
      onClose={vi.fn()}
      onSubmit={onSubmit}
      onSaved={vi.fn()}
      onError={vi.fn()}
    />,
  );
  return onSubmit;
}

describe("LegalRepresentativesFormPanel — precarga de compañías (HU #11058)", () => {
  it("precarga TODAS las compañías del representante", async () => {
    renderPanel(ITEM);

    expect(await screen.findByDisplayValue("900123456-7")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Comercializadora XYZ")).toBeInTheDocument();
    expect(screen.getByDisplayValue("901987654-3")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Inversiones ABC")).toBeInTheDocument();
  });

  it("precarga el CONTACTO de cada compañía, no solo NIT y razón social", async () => {
    renderPanel(ITEM);

    for (const valor of [
      "contacto@xyz.co",
      "Carrera 50 #10-20",
      "6041234567",
      "hola@abc.co",
      "Avenida 80 #5-15",
      "6017654321",
    ]) {
      expect(await screen.findByDisplayValue(valor)).toBeInTheDocument();
    }
  });

  it("guardar sin tocar las asociaciones las conserva con su contacto intacto", async () => {
    const onSubmit = renderPanel(ITEM);

    await screen.findByDisplayValue("900123456-7");
    await userEvent.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const payload = onSubmit.mock.calls[0][0];

    // Las dos compañías siguen ahí, con su contacto: el upsert del backend no las vacía.
    expect(payload.companies).toEqual([
      {
        nit: "900123456-7",
        name: "Comercializadora XYZ",
        email: "contacto@xyz.co",
        address: "Carrera 50 #10-20",
        city: "Medellín",
        phone: "6041234567",
      },
      {
        nit: "901987654-3",
        name: "Inversiones ABC",
        email: "hola@abc.co",
        address: "Avenida 80 #5-15",
        city: "Bogotá",
        phone: "6017654321",
      },
    ]);
  });

  it("cambiar solo un dato del representante no altera las compañías", async () => {
    const onSubmit = renderPanel(ITEM);

    const telefono = await screen.findByDisplayValue("3001112233");
    await userEvent.clear(telefono);
    await userEvent.type(telefono, "3009998877");
    await userEvent.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const payload = onSubmit.mock.calls[0][0];

    expect(payload.phone).toBe("3009998877");
    expect(payload.companies).toHaveLength(2);
    expect(payload.companies[0].email).toBe("contacto@xyz.co");
    expect(payload.companies[1].email).toBe("hola@abc.co");
  });

  it("una compañía sin contacto registrado se envía en null, no en blanco", async () => {
    const sinContacto: LegalRepresentativeItem = {
      ...ITEM,
      companies: [{ id: "co-1", nit: "900123456-7", name: "Comercializadora XYZ", deeds: [] }],
    };
    const onSubmit = renderPanel(sinContacto);

    await screen.findByDisplayValue("900123456-7");
    await userEvent.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].companies[0]).toEqual({
      nit: "900123456-7",
      name: "Comercializadora XYZ",
      email: null,
      address: null,
      city: null,
      phone: null,
    });
  });

  it("en alta el formulario arranca en blanco", async () => {
    renderPanel(null);

    expect(screen.queryByDisplayValue("900123456-7")).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /registrar representante/i }),
    ).toBeInTheDocument();
  });
});
