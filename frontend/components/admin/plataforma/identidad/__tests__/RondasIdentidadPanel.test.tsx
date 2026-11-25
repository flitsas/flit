import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { RondasIdentidadPanel } from "@/components/admin/plataforma/identidad/RondasIdentidadPanel";
import AdminRondasIdentidadPage from "@/app/admin/plataforma/rondas-identidad/page";
import {
  getIdentityRoundSettings,
  putIdentityRoundSettings,
  type IdentityRoundSettings,
} from "@/lib/api/admin-identity-round-settings";
import { ApiError } from "@/lib/api/types";

// Uso de ejemplo: <RondasIdentidadPanel /> dentro de /admin/plataforma/rondas-identidad (solo SuperAdmin de FLIT).

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock("@/lib/api/admin-identity-round-settings", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-identity-round-settings")>();
  return { ...real, getIdentityRoundSettings: vi.fn(), putIdentityRoundSettings: vi.fn() };
});

const getMock = vi.mocked(getIdentityRoundSettings);
const putMock = vi.mocked(putIdentityRoundSettings);

const AJUSTES: IdentityRoundSettings = {
  rondasMaximas: 3,
  disparadorAlerta: "al_maximo",
  correosAlerta: ["ops@flit.co"],
};

async function cargado() {
  render(<RondasIdentidadPanel />);
  return screen.findByRole("form", { name: /rondas de validación/i });
}

const rondas = () => screen.getByRole("spinbutton", { name: /rondas máximas/i });
const guardar = () => screen.getByRole("button", { name: /^guardar$/i });
const descartar = () => screen.getByRole("button", { name: /descartar cambios/i });

async function escribirRondas(v: string) {
  await userEvent.clear(rondas());
  if (v) await userEvent.type(rondas(), v);
}

async function agregar(correo: string) {
  await userEvent.type(screen.getByLabelText(/agregar correo/i), correo);
  await userEvent.click(screen.getByRole("button", { name: /^agregar$/i }));
}

describe("RondasIdentidadPanel", () => {
  beforeEach(() => {
    getMock.mockReset();
    putMock.mockReset();
  });

  it("cargando: estado accesible mientras llega el GET", () => {
    getMock.mockReturnValue(new Promise(() => {}));
    render(<RondasIdentidadPanel />);
    expect(screen.getByTestId("rondas-identidad-loading")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(/cargando/i);
  });

  it("error al cargar: mensaje y «Reintentar» vuelve a pedir el GET", async () => {
    getMock.mockRejectedValueOnce(new ApiError(500, "x")).mockResolvedValueOnce(AJUSTES);
    render(<RondasIdentidadPanel />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/no se pudo cargar/i);
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByRole("form", { name: /rondas de validación/i })).toBeInTheDocument();
    expect(getMock).toHaveBeenCalledTimes(2);
  });

  it("403 al cargar: «Sin permiso» y sin reintento", async () => {
    getMock.mockRejectedValue(new ApiError(403, "x"));
    render(<RondasIdentidadPanel />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/sin permiso/i);
    expect(screen.queryByRole("button", { name: /reintentar/i })).not.toBeInTheDocument();
  });

  it("lleno: muestra valores, disparador, correos y el aviso de datos completos; Guardar y Descartar deshabilitados", async () => {
    getMock.mockResolvedValue(AJUSTES);
    await cargado();
    expect(rondas()).toHaveValue(3);
    expect(screen.getByRole("radio", { name: /al llegar al máximo/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /cada ronda rechazada/i })).not.toBeChecked();
    expect(within(screen.getByTestId("rondas-identidad-correos")).getByText("ops@flit.co")).toBeInTheDocument();
    expect(screen.getByTestId("rondas-identidad-aviso-datos")).toHaveTextContent(/datos completos de la persona/i);
    expect(guardar()).toBeDisabled();
    expect(descartar()).toBeDisabled();
  });

  it("vacío: sin correos muestra el estado vacío", async () => {
    getMock.mockResolvedValue({ ...AJUSTES, correosAlerta: [] });
    await cargado();
    expect(screen.getByTestId("rondas-identidad-vacio")).toHaveTextContent(/sin correos configurados/i);
  });

  it("rango: 0 y 11 marcan error y bloquean Guardar; 10 lo habilita", async () => {
    getMock.mockResolvedValue(AJUSTES);
    await cargado();
    await escribirRondas("0");
    expect(rondas()).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent(/entre 1 y 10/i);
    expect(guardar()).toBeDisabled();
    await escribirRondas("11");
    expect(guardar()).toBeDisabled();
    await escribirRondas("10");
    expect(guardar()).toBeEnabled();
  });

  it("correo inválido y duplicado: error anunciado y no se agrega", async () => {
    getMock.mockResolvedValue(AJUSTES);
    await cargado();
    await agregar("no-es-correo");
    expect(screen.getByRole("alert")).toHaveTextContent(/formato válido/i);
    expect(screen.getByLabelText(/agregar correo/i)).toHaveAttribute("aria-invalid", "true");
    await userEvent.clear(screen.getByLabelText(/agregar correo/i));
    await agregar("OPS@flit.co");
    expect(screen.getByRole("alert")).toHaveTextContent(/ya está en la lista/i);
    expect(within(screen.getByTestId("rondas-identidad-correos")).getAllByRole("listitem")).toHaveLength(1);
    expect(guardar()).toBeDisabled();
  });

  it("máximo 20 correos: con la lista llena no se puede agregar", async () => {
    const veinte = Array.from({ length: 20 }, (_, i) => `u${i}@flit.co`);
    getMock.mockResolvedValue({ ...AJUSTES, correosAlerta: veinte });
    await cargado();
    expect(screen.getByLabelText(/agregar correo/i)).toBeDisabled();
    expect(screen.getByRole("button", { name: /^agregar$/i })).toBeDisabled();
  });

  it("guardado correcto: envía el cuerpo, confirma visiblemente y deja Guardar deshabilitado", async () => {
    getMock.mockResolvedValue(AJUSTES);
    const guardado: IdentityRoundSettings = {
      rondasMaximas: 5,
      disparadorAlerta: "cada_ronda",
      correosAlerta: ["ops@flit.co", "nuevo@flit.co"],
    };
    putMock.mockResolvedValue(guardado);
    await cargado();
    await escribirRondas("5");
    await userEvent.click(screen.getByRole("radio", { name: /cada ronda rechazada/i }));
    await agregar("Nuevo@flit.co");
    expect(guardar()).toBeEnabled();
    await userEvent.click(guardar());
    await waitFor(() => expect(putMock).toHaveBeenCalledWith(guardado));
    expect(await screen.findByText(/configuración guardada/i)).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(/configuración guardada/i);
    expect(guardar()).toBeDisabled();
  });

  it("quitar un correo habilita Guardar y «Descartar cambios» restaura lo persistido", async () => {
    getMock.mockResolvedValue(AJUSTES);
    await cargado();
    await userEvent.click(screen.getByRole("button", { name: /quitar ops@flit\.co/i }));
    expect(screen.getByTestId("rondas-identidad-vacio")).toBeInTheDocument();
    expect(guardar()).toBeEnabled();
    await userEvent.click(descartar());
    expect(within(screen.getByTestId("rondas-identidad-correos")).getByText("ops@flit.co")).toBeInTheDocument();
    expect(guardar()).toBeDisabled();
  });

  it("400 del servidor: el detalle se pinta junto al campo correspondiente", async () => {
    getMock.mockResolvedValue(AJUSTES);
    putMock.mockRejectedValue(
      new ApiError(400, "x", {
        details: ["rondasMaximas fuera de rango", "El correo ops2@flit.co está duplicado"],
      }),
    );
    await cargado();
    await escribirRondas("4");
    await userEvent.click(guardar());
    expect(await screen.findByText(/fuera de rango/i)).toBeInTheDocument();
    expect(rondas()).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText(/ops2@flit\.co está duplicado/i)).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(/no se guardó/i);
  });

  it("403 al guardar: mensaje de permiso y se conserva lo escrito", async () => {
    getMock.mockResolvedValue(AJUSTES);
    putMock.mockRejectedValue(new ApiError(403, "x"));
    await cargado();
    await escribirRondas("4");
    await userEvent.click(guardar());
    expect(await screen.findAllByText(/solo para el superadmin de flit/i)).not.toHaveLength(0);
    expect(rondas()).toHaveValue(4);
  });
});

describe("AdminRondasIdentidadPage", () => {
  it("renderiza el título y el panel", async () => {
    getMock.mockResolvedValue(AJUSTES);
    render(<AdminRondasIdentidadPage />);
    expect(screen.getByRole("heading", { name: /rondas de identidad/i })).toBeInTheDocument();
    expect(await screen.findByRole("form", { name: /rondas de validación/i })).toBeInTheDocument();
  });
});
