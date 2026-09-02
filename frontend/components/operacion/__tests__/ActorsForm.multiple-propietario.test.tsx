// Múltiple Propietario (ADR-0053) — UI de pestañas + reparto porcentual en `ActorsForm.tsx`.
// La lógica pura (solidario, redistribución, reindexado de mapas posicionales) ya está cubierta
// exhaustivamente en `frontend/lib/tramites/__tests__/ownership-share.test.ts`, sin RTL. Este
// archivo verifica que `ActorsForm.tsx` la conecta correctamente al DOM: el caso de un solo actor
// no sufre regresión, las pestañas/porcentaje aparecen y se comportan como pide el encargo, los dos
// mensajes de bloqueo son exactos, el máximo de 4 se respeta, y el estado de identidad por actor
// (consulta RUNT) no se mezcla al agregar/quitar copropietarios.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  actorContactLookup: vi.fn(),
  getBiometricState: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getActors: mocks.getActors,
    saveActors: mocks.saveActors,
    runtPersonLookup: mocks.runtPersonLookup,
    ruesPersonLookup: mocks.ruesPersonLookup,
    getInstance: mocks.getInstance,
    patchFieldValues: mocks.patchFieldValues,
    lookupLegalRepresentativeByNit: mocks.lookupLegalRepresentativeByNit,
    actorContactLookup: mocks.actorContactLookup,
    getBiometricState: mocks.getBiometricState,
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';

const INSTANCE = 'inst-mp-1';

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.patchFieldValues.mockResolvedValue(undefined);
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.actorContactLookup.mockResolvedValue({ found: false });
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.runtPersonLookup.mockResolvedValue({
    found: true,
    fullName: 'Persona Encontrada',
    firstName: 'Persona',
    lastName: 'Encontrada',
    documentType: 'CC',
    documentNumber: '111',
    source: 'RUNT',
    mode: 'mock',
  });
});

function addButton(sideLabel: string) {
  return screen.getByRole('button', { name: `Agregar copropietario de ${sideLabel}` });
}

// CAMBIO DE COMPORTAMIENTO (decisión del usuario, imagen de referencia): con un solo actor la fila
// de pestañas SÍ se ve — una sola píldora "Comprador 1  100%" activa, sin ×, más el botón "+". Lo
// que sigue sin verse con un solo actor es el BLOQUE de porcentaje (slider/casilla/consolidado):
// eso solo aparece desde el segundo propietario (`revealed`, sin cambios). Antes de este ajuste la
// fila de pestañas completa estaba detrás de `revealed` (una fila punteada "¿Hay más de un
// propietario?" hacía de disparador); esa fila punteada no existe en el diseño y se retiró.
describe('ActorsForm — Múltiple Propietario, un solo actor (regresión, caso mayoritario)', () => {
  it('matrícula inicial con un comprador: pestaña única al 100%, sin bloque de porcentaje', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText(/Número de documento/);

    expect(screen.getByRole('tablist')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Comprador 1 100%' })).toBeInTheDocument();
    // El ordinal=1 nunca se elimina: sin botón "×" con un solo propietario.
    expect(screen.queryByRole('button', { name: 'Quitar Comprador 1' })).toBeNull();
    // El bloque de porcentaje (slider/casilla/consolidado) sigue sin verse hasta el segundo.
    expect(screen.queryByText(/Porcentaje de propiedad/)).toBeNull();
    expect(addButton('comprador')).toBeInTheDocument();
  });

  it('traspaso con un vendedor y un comprador: ambos lados muestran su pestaña única al 100%', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);
    const vendedorCard = await screen.findByRole('group', { name: 'Vendedor' });
    const compradorCard = screen.getByRole('group', { name: 'Comprador' });

    expect(within(vendedorCard).getByRole('tab', { name: 'Vendedor 1 100%' })).toBeInTheDocument();
    expect(within(compradorCard).getByRole('tab', { name: 'Comprador 1 100%' })).toBeInTheDocument();
    expect(screen.queryByText(/Porcentaje de propiedad/)).toBeNull();
    expect(addButton('vendedor')).toBeInTheDocument();
    expect(addButton('comprador')).toBeInTheDocument();
  });
});

describe('ActorsForm — Múltiple Propietario, agregar/quitar copropietarios (matrícula inicial)', () => {
  async function addSecondComprador(user: ReturnType<typeof userEvent.setup>) {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText(/Número de documento/);
    await user.click(addButton('comprador'));
    await screen.findByRole('tablist');
  }

  it('al agregar el segundo aparecen las pestañas con el rótulo del rol + ordinal, y el bloque de %', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    expect(screen.getByRole('tab', { name: /Comprador 1/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Comprador 2/ })).toBeInTheDocument();
    expect(screen.getByText(/Porcentaje de propiedad/)).toBeInTheDocument();
    // Reparto por defecto 50/50 — el solidario (Comprador 1) absorbe el residuo.
    expect(screen.getByRole('tab', { name: /Comprador 1 50%/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Comprador 2 50%/ })).toBeInTheDocument();
  });

  it('el bloque de porcentaje va DESPUÉS de los datos del actor (Datos de contacto), no junto a las pestañas', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    const tablist = screen.getByRole('tablist');
    const datosContacto = screen.getByText('Datos de contacto');
    const panel = screen.getByText(/Porcentaje de propiedad/);

    // DOCUMENT_POSITION_PRECEDING: el nodo de referencia aparece ANTES en el árbol del DOM.
    expect(datosContacto.compareDocumentPosition(tablist) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
    expect(panel.compareDocumentPosition(datosContacto) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
  });

  it('el tab activo y el panel de porcentaje se enlazan por aria-controls/aria-labelledby aunque estén lejos en el DOM', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    const activeTab = screen.getByRole('tab', { name: /Comprador 2/ }); // recién agregado, queda activo
    const panel = screen.getByRole('tabpanel');

    expect(activeTab).toHaveAttribute('aria-controls', panel.id);
    expect(panel).toHaveAttribute('aria-labelledby', activeTab.id);
  });

  it('el solidario (ordinal=1) absorbe el residuo mientras no se edite a mano', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    // La pestaña 2 queda activa tras agregar (foco natural en lo recién creado).
    const input2 = screen.getByLabelText(/Porcentaje exacto de Comprador 2/) as HTMLInputElement;
    await user.clear(input2);
    await user.type(input2, '30');

    await waitFor(() =>
      expect(screen.getByRole('tab', { name: /Comprador 1 70%/ })).toBeInTheDocument(),
    );
  });

  it('al editar el solidario a mano, deja de absorber el residuo', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    await user.click(screen.getByRole('tab', { name: /Comprador 1/ }));
    const input1 = screen.getByLabelText(/Porcentaje exacto de Comprador 1/) as HTMLInputElement;
    await user.clear(input1);
    await user.type(input1, '40');
    await waitFor(() => expect(input1).toHaveValue(40));

    // Cambia el agregado: el solidario YA NO se recalcula — queda fijo en 40, aunque la suma se rompa.
    await user.click(screen.getByRole('tab', { name: /Comprador 2/ }));
    const input2 = screen.getByLabelText(/Porcentaje exacto de Comprador 2/) as HTMLInputElement;
    await user.clear(input2);
    await user.type(input2, '10');

    await user.click(screen.getByRole('tab', { name: /Comprador 1/ }));
    const input1Again = screen.getByLabelText(/Porcentaje exacto de Comprador 1/) as HTMLInputElement;
    expect(input1Again).toHaveValue(40);
  });

  // CAMBIO DE COMPORTAMIENTO (decisión del usuario, con capturas de por medio): el bloque
  // "Porcentaje de propiedad" ya NO tiene memoria histórica. Antes, una vez `revealed` quedaba en
  // `true` para siempre (regla previa: "al eliminar el segundo, el primero queda con 100%
  // escrito" se leía como "y el bloque se queda visible"). El usuario mandó una captura con un
  // solo propietario donde el bloque SÍ se veía y corrigió: "esto debería aparecer únicamente
  // cuando se escoja otro propietario, si solo hay un propietario... esto se oculta". La fila de
  // pestañas sigue viéndose siempre (eso no cambió) y el 100% del ordinal=1 se sigue escribiendo
  // (la lógica de `redistributeAfterRemoval` no se tocó) — lo único que cambia es que el bloque de
  // porcentaje deja de montarse en cuanto el lado vuelve a tener un solo propietario.
  it('al eliminar la segunda pestaña, la primera queda con 100% y el bloque de porcentaje se oculta de nuevo', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    await user.click(screen.getByRole('button', { name: 'Quitar Comprador 2' }));

    await waitFor(() => expect(screen.queryByRole('tab', { name: /Comprador 2/ })).toBeNull());
    // La pestaña sigue viéndose (nunca se oculta) y el 100% se escribe en el ordinal=1…
    expect(screen.getByRole('tab', { name: 'Comprador 1 100%' })).toBeInTheDocument();
    // …pero el bloque de porcentaje (slider/casilla/consolidado) desaparece: de vuelta a un solo
    // propietario, es "el normal de siempre".
    expect(screen.queryByText(/Porcentaje de propiedad/)).toBeNull();
  });

  it('máximo 4 propietarios por lado: el botón "+" se deshabilita al llegar al límite', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText(/Número de documento/);

    await user.click(addButton('comprador'));
    await screen.findByRole('tablist');
    await user.click(addButton('comprador'));
    await user.click(addButton('comprador'));

    await waitFor(() => expect(screen.getByRole('tab', { name: /Comprador 4/ })).toBeInTheDocument());
    expect(addButton('comprador')).toBeDisabled();
  });

  it('los dos mensajes de bloqueo son distintos y textuales (no paráfrasis)', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    // Suma != 100: el solidario en 40, el agregado se queda en 50 (sin tocarlo) → suma 90.
    await user.click(screen.getByRole('tab', { name: /Comprador 1/ }));
    const input1 = screen.getByLabelText(/Porcentaje exacto de Comprador 1/) as HTMLInputElement;
    await user.clear(input1);
    await user.type(input1, '40');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(
      await screen.findByText('La suma de los porcentajes debe ser exactamente 100%.'),
    ).toBeInTheDocument();
    expect(
      screen.queryByText('Todos los propietarios deben tener un porcentaje mayor a 0%.'),
    ).toBeNull();

    // Ahora el agregado a 0%: el solidario vuelve a absorber (100), pero el agregado queda en 0.
    await user.click(screen.getByRole('tab', { name: /Comprador 2/ }));
    const input2 = screen.getByLabelText(/Porcentaje exacto de Comprador 2/) as HTMLInputElement;
    await user.clear(input2);
    await user.type(input2, '0');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(
      await screen.findByText('Todos los propietarios deben tener un porcentaje mayor a 0%.'),
    ).toBeInTheDocument();
    expect(mocks.saveActors).not.toHaveBeenCalled();
  });
});

describe('ActorsForm — Múltiple Propietario, sin estado fantasma al desplazar índices', () => {
  it('la consulta RUNT del vendedor#1 sobrevive a insertar un vendedor#2 antes del comprador', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);

    const vendedorCard = (await screen.findByRole('group', { name: 'Vendedor' })) as HTMLElement;
    const compradorCard = screen.getByRole('group', { name: 'Comprador' }) as HTMLElement;

    // Consulta RUNT del vendedor único (ordinal=1, índice 0 en el array `actors`).
    await user.type(within(vendedorCard).getByLabelText(/Número de documento/), '111');
    await user.click(within(vendedorCard).getByRole('button', { name: /Consultar RUNT/ }));
    await within(vendedorCard).findByText(/Persona encontrada en RUNT/i);

    // El comprador (índice 1, ANTES de agregar) sigue vacío y sin consultar.
    expect((within(compradorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');

    // Agrega un 2do vendedor: se inserta EN el índice 1 (justo tras el vendedor#1), desplazando al
    // comprador de índice 1 → 2. Sin reindexar los mapas posicionales, la consulta RUNT del
    // vendedor#1 podría "saltar" al actor equivocado tras este desplazamiento.
    await user.click(within(vendedorCard).getByRole('button', { name: 'Agregar copropietario de vendedor' }));
    await within(vendedorCard).findByRole('tablist');

    // Vuelve a la pestaña del vendedor#1: su consulta sigue siendo LA SUYA, no se perdió ni se
    // reasoció al vendedor#2 recién creado (que debe seguir sin consultar).
    await user.click(within(vendedorCard).getByRole('tab', { name: /Vendedor 1/ }));
    expect(within(vendedorCard).getByText(/Persona encontrada en RUNT/i)).toBeInTheDocument();

    await user.click(within(vendedorCard).getByRole('tab', { name: /Vendedor 2/ }));
    expect(within(vendedorCard).queryByText(/Persona encontrada en RUNT/i)).toBeNull();
    expect((within(vendedorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');

    // El comprador (ahora en índice 2) tampoco heredó ni perdió nada: sigue vacío, tal cual estaba.
    expect((within(compradorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');
    expect(mocks.runtPersonLookup).toHaveBeenCalledTimes(1);
  });
});

describe('ActorsForm — Múltiple Propietario, toda la píldora es clicable (no solo el texto)', () => {
  it('el <button role="tab"> ABSORBE el borde/alto/padding de la píldora — no vive en un <div> envolvente sin onClick', async () => {
    // Regresión estructural: jsdom no hace hit-testing por coordenadas, así que un
    // `fireEvent.click` en el nodo `role="tab"` "funciona" tanto si el borde/padding están en ese
    // botón como si están en un `<div>` externo sin manejador — no distingue el bug real (clic en
    // el borde/padding, que en un navegador de verdad cae en el `<div>` mudo). La única forma
    // fiable de blindar esto sin un navegador real es verificar que el propio nodo `role="tab"`
    // es quien carga las clases de caja completa (borde, alto, relleno): si alguien vuelve a
    // mover esas clases a un envolvente, esta aserción se rompe.
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText(/Número de documento/);
    await user.click(addButton('comprador'));
    await screen.findByRole('tablist');

    const tab1 = screen.getByRole('tab', { name: /Comprador 1/ });
    const tab2 = screen.getByRole('tab', { name: /Comprador 2/ });
    for (const tab of [tab1, tab2]) {
      expect(tab.className).toMatch(/\bborder\b/);
      expect(tab.className).toMatch(/\bh-9\b/);
      expect(tab.className).toMatch(/\bpl-3\b/);
    }

    // La "×" (pestaña 2, activa y eliminable) es un <button> HERMANO, nunca anidado dentro del
    // tab — anidar controles interactivos es HTML inválido.
    const removeBtn = screen.getByRole('button', { name: 'Quitar Comprador 2' });
    expect(tab2.contains(removeBtn)).toBe(false);
    expect(removeBtn.parentElement).toBe(tab2.parentElement);
  });

  it('un clic en el botón "×" NO cambia la pestaña activa (solo elimina)', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText(/Número de documento/);
    await user.click(addButton('comprador'));
    await screen.findByRole('tablist');
    await user.click(addButton('comprador')); // Comprador 3 queda activa

    const tab3 = screen.getByRole('tab', { name: /Comprador 3/ });
    expect(tab3).toHaveAttribute('aria-selected', 'true');

    // Vuelve a Comprador 1 y desde ahí intenta "×" en Comprador 3 (ya no está activa: sin ×).
    await user.click(screen.getByRole('tab', { name: /Comprador 1/ }));
    expect(screen.getByRole('tab', { name: /Comprador 1/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByRole('button', { name: 'Quitar Comprador 3' })).toBeNull();

    // Reactiva Comprador 3 y ahora sí elimínala con "×": la pestaña activa pasa a otra, no se
    // queda "colgada" en la eliminada ni salta a una distinta de lo esperado.
    await user.click(screen.getByRole('tab', { name: /Comprador 3/ }));
    await user.click(screen.getByRole('button', { name: 'Quitar Comprador 3' }));
    expect(screen.queryByRole('tab', { name: /Comprador 3/ })).toBeNull();
  });
});

describe('ActorsForm — Múltiple Propietario, reemplazo de contenido (una tarjeta, nunca apiladas)', () => {
  it('matrícula inicial: el contenido se reemplaza al cambiar de pestaña, y sobrevive limpio a la transición isSplit → MULTI → isSplit', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    // Fase 1 (layout "isSplit", un solo actor): el gestor ya escribió el documento del primero.
    const doc = await screen.findByLabelText(/Número de documento/);
    await user.type(doc, '111');
    expect((screen.getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('111');

    // Fase 2: agrega el segundo → abandona "isSplit", cae al layout MULTI. `getByLabelText`
    // exige UN solo nodo con ese nombre accesible: si hubiera dos tarjetas apiladas (la vieja Y
    // la nueva), esta misma línea ya fallaría por ambigüedad.
    await user.click(addButton('comprador'));
    await screen.findByRole('tablist');

    // La pestaña nueva (Comprador 2) queda activa, y su documento es el de un actor VACÍO — no
    // heredó lo que el gestor había escrito para Comprador 1, ni lo perdió (se verifica abajo).
    expect(screen.getByRole('tab', { name: /Comprador 2/ })).toHaveAttribute('aria-selected', 'true');
    expect((screen.getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');

    // Fase 3: vuelve a Comprador 1 — su "111" sigue ahí, intacto pese al cambio de layout.
    await user.click(screen.getByRole('tab', { name: /Comprador 1/ }));
    expect((screen.getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('111');

    // Fase 4: de vuelta a Comprador 2 — sigue vacío, no "pegado" con el valor de Comprador 1.
    await user.click(screen.getByRole('tab', { name: /Comprador 2/ }));
    expect((screen.getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');

    // Fase 5: elimina Comprador 2 (activo) → el formulario regresa a "isSplit" (un solo actor).
    // Comprador 1 no perdió su documento en el viaje de ida y vuelta entre layouts.
    await user.click(screen.getByRole('button', { name: 'Quitar Comprador 2' }));
    await waitFor(() =>
      expect(screen.queryByRole('tablist')?.querySelectorAll('[role="tab"]').length ?? 0).toBe(1),
    );
    expect((screen.getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('111');
  });

  it('traspaso: vendedores y compradores se comportan IGUAL — el contenido se reemplaza al cambiar de pestaña en ambos lados', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);

    const vendedorCard = await screen.findByRole('group', { name: 'Vendedor' });
    const compradorCard = screen.getByRole('group', { name: 'Comprador' });

    // CC solo admite dígitos (`sanitizeDocNumber`) — valores puramente numéricos y distintos
    // entre sí, para no depender de letras que la sanitización descartaría igual en ambos lados.
    await user.type(within(vendedorCard).getByLabelText(/Número de documento/), '9111');
    await user.type(within(compradorCard).getByLabelText(/Número de documento/), '8222');

    // Agrega un segundo en AMBOS lados — traspaso siempre es layout MULTI (sin salto de layout),
    // pero el comportamiento de reemplazo debe ser idéntico al de matrícula.
    await user.click(within(vendedorCard).getByRole('button', { name: 'Agregar copropietario de vendedor' }));
    await user.click(within(compradorCard).getByRole('button', { name: 'Agregar copropietario de comprador' }));

    // Cada lado quedó en su pestaña 2, vacía — sin heredar el dato del otro lado ni del propio
    // ordinal=1.
    expect((within(vendedorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');
    expect((within(compradorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');

    // Vuelve a la pestaña 1 de CADA lado: cada uno recupera SU dato, sin mezclarse con el otro.
    await user.click(within(vendedorCard).getByRole('tab', { name: /Vendedor 1/ }));
    await user.click(within(compradorCard).getByRole('tab', { name: /Comprador 1/ }));
    expect((within(vendedorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('9111');
    expect((within(compradorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('8222');

    // Y de vuelta a la 2: otra vez vacío en los dos lados — el reemplazo es simétrico.
    await user.click(within(vendedorCard).getByRole('tab', { name: /Vendedor 2/ }));
    await user.click(within(compradorCard).getByRole('tab', { name: /Comprador 2/ }));
    expect((within(vendedorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');
    expect((within(compradorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');
  });
});
