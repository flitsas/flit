// HU #14076 — Pestaña «Contenido»: listas administrables con orden editable (AC1), banner con validación de imagen y
// texto alternativo (AC2), borrador → vista previa → publicar con confirmación y fecha (AC3), enlaces legales sin
// documento oficial (AC4) y los cuatro estados de cada sección (AC5). La API (HU #14073) es un doble.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import { ContenidoTab } from "../ContenidoTab";
import { moverId } from "../PreguntasSection";
import {
  actualizarFaqPn,
  crearBannerPn,
  crearFaqPn,
  establecerBannerActivoPn,
  establecerEnlacePn,
  establecerFaqActivaPn,
  fetchBannersPn,
  fetchBorradorPn,
  fetchEnlacesPn,
  fetchFaqsPn,
  fetchImagenBannerPn,
  fetchVersionesPn,
  ordenarFaqsPn,
  publicarContenidoPn,
  type BannerPn,
  type BorradorContenidoPn,
  type EnlacePn,
  type FaqPn,
} from "@/lib/api/admin-pn-contenido";

vi.mock("@/lib/api/admin-pn-contenido", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-contenido")>();
  return {
    ...real,
    fetchFaqsPn: vi.fn(),
    crearFaqPn: vi.fn(),
    actualizarFaqPn: vi.fn(),
    establecerFaqActivaPn: vi.fn(),
    ordenarFaqsPn: vi.fn(),
    fetchBannersPn: vi.fn(),
    crearBannerPn: vi.fn(),
    actualizarBannerPn: vi.fn(),
    establecerBannerActivoPn: vi.fn(),
    ordenarBannersPn: vi.fn(),
    fetchImagenBannerPn: vi.fn(),
    fetchEnlacesPn: vi.fn(),
    establecerEnlacePn: vi.fn(),
    fetchBorradorPn: vi.fn(),
    publicarContenidoPn: vi.fn(),
    fetchVersionesPn: vi.fn(),
  };
});

const faq = (over: Partial<FaqPn>): FaqPn => ({
  id: "f1",
  pregunta: "¿Cuánto cuesta el trámite?",
  respuesta: "Depende del organismo y del tipo de trámite.",
  orden: 1,
  activa: true,
  rowVersion: 1,
  creadaEn: "2026-10-01T10:00:00Z",
  actualizadaEn: null,
  ...over,
});
const banner = (over: Partial<BannerPn>): BannerPn => ({
  id: "b1",
  titulo: "Traspasos sin filas",
  textoAlternativo: "Una persona haciendo un trámite desde el celular",
  enlaceUrl: null,
  orden: 1,
  vigenteDesde: null,
  vigenteHasta: null,
  activo: true,
  imagenSha256: "abc",
  imagenContentType: "image/png",
  imagenBytes: 1000,
  rowVersion: 1,
  creadoEn: "2026-10-01T10:00:00Z",
  actualizadoEn: null,
  ...over,
});
const ENLACES: EnlacePn[] = [
  { codigo: "POLITICA_DATOS", url: null, actualizadoEn: null },
  { codigo: "TERMINOS", url: "https://flit.test/terminos", actualizadoEn: "2026-10-02T12:00:00Z" },
  { codigo: "CONTACTO_VENTAS", url: null, actualizadoEn: null },
  { codigo: "VIDEO", url: null, actualizadoEn: null },
];
const borrador = (over: Partial<BorradorContenidoPn> = {}): BorradorContenidoPn => ({
  contenido: {
    preguntas: [{ id: "f1", pregunta: "¿Cuánto cuesta el trámite?", respuesta: "Depende del organismo.", orden: 1 }],
    banners: [
      {
        id: "b1", titulo: "Traspasos sin filas", textoAlternativo: "Una persona", enlaceUrl: null, orden: 1,
        vigenteDesde: null, vigenteHasta: null, imagenSha256: "abc", imagenContentType: "image/png",
      },
    ],
    enlaces: ENLACES,
  },
  versionPublicada: 2,
  hayCambiosSinPublicar: true,
  ...over,
});

function renderTab() {
  return render(
    <ToastProvider>
      <ContenidoTab />
    </ToastProvider>,
  );
}
const seccion = (nombre: RegExp) => screen.getByRole("region", { name: nombre });

describe("moverId", () => {
  it("intercambia con el vecino y no se sale de la lista", () => {
    expect(moverId(["a", "b", "c"], 1, -1)).toEqual(["b", "a", "c"]);
    expect(moverId(["a", "b", "c"], 1, 1)).toEqual(["a", "c", "b"]);
    expect(moverId(["a", "b"], 0, -1)).toEqual(["a", "b"]);
  });
});

describe("ContenidoTab (#14076)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchFaqsPn).mockResolvedValue([faq({}), faq({ id: "f2", pregunta: "¿Cuánto demora?", orden: 2, activa: false })]);
    vi.mocked(fetchBannersPn).mockResolvedValue([banner({}), banner({ id: "b2", titulo: "Promo", orden: 2, activo: false })]);
    vi.mocked(fetchEnlacesPn).mockResolvedValue(ENLACES);
    vi.mocked(fetchBorradorPn).mockResolvedValue(borrador());
    vi.mocked(fetchImagenBannerPn).mockResolvedValue("blob:imagen");
    vi.mocked(fetchVersionesPn).mockResolvedValue([
      { version: 2, publicadoEn: "2026-10-15T15:30:00Z", publicadoPor: "abcdef12-0000-0000-0000-000000000000", preguntas: 3, banners: 1 },
      { version: 1, publicadoEn: "2026-10-01T15:30:00Z", publicadoPor: null, preguntas: 2, banners: 0 },
    ]);
    URL.createObjectURL = vi.fn(() => "blob:local");
    URL.revokeObjectURL = vi.fn();
  });

  it("AC1: tres secciones con su tabla estándar, orden y estado; los enlaces legales sin documento lo dicen (AC4)", async () => {
    renderTab();
    expect(await screen.findByRole("table", { name: /preguntas frecuentes del portal/i })).toBeInTheDocument();
    expect(await screen.findByRole("table", { name: /banners del portal/i })).toBeInTheDocument();
    const enlaces = await screen.findByRole("table", { name: /enlaces del portal/i });
    expect(within(enlaces).getByText("Sin documento oficial")).toBeInTheDocument(); // política de datos
    expect(within(enlaces).getAllByText("Sin valor")).toHaveLength(2); // contacto de ventas y video
    expect(within(enlaces).getByText("https://flit.test/terminos")).toBeInTheDocument();
    expect(within(enlaces).getByText("02/10/2026 07:00")).toBeInTheDocument();
    const preguntas = screen.getByRole("table", { name: /preguntas frecuentes del portal/i });
    expect(within(preguntas).getByText("Inactiva")).toBeInTheDocument();
    expect(within(preguntas).getByText("¿Cuánto demora?")).toBeInTheDocument();
  });

  it("AC1: subir una pregunta guarda el orden nuevo; la primera no puede subir", async () => {
    const ue = userEvent.setup();
    vi.mocked(ordenarFaqsPn).mockResolvedValue(undefined);
    renderTab();
    const tabla = await screen.findByRole("table", { name: /preguntas frecuentes del portal/i });
    expect(within(tabla).getByRole("button", { name: /subir la pregunta «¿cuánto cuesta el trámite\?»/i })).toBeDisabled();
    await ue.click(within(tabla).getByRole("button", { name: /subir la pregunta «¿cuánto demora\?»/i }));
    await waitFor(() => expect(ordenarFaqsPn).toHaveBeenCalledWith(["f2", "f1"]));
    await waitFor(() => expect(fetchBorradorPn).toHaveBeenCalledTimes(2)); // refresca el estado de publicación
  });

  it("AC1: crea una pregunta con texto plano; el HTML se rechaza en el campo", async () => {
    const ue = userEvent.setup();
    vi.mocked(crearFaqPn).mockResolvedValue(faq({ id: "f3", pregunta: "¿Cómo pago?", respuesta: "Por transferencia.", orden: 3 }));
    renderTab();
    await screen.findByRole("table", { name: /preguntas frecuentes del portal/i });
    await ue.click(screen.getByRole("button", { name: /acciones de preguntas frecuentes/i }));
    await ue.click(await screen.findByRole("menuitem", { name: /nueva pregunta/i }));
    const dialog = await screen.findByRole("dialog", { name: /nueva pregunta frecuente/i });

    await ue.type(within(dialog).getByLabelText(/^Pregunta/), "<b>¿Cómo pago?</b>");
    await ue.type(within(dialog).getByLabelText(/^Respuesta/), "Por transferencia.");
    await ue.click(within(dialog).getByRole("button", { name: /guardar pregunta/i }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/es texto simple: no se acepta html/i);
    expect(crearFaqPn).not.toHaveBeenCalled();

    const campo = within(dialog).getByLabelText(/^Pregunta/);
    await ue.clear(campo);
    await ue.type(campo, "¿Cómo pago?");
    await ue.click(within(dialog).getByRole("button", { name: /guardar pregunta/i }));
    await waitFor(() => expect(crearFaqPn).toHaveBeenCalledWith({ pregunta: "¿Cómo pago?", respuesta: "Por transferencia.", activa: true }));
    expect(await screen.findByText("¿Cómo pago?")).toBeInTheDocument();
  });

  it("AC1: editar y desactivar una pregunta llaman a la API y se reflejan en la fila", async () => {
    const ue = userEvent.setup();
    vi.mocked(establecerFaqActivaPn).mockResolvedValue(faq({ activa: false }));
    vi.mocked(actualizarFaqPn).mockResolvedValue(faq({ pregunta: "¿Cuánto vale?" }));
    renderTab();
    const tabla = await screen.findByRole("table", { name: /preguntas frecuentes del portal/i });
    await ue.click(within(tabla).getByRole("button", { name: /desactivar la pregunta «¿cuánto cuesta el trámite\?»/i }));
    await waitFor(() => expect(establecerFaqActivaPn).toHaveBeenCalledWith("f1", false));
    await waitFor(() => expect(within(tabla).getAllByText("Inactiva")).toHaveLength(2));

    await ue.click(within(tabla).getByRole("button", { name: /editar la pregunta «¿cuánto cuesta el trámite\?»/i }));
    const dialog = await screen.findByRole("dialog", { name: /editar pregunta frecuente/i });
    const pregunta = within(dialog).getByLabelText(/^Pregunta/);
    await ue.clear(pregunta);
    await ue.type(pregunta, "¿Cuánto vale?");
    await ue.click(within(dialog).getByRole("button", { name: /guardar pregunta/i }));
    await waitFor(() => expect(actualizarFaqPn).toHaveBeenCalledWith("f1", expect.objectContaining({ pregunta: "¿Cuánto vale?" })));
  });

  it("AC2: un banner con imagen que no es JPG/PNG/WEBP, de más de 2 MB o sin texto alternativo no se guarda", async () => {
    const ue = userEvent.setup({ applyAccept: false });
    renderTab();
    await screen.findByRole("table", { name: /banners del portal/i });
    await ue.click(screen.getByRole("button", { name: /acciones de banners/i }));
    await ue.click(await screen.findByRole("menuitem", { name: /nuevo banner/i }));
    const dialog = await screen.findByRole("dialog", { name: /nuevo banner/i });
    await ue.type(within(dialog).getByLabelText(/^Título/), "Promo nueva");

    const input = within(dialog).getByLabelText("Imagen del banner");
    await ue.upload(input, new File(["x"], "documento.pdf", { type: "application/pdf" }));
    expect(await within(dialog).findByText("La imagen debe ser JPG, PNG o WEBP.")).toBeInTheDocument();

    const grande = new File(["x"], "grande.png", { type: "image/png" });
    Object.defineProperty(grande, "size", { value: 3 * 1024 * 1024 });
    await ue.upload(input, grande);
    expect(await within(dialog).findByText("La imagen no puede pesar más de 2 MB.")).toBeInTheDocument();

    await ue.click(within(dialog).getByRole("button", { name: /guardar banner/i }));
    expect(await within(dialog).findByText(/el texto alternativo es obligatorio/i)).toBeInTheDocument();
    expect(crearBannerPn).not.toHaveBeenCalled();
  });

  it("AC2: un banner válido se crea con su imagen, texto alternativo y vigencia en hora de Colombia", async () => {
    const ue = userEvent.setup();
    vi.mocked(crearBannerPn).mockResolvedValue(banner({ id: "b3", titulo: "Promo nueva", orden: 3 }));
    renderTab();
    await screen.findByRole("table", { name: /banners del portal/i });
    await ue.click(screen.getByRole("button", { name: /acciones de banners/i }));
    await ue.click(await screen.findByRole("menuitem", { name: /nuevo banner/i }));
    const dialog = await screen.findByRole("dialog", { name: /nuevo banner/i });

    await ue.type(within(dialog).getByLabelText(/^Título/), "Promo nueva");
    await ue.type(within(dialog).getByLabelText(/^Texto alternativo/), "Una persona sonriendo");
    const imagen = new File(["x"], "promo.webp", { type: "image/webp" });
    await ue.upload(within(dialog).getByLabelText("Imagen del banner"), imagen);
    expect(await within(dialog).findByAltText(/vista previa de la imagen elegida/i)).toBeInTheDocument();
    await ue.click(within(dialog).getByRole("button", { name: /guardar banner/i }));

    await waitFor(() => expect(crearBannerPn).toHaveBeenCalledTimes(1));
    expect(vi.mocked(crearBannerPn).mock.calls[0][0]).toMatchObject({
      titulo: "Promo nueva",
      textoAlternativo: "Una persona sonriendo",
      activo: true,
      archivo: imagen,
    });
    expect(await screen.findByText("Promo nueva")).toBeInTheDocument();
  });

  it("AC2: desactivar un banner usa el PATCH dedicado", async () => {
    const ue = userEvent.setup();
    vi.mocked(establecerBannerActivoPn).mockResolvedValue(banner({ activo: false }));
    renderTab();
    const tabla = await screen.findByRole("table", { name: /banners del portal/i });
    await ue.click(within(tabla).getByRole("button", { name: /desactivar el banner «traspasos sin filas»/i }));
    await waitFor(() => expect(establecerBannerActivoPn).toHaveBeenCalledWith("b1", false));
  });

  it("AC4: un enlace legal vacío se guarda como «sin documento oficial» (null); una URL inválida se marca", async () => {
    const ue = userEvent.setup();
    vi.mocked(establecerEnlacePn).mockResolvedValue({ codigo: "TERMINOS", url: null, actualizadoEn: "2026-10-28T10:00:00Z" });
    renderTab();
    const tabla = await screen.findByRole("table", { name: /enlaces del portal/i });
    await ue.click(within(tabla).getByRole("button", { name: /editar términos y condiciones/i }));
    const dialog = await screen.findByRole("dialog", { name: /editar: términos y condiciones/i });
    const campo = within(dialog).getByLabelText(/^Dirección/);
    await ue.clear(campo);
    await ue.type(campo, "ftp://flit.test/x");
    await ue.click(within(dialog).getByRole("button", { name: /guardar enlace/i }));
    expect(await within(dialog).findByText(/debe empezar por http/i)).toBeInTheDocument();
    expect(establecerEnlacePn).not.toHaveBeenCalled();

    await ue.clear(campo);
    await ue.click(within(dialog).getByRole("button", { name: /guardar enlace/i }));
    await waitFor(() => expect(establecerEnlacePn).toHaveBeenCalledWith("TERMINOS", null));
    await waitFor(() => expect(within(tabla).getAllByText("Sin documento oficial")).toHaveLength(2));
  });

  it("AC3: la vista previa muestra el borrador con el aspecto del portal sin publicarlo", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table", { name: /banners del portal/i });
    await ue.click(screen.getByRole("button", { name: /vista previa/i }));
    const dialog = await screen.findByRole("dialog", { name: /vista previa del portal/i });
    const portal = await within(dialog).findByTestId("pn-portal-preview");
    expect(within(portal).getByText("Traspasos sin filas")).toBeInTheDocument();
    expect(within(portal).getByText("¿Cuánto cuesta el trámite?")).toBeInTheDocument();
    expect(within(portal).getAllByText("Sin documento oficial").length).toBeGreaterThan(0);
    expect(within(dialog).getByText(/todavía no está publicado/i)).toBeInTheDocument();
    expect(publicarContenidoPn).not.toHaveBeenCalled();
  });

  it("AC3: Publicar pide confirmación, publica y muestra la fecha; sin cambios queda deshabilitado", async () => {
    const ue = userEvent.setup();
    vi.mocked(publicarContenidoPn).mockResolvedValue({
      version: 3,
      publicadoEn: "2026-10-28T20:15:00Z",
      publicadoPor: null,
      contenido: borrador().contenido,
    });
    renderTab();
    const publicar = await screen.findByRole("button", { name: /^publicar$/i });
    await waitFor(() => expect(publicar).toBeEnabled());
    expect(screen.getByText("Hay cambios sin publicar")).toBeInTheDocument();
    expect(screen.getByText("Versión publicada: 2")).toBeInTheDocument();

    vi.mocked(fetchBorradorPn).mockResolvedValue(borrador({ hayCambiosSinPublicar: false, versionPublicada: 3 }));
    await ue.click(publicar);
    const dialog = await screen.findByRole("dialog", { name: /publicar el contenido/i });
    expect(within(dialog).getByText(/versión nueva \(3\)/i)).toBeInTheDocument();
    expect(publicarContenidoPn).not.toHaveBeenCalled(); // todavía no: pidió confirmación
    await ue.click(within(dialog).getByRole("button", { name: /publicar ahora/i }));

    expect(await screen.findByText(/contenido publicado el 28\/10\/2026 15:15 \(versión 3\)/i)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText("Sin cambios por publicar")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /^publicar$/i })).toBeDisabled();
  });

  it("AC3: un 409 «sin cambios por publicar» se explica dentro de la confirmación", async () => {
    const ue = userEvent.setup();
    vi.mocked(publicarContenidoPn).mockRejectedValue(
      new ApiError(409, "sin_cambios_por_publicar", { error: "sin_cambios_por_publicar", message: "No hay cambios por publicar: el contenido ya es el publicado." }),
    );
    renderTab();
    const publicar = await screen.findByRole("button", { name: /^publicar$/i });
    await waitFor(() => expect(publicar).toBeEnabled());
    await ue.click(publicar);
    const dialog = await screen.findByRole("dialog", { name: /publicar el contenido/i });
    await ue.click(within(dialog).getByRole("button", { name: /publicar ahora/i }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/no hay cambios por publicar/i);
  });

  it("AC3: el historial de versiones lista cuándo y quién publicó", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table", { name: /banners del portal/i });
    await ue.click(screen.getByRole("button", { name: /historial de versiones/i }));
    const tabla = await screen.findByRole("table", { name: /versiones publicadas del contenido/i });
    expect(within(tabla).getByText("15/10/2026 10:30")).toBeInTheDocument();
    expect(within(tabla).getByText("abcdef12")).toBeInTheDocument();
  });

  it("AC5: cada sección muestra su vacío, su error con reintento y su carga", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchFaqsPn).mockResolvedValueOnce([]);
    vi.mocked(fetchBannersPn).mockRejectedValueOnce(new Error("caído"));
    vi.mocked(fetchEnlacesPn).mockReturnValueOnce(new Promise(() => {}));
    renderTab();
    expect(await screen.findByText(/aún no hay preguntas frecuentes/i)).toBeInTheDocument();
    expect(await screen.findByText(/no se pudieron cargar los banners/i)).toBeInTheDocument();
    expect(screen.getByText(/cargando enlaces/i)).toBeInTheDocument();
    await ue.click(within(seccion(/^banners$/i)).getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByRole("table", { name: /banners del portal/i })).toBeInTheDocument();
  });

  it("si no se puede consultar el estado de publicación lo dice y deja reintentar", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchBorradorPn).mockRejectedValueOnce(new Error("caído"));
    renderTab();
    expect(await screen.findByText("No se pudo consultar el estado")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^publicar$/i })).toBeDisabled();
    await ue.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Hay cambios sin publicar")).toBeInTheDocument();
  });
});
