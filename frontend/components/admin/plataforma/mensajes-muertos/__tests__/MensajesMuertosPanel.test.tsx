import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "@/components/admin/Toast";
import { MensajesMuertosPanel } from "../MensajesMuertosPanel";

const listMensajesMuertos = vi.fn();
const retryMensajeMuerto = vi.fn();
const discardMensajeMuerto = vi.fn();

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listMensajesMuertos: (...a: unknown[]) => listMensajesMuertos(...a),
    retryMensajeMuerto: (...a: unknown[]) => retryMensajeMuerto(...a),
    discardMensajeMuerto: (...a: unknown[]) => discardMensajeMuerto(...a),
    listCompanies: vi.fn().mockResolvedValue({ data: [{ id: "e-1", nit: "900", razonSocial: "Renting Prueba", estadoActivo: true }] }),
  },
}));

const muerto = {
  id: "0193-aaaa",
  tipo: "notificaciones.email.send",
  empresaId: "e-1",
  origen: "tramites",
  ocurridoEn: "2026-10-06T12:00:00Z",
  muertoEn: "2026-10-06T12:12:00Z",
  motivo: "CorreoNoEnviadoException",
  ultimoError: "El correo no salió (ProviderUnavailable).",
  intentos: 3,
};

function pintar() {
  return render(
    <ToastProvider>
      <MensajesMuertosPanel />
    </ToastProvider>,
  );
}

describe("MensajesMuertosPanel (HU #13358)", () => {
  beforeEach(() => {
    listMensajesMuertos.mockReset().mockResolvedValue({ mensajes: [muerto] });
    retryMensajeMuerto.mockReset().mockResolvedValue(undefined);
    discardMensajeMuerto.mockReset().mockResolvedValue(undefined);
  });

  it("AC1 — muestra cola, tipo, empresa, fecha y último error de cada mensaje", async () => {
    pintar();

    expect(await screen.findByText("El correo no salió (ProviderUnavailable).")).toBeInTheDocument();
    expect(screen.getByText("notificaciones.email.send")).toBeInTheDocument();
    expect(await screen.findByText("Renting Prueba")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Correos" })).toHaveAttribute("aria-selected", "true");
    expect(listMensajesMuertos).toHaveBeenCalledWith("correos");
  });

  it("cambia de cola a webhooks", async () => {
    pintar();
    await screen.findByText("El correo no salió (ProviderUnavailable).");

    fireEvent.click(screen.getByRole("tab", { name: "Webhooks" }));

    await waitFor(() => expect(listMensajesMuertos).toHaveBeenCalledWith("webhooks"));
  });

  it("AC1 — reintentar devuelve el mensaje a su cola y recarga", async () => {
    pintar();
    fireEvent.click(await screen.findByRole("button", { name: `Reintentar el mensaje ${muerto.id}` }));

    await waitFor(() => expect(retryMensajeMuerto).toHaveBeenCalledWith("correos", muerto.id));
    await waitFor(() => expect(listMensajesMuertos).toHaveBeenCalledTimes(2));
  });

  it("AC2 — descartar pide confirmación en la página antes de llamar al servidor", async () => {
    pintar();
    fireEvent.click(await screen.findByRole("button", { name: `Descartar el mensaje ${muerto.id}` }));

    const dialogo = await screen.findByTestId("mensajes-muertos-descartar");
    expect(discardMensajeMuerto).not.toHaveBeenCalled();
    expect(within(dialogo).getByText(/queda en la auditoría/)).toBeInTheDocument();

    fireEvent.click(within(dialogo).getByRole("button", { name: "Descartar" }));
    await waitFor(() => expect(discardMensajeMuerto).toHaveBeenCalledWith("correos", muerto.id));
  });

  it("cancelar el descarte no llama al servidor", async () => {
    pintar();
    fireEvent.click(await screen.findByRole("button", { name: `Descartar el mensaje ${muerto.id}` }));

    fireEvent.click(within(await screen.findByTestId("mensajes-muertos-descartar")).getByRole("button", { name: "Cancelar" }));

    await waitFor(() => expect(screen.queryByTestId("mensajes-muertos-descartar")).not.toBeInTheDocument());
    expect(discardMensajeMuerto).not.toHaveBeenCalled();
  });
});
