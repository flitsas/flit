// HU #14075 AC3/AC4/AC5 — Pestaña «Ajustes del portal»: rangos visibles que impiden guardar, guardado con `rowVersion`,
// conflicto 409 que conserva lo escrito y ofrece recargar, y los estados de carga y error. La API es un doble.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import { ParametrosTab } from "../ParametrosTab";
import { fetchParametrosPn, guardarParametrosPn, type ParametrosPn } from "@/lib/api/admin-pn-config";

vi.mock("@/lib/api/admin-pn-config", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-config")>();
  return { ...real, fetchParametrosPn: vi.fn(), guardarParametrosPn: vi.fn() };
});

const BASE: ParametrosPn = {
  sesionVigenciaMinutos: 60,
  borradorVigenciaDias: null,
  topeConsultasCiudadanoDia: 10,
  topeConsultasGlobalDia: null,
  captchaActivo: true,
  soporteCorreo: "soporte@flit.test",
  soporteTelefono: null,
  soporteHorario: null,
  rowVersion: 3,
  actualizadoEn: "2026-10-20T15:00:00Z",
  actualizadoPor: null,
};

function renderTab() {
  render(
    <ToastProvider>
      <ParametrosTab />
    </ToastProvider>,
  );
}

describe("ParametrosTab (#14075)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchParametrosPn).mockResolvedValue(BASE);
  });

  it("carga los valores, muestra el rango en cada campo y no deja guardar sin cambios", async () => {
    renderTab();
    expect(await screen.findByLabelText(/duración de la sesión/i)).toHaveValue("60");
    expect(screen.getByText(/permitido: de 15 a 1440 minutos/i)).toBeInTheDocument();
    expect(screen.getByText(/permitido: de 1 a 90 días/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/días que se guarda un trámite/i)).toHaveValue("");
    expect(screen.getByLabelText(/verificación anti-robots/i)).toHaveValue("si");
    expect(screen.getByText(/última actualización: 20\/10\/2026/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /guardar ajustes/i })).toBeDisabled();
  });

  it("AC3: un valor fuera de rango muestra el rango permitido y no guarda", async () => {
    const ue = userEvent.setup();
    renderTab();
    const sesion = await screen.findByLabelText(/duración de la sesión/i);
    await ue.clear(sesion);
    await ue.type(sesion, "5");
    await ue.click(screen.getByRole("button", { name: /guardar ajustes/i }));
    expect(await screen.findByText(/escribe un número entero entre 15 y 1440 minutos/i)).toBeInTheDocument();
    expect(sesion).toHaveAttribute("aria-invalid", "true");

    const ciudadano = screen.getByLabelText(/consultas por persona al día/i);
    await ue.clear(ciudadano);
    await ue.type(ciudadano, "abc");
    await ue.click(screen.getByRole("button", { name: /guardar ajustes/i }));
    expect(await screen.findByText(/entre 1 y 100 consultas/i)).toBeInTheDocument();
    expect(guardarParametrosPn).not.toHaveBeenCalled();
  });

  it("valida el correo y el teléfono de soporte", async () => {
    const ue = userEvent.setup();
    renderTab();
    const correo = await screen.findByLabelText(/correo de soporte/i);
    await ue.clear(correo);
    await ue.type(correo, "sin-arroba");
    await ue.type(screen.getByLabelText(/teléfono de soporte/i), "abc");
    await ue.click(screen.getByRole("button", { name: /guardar ajustes/i }));
    expect(await screen.findByText(/escribe un correo válido/i)).toBeInTheDocument();
    expect(screen.getByText(/teléfono de 7 a 30 caracteres/i)).toBeInTheDocument();
    expect(guardarParametrosPn).not.toHaveBeenCalled();
  });

  it("guarda con la versión leída, los vacíos viajan como nulos y se confirma con un mensaje", async () => {
    const ue = userEvent.setup();
    vi.mocked(guardarParametrosPn).mockResolvedValue({ ...BASE, sesionVigenciaMinutos: 120, rowVersion: 4 });
    renderTab();
    const sesion = await screen.findByLabelText(/duración de la sesión/i);
    await ue.clear(sesion);
    await ue.type(sesion, "120");
    await ue.click(screen.getByRole("button", { name: /guardar ajustes/i }));

    await waitFor(() => expect(guardarParametrosPn).toHaveBeenCalledTimes(1));
    expect(guardarParametrosPn).toHaveBeenCalledWith(
      {
        sesionVigenciaMinutos: 120,
        borradorVigenciaDias: null,
        topeConsultasCiudadanoDia: 10,
        topeConsultasGlobalDia: null,
        captchaActivo: true,
        soporteCorreo: "soporte@flit.test",
        soporteTelefono: null,
        soporteHorario: null,
      },
      3,
    );
    expect(await screen.findByText(/ajustes guardados/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /guardar ajustes/i })).toBeDisabled();
  });

  it("AC4: un 409 muestra el conflicto, conserva lo escrito y «Recargar» trae los valores actuales", async () => {
    const ue = userEvent.setup();
    const actual: ParametrosPn = { ...BASE, sesionVigenciaMinutos: 30, rowVersion: 7 };
    vi.mocked(guardarParametrosPn).mockRejectedValue(
      new ApiError(409, "conflicto_de_version", { error: "conflicto_de_version", message: "x", actual }),
    );
    renderTab();
    const sesion = await screen.findByLabelText(/duración de la sesión/i);
    await ue.clear(sesion);
    await ue.type(sesion, "90");
    await ue.click(screen.getByRole("button", { name: /guardar ajustes/i }));

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent(/alguien más cambió estos ajustes/i);
    expect(sesion).toHaveValue("90"); // no se pierde lo escrito

    await ue.click(screen.getByRole("button", { name: /cargar valores actuales/i }));
    expect(screen.getByLabelText(/duración de la sesión/i)).toHaveValue("30");
    expect(screen.queryByRole("button", { name: /cargar valores actuales/i })).not.toBeInTheDocument();

    // Con la versión nueva, el siguiente guardado ya viaja con rowVersion 7.
    vi.mocked(guardarParametrosPn).mockResolvedValue({ ...actual, sesionVigenciaMinutos: 90, rowVersion: 8 });
    const s2 = screen.getByLabelText(/duración de la sesión/i);
    await ue.clear(s2);
    await ue.type(s2, "90");
    await ue.click(screen.getByRole("button", { name: /guardar ajustes/i }));
    await waitFor(() => expect(guardarParametrosPn).toHaveBeenLastCalledWith(expect.anything(), 7));
  });

  it("un 400 del servidor se marca en su campo", async () => {
    const ue = userEvent.setup();
    vi.mocked(guardarParametrosPn).mockRejectedValue(
      new ApiError(400, "correo_invalido", { error: "correo_invalido", campo: "soporteCorreo", message: "El correo de soporte no tiene un formato válido." }),
    );
    renderTab();
    const horario = await screen.findByLabelText(/horario de atención/i);
    await ue.type(horario, "L-V");
    await ue.click(screen.getByRole("button", { name: /guardar ajustes/i }));
    expect(await screen.findByText("El correo de soporte no tiene un formato válido.")).toBeInTheDocument();
  });

  it("AC5: error con reintento y cargando con el CarLoaderModal", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchParametrosPn).mockRejectedValueOnce(new Error("caído"));
    const { unmount } = render(
      <ToastProvider>
        <ParametrosTab />
      </ToastProvider>,
    );
    expect(await screen.findByText(/no se pudieron cargar los ajustes/i)).toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByLabelText(/duración de la sesión/i)).toBeInTheDocument();
    unmount();

    vi.mocked(fetchParametrosPn).mockReturnValue(new Promise(() => {}));
    renderTab();
    expect(screen.getByText(/cargando ajustes/i)).toBeInTheDocument();
  });

  it("explica en lenguaje claro qué pasa con un campo vacío y permite descartar los cambios sin guardar", async () => {
    const ue = userEvent.setup();
    renderTab();
    const sesion = await screen.findByLabelText(/duración de la sesión/i);
    expect(screen.getByText(/si dejas un campo vacío, el portal usa su valor habitual/i)).toBeInTheDocument();
    expect(screen.getByText(/no hay cambios por guardar/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /descartar cambios/i })).not.toBeInTheDocument();

    await ue.clear(sesion);
    await ue.type(sesion, "99");
    await ue.click(screen.getByRole("button", { name: /descartar cambios/i }));
    expect(sesion).toHaveValue("60");
    expect(guardarParametrosPn).not.toHaveBeenCalled();
  });
});
