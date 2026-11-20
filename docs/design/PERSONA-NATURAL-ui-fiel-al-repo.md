# Portal Persona Natural — Diseño de UI fiel al repo `transfer-step-trail`

> Estado: **Propuesto** · Fecha: 2026-10-27 · Épica: *Portal Persona Natural* (`docs/ado-drafts/epic-persona-natural/`)
> HUs que consumen este documento: **E1–E6** (landing y modales), **F1–F7** (wizard), **H2** (admin en FLIT 2, fuera de este repo de UI) y las **HUs del Feature de tipos dinámicos** (por crear; ver §12).
> **Alcance (corrección del PO, 2026-10-27):** el portal sirve **cualquier tipo de trámite elegible para persona natural**; el traspaso es el **piloto**. El supuesto S3 «solo traspaso» queda anulado. Las secciones §1–§11 describen el wizard de traspaso **tal como existe en el repo**; §12 define cómo se generaliza sin romper fidelidad.
> Fuente de verdad visual: repo `C:\FLIT 2.0\transfer-step-trail`, **commit `6fd389f` (2026-10-26)**, TanStack Start + Vite 8 + Tailwind 4 + Radix + bun, generado con Lovable. El repo se trató como **solo lectura**; todo lo ejecutado se hizo en una copia en el scratchpad.
> Este documento **no** es el diseño de FLIT 2 (`frontend/`, Next.js, `globals.css`, guardián `flit-design-guardian`). El portal tiene **su propia línea base** y manda el repo, no el guardián. Las dos paletas se parecen pero **no son la misma**.

## 0. Cómo leer este documento

| Marca | Significado |
|---|---|
| 🟢 **EJECUCIÓN** | Visto corriendo el dev server de la copia (Vite 8, `127.0.0.1:5199`) en Chrome, o medido con script sobre la página en ejecución. |
| 🔵 **CÓDIGO** | Solo leído en el código fuente. No se vio en pantalla. |
| 🧮 **CÁLCULO** | Derivado de los tokens con un script (contraste WCAG); no es una medición de pantalla. |

**Qué se ejecutó (🟢):** landing a 1536×735; modal «Consultar mi trámite» con `XYZ987` / `222`; wizard Paso 1 (`?placa=ABC123&cedula=1020456789&tipo=Traspaso vehicular&organismo=Envigado`); Paso 2; Pasos 3, 4 y 5 por la ruta de subsanación (`&subsanar=true`) subiendo archivos de prueba por script; landing y wizard dentro de un iframe de **360 px** (la ventana del navegador no se dejó redimensionar: `innerWidth` siguió en 1536); `vitest run` del repo: **9 archivos, 42 pruebas, todas en verde**.

**Qué NO se vio en pantalla (🔵):** modales *Requisitos*, *Cotizar*, *Tarifas* y *legal/«Próximamente»*; Dr. FLIT abierto; carruseles en movimiento; navegación por teclado real; breakpoints 540/640/760/900/1100 px (solo 360 y 1536); Paso 3 sin subsanación; estados de error (el repo casi no los tiene).

---

## 1. Hallazgos que cambian el plan (leer primero)

1. **Los 20 activos gráficos NO están en el repo.** `src/assets/*.asset.json` son punteros a la CDN de Lovable (`/__l5e/assets-v1/<id>/<archivo>`); el repo no contiene ningún `.svg/.png/.jpg`. 🟢 Fuera de Lovable las **11 imágenes de la landing salen rotas** (logo, banners, íconos de acción, pasos, Dr. FLIT). Hay que **exportarlos desde Lovable** a `public/` antes de E1/E2. *Bloqueante de despliegue.*
2. **El README describe un wizard distinto al que está implementado.** El README exige un carrusel horizontal (`scroll-snap`, deslizamiento en eje X, bloqueo de swipe). 🔵 El código renderiza **un paso a la vez con barra lateral vertical** (`roadmap.md`: «Wizard a 2 columnas»); las clases `.wizard-slide/.wizard-slider` en `styles.css` están **huérfanas** (ningún `.tsx` las usa) y el test «wizard swipe lock» solo prueba `isStepValid`. **Se sigue el código** (es lo que se ve y está en el roadmap). *Pendiente del PO: confirmar que el carrusel del README quedó obsoleto.* El título de **F1** («…y carrusel horizontal») debe ajustarse según esa respuesta.
3. **No hay login ni cuenta** (decisión ya tomada). La identidad se prueba por persona y por trámite con el enlace de validación. El repo ya refleja eso (`Validación: pendiente` + `Reenviar Validación`).
4. **Todo es simulado y sin cliente HTTP.** No hay `fetch`/API. `@tanstack/react-query` está instalado y `QueryClientProvider` montado en `__root.tsx`, pero sin ningún `useQuery`. Los módulos puros de `src/lib/*` (con pruebas) son el punto de reemplazo natural.
5. **El repo casi no define estados de carga ni de error.** Solo existen: `MobilityLoader` a pantalla completa (al iniciar), validación inline (`aria-invalid` + texto naranja), 404/500 genéricos **en inglés**, y «No hay organismos que coincidan». Los estados que exige la integración real (cargando, error de red, 429, sesión vencida, no encontrado, «procesando») **hay que diseñarlos reutilizando el vocabulario visual existente** (§7.4).
6. **El diseño choca con tres decisiones de negocio ya tomadas** (políticas C y de compuerta): ver §9 (D-15, D-16, D-17). No se resuelven aquí; se señalan para el ADR/PO.
7. **La paleta no cumple WCAG 2.1 AA en varios pares de color** 🧮 (§6.1). Fidelidad y AA entran en conflicto: se documenta la decisión que debe tomar el PO (§6.3).
8. **El repo solo tiene diseñado el wizard de traspaso** 🔵 (ruta `/tramite-traspaso`, `h1` «Trámite de traspaso», 5 pasos fijos y textos del traspaso). Con el alcance «cualquier tipo» el wizard pasa a ser **dinámico** (§12): hay que generalizar títulos, pasos, tarjetas y resumen, y existen piezas **sin diseño en el repo** (renderer de campos genéricos, `fur`/firma). *Pendiente del PO: diseño de los demás tipos* (§11, §12.7).

---

## 2. Reglas de fidelidad (qué se toca y qué no)

### 2.1 No tocar sin aprobación explícita del PO (cualquier cambio visible)
- Tokens de `:root` en `src/styles.css` (§3), la fuente **Poppins 300/400/500/600/700**, `--radius: 9px` y los radios `rounded-[9px]`/`rounded-full` por componente.
- Estructura de las pantallas: orden de secciones de la landing, grilla 3fr/1fr, tarjetas de acción (2fr + 1fr), orden de las tarjetas de cada paso del wizard, sidebar de 300 px, stepper vertical.
- Variantes de `Button` y su uso semántico (§4.1) y el patrón de modal (§4.4).
- Copy visible (títulos, etiquetas, textos de ayuda, mensajes), salvo los reemplazos listados en §8 (PSE → transferencia, datos reales).
- Clases CSS referenciadas por selectores de `styles.css`: **no renombrar ni reemplazar por utilidades** (ej. la animación de entrada de los modales depende del selector `[role="dialog"].fixed.left-1\/2.top-1\/2[data-state="open"]`; si se cambian esas utilidades se pierde `flit-modal-in`). Idem `.dashboard-*`, `.action-*`, `.wizard-*`, `.ot-*`, `.paso-*`, `.somos-*`.
- Animaciones (`flit-banner-in`, `flit-modal-in`, `paso-float`, etc.) y su `prefers-reduced-motion`.

### 2.2 Permitido sin aprobación (no altera lo que se ve)
- Conectar datos reales en lugar de mocks, manteniendo el mismo formato visible (`formatCOP`, fechas `dd/mm/aaaa hh:mm`, mayúsculas de placa, etc.).
- Cambios **no visuales** de accesibilidad: gestión del foco, `aria-*`, `role="alert"/"status"`, `htmlFor`, orden de tabulación, `lang`, `aria-live`.
- Extraer los componentes locales del wizard (`Card`, `Field`, `Row`, `Badge`, `Btn`, `Nav`, `Drop`, `AddressField`) a archivos propios **sin cambiar una sola clase**.
- Tipado, pruebas, lint, manejo de errores en la capa de API, variables de entorno, SEO/`head`.
- Estados nuevos construidos **solo con piezas ya existentes** (§7.4), una vez el PO apruebe este documento.

### 2.3 Todo cambio visual requiere captura «antes/después» contra el baseline del repo (§10).

---

## 3. Inventario de tokens (`src/styles.css`, Tailwind 4 `@theme inline`, todo en `oklch`)

### 3.1 Paleta de marca (`:root`)
| Token | Valor | Uso semántico |
|---|---|---|
| `--flit-navy` | `#162744` (`oklch(from #162744 l c h)`) | texto base (`--foreground`), `--secondary` (botón «back»), tarjetas de cabecera oscura |
| `--flit-blue` | `oklch(0.63 0.19 268)` | `--primary`, `--ring`, títulos de sección, links, botón `flit` |
| `--flit-teal` | `oklch(0.81 0.14 190)` | `--accent`, extremo del degradado de marca |
| `--flit-green` | `oklch(0.75 0.17 128)` | `--success` (paso completado, badge OK, botón `positive`) |
| `--flit-red` | `oklch(0.47 0.19 28)` | `--destructive` (borde de «Drop» con error) |
| `--flit-yellow` | `oklch(0.79 0.17 75)` | `--warning` (paso en curso, «pendiente») |
| `--flit-orange` | `oklch(0.66 0.23 38)` | `--alert` (errores de campo, subsanación, rechazo) |
| `--flit-gray` | `oklch(0.71 0.03 240)` | `--muted-foreground` y `--disabled` |
| `--flit-line` | `oklch(0.83 0.07 260)` | `--border`, `--input` |
| `--flit-soft` | `oklch(0.95 0.02 258)` | `--background`, `--muted` |
| `--dashboard-action-background` | `#557EFF` | fondo de las tarjetas de acción de la landing |

🟢 Medido en ejecución: `body` = `background oklch(0.95 0.02 258)`, `color oklch(0.274 0.059 260.48)`, `font-family Poppins, sans-serif`; las 5 variantes de Poppins cargan.

### 3.2 Otros tokens
- **Radio:** `--radius: 9px` (+ `sm/md/lg/xl…` derivados). Botones y píldoras: `rounded-full`. Tarjetas y modales: `rounded-[9px]`. Íconos de acción: círculo.
- **Degradados:** `--gradient-flit` (teal→blue, 135°), `--gradient-enterprise`, `--gradient-heading` (`#00DBD5`→`#557EFF`, títulos `.flit-gradient-title`), `--gradient-icon-soft`.
- **Sombras:** `--shadow-float` (`3px 3px 3px` azul 30 %), `--shadow-select`, `--shadow-action`.
- **Cristal de ícono:** `--icon-glass` (`primary-foreground` 10 %).
- **Movimiento:** `flit-banner-in`, `flit-fade-in-up`, `flit-pulse-ring`, `flit-scale-up-br`, `flit-modal-in` (250 ms), `paso-float` (3 s), `somos-in`, `flit-title-shift` (6 s), `mobility-loop` (1.8 s). Todo con bloque `prefers-reduced-motion`.
- **Tipografía:** Poppins; títulos de página `text-[32px] font-bold`; título de tarjeta del wizard `.wizard-section-title` = `text-2xl font-bold` en `--primary`; `.dashboard-title` 18 px/700/primary; cuerpo `text-sm`; ayuda `text-xs font-light text-muted-foreground`. Google Fonts por `<link>` en `__root.tsx`.
- **Scroll:** barra de página de 10 px azul (`html::-webkit-scrollbar`), `scroll-behavior: smooth`.

### 3.3 Utilidades y clases propias
| Clase | Qué hace |
|---|---|
| `@utility field` | input/select/textarea: `min-height 44px`, borde 1 px `--border`, radio 9 px, `padding 0 14px`, 16 px (12 px dentro de tarjetas del dashboard), foco = borde primary + halo 2 px; `:disabled/[readonly]` = fondo `--muted`; `[aria-invalid=true]` = borde `--alert` |
| `@utility badge-arrow` | píldora con punta de flecha a la derecha (`clip-path`), usada en badges del wizard |
| `.dashboard-shell/.dashboard-two-col` | layout landing: máx. 1440 px, padding lateral 50 px (12 px ≤640), `3fr / 1fr`, bloque `.dashboard-fullwidth` (fila completa) |
| `.dashboard-card` | tarjeta blanca, borde `--border`, radio 9, hover borde primary |
| `.dashboard-institutional-card` / `.action-start` / `.action-row` / `.action-clickable` | tarjetas azules `#557EFF` de acción; hover `translateY(-4px)`; foco visible `outline 3px accent` |
| `.action-top-icon` | círculo de 64 px (48 px en filas) con cristal y SVG blanco |
| `.wizard-cols/.wizard-sidebar` | wizard a 2 columnas (`300px 1fr`, gap 24); sidebar `sticky top 24px`, sin scrollbar; ≤900 px 1 columna y sidebar `static` |
| `.wizard-header/.wizard-main` | `padding-inline 50px` (12 px ≤640) |
| `.timeline-connector` | línea entre pasos del tracking |
| `.mobility-track/.mobility-orbit/.mobility-mini` | loader del auto orbitando (84 px) y spinner de 14 px |
| `.chat-launcher/.chat-widget/.dr-flit-tab` | asistente fijo |
| `.how-step/.paso-*/.somos-*/.otros-servicios-*/.bondades-grid` | secciones de la landing |

### 3.4 Breakpoints reales usados por el CSS
`min-height ≤740` (compactación), `≤1100`, `≤1200` (nav), `≤900` (una columna, footer/Dr. FLIT reubicados), `≤760`, `≤640` (gutter 12 px, acciones apiladas), `≤540`. Tailwind: `sm 640`, `md 768`, `lg 1024`.

---

## 4. Inventario de componentes reutilizables

### 4.1 `Button` (`components/ui/button.tsx`, `cva`)
| Variante | Aspecto | Uso en el repo |
|---|---|---|
| `flit` | píldora azul `primary`, texto blanco 600 | acción principal (Consultar, Calcular, Iniciar ahora, Continuar/Siguiente, Cerrar) |
| `positive` | píldora verde `success` | cierre exitoso (Enviar trámite, Descargar comprobante, Pagar) |
| `back` | píldora navy `secondary` | Atrás / secundaria / «Iniciar más tarde» |
| `institutional` | píldora blanca texto navy | CTA dentro de tarjetas azules |
| `alert` | píldora naranja | definida; sin uso visible en pantallas |
| `ghost`, `link`, `outline`, `default`, `secondary`, `destructive` | estándar shadcn | íconos de cerrar, links del footer, «Ver tarifas» |
Tamaño mínimo táctil por convención: `h-11` (44 px) en casi todo; los íconos de cerrar `h-11 w-11`.

### 4.2 Componentes de dominio
- **`TransactionSelect`** (Radix Select): `field`, chevron primary, lista `bg-popover`, `shadow-select`, ítem resaltado `primary/10`. Soporta `label` (aria), `disabled`.
- **`RadicadoTracking`**: 5 pasos con círculo de 40 px, línea conectora, título y fecha `dd/mm/aaaa hh:mm`; estados `done` (verde ✓), `current` (amarillo, reloj, `animate-pulse` solo si `motion-safe`), `rejected` (naranja ⚠), `pending` (gris, número). Textos de paso: «Borrador del trámite», «Validación de identidad», «Enviado al OT», «En revisión», «Aprobado / Rechazado» (el último cambia a «Aprobado» o «Rechazado»).
- **`MobilityLoader`**: `role="status" aria-live="polite"`, auto orbitando + «Cargando…». Se usa dentro de un overlay `fixed inset-0 z-50 bg-background/80 backdrop-blur-sm`.
- **`DashboardChat`** (Dr. FLIT): pestaña fija con degradado + panel `role="dialog" aria-modal="false"`, Escape minimiza y devuelve el foco. **Simulado** (supuesto S4).
- **`ChatCoverage`**, **`RadicadoTimeline`** (4 macro-estados), **`PseSimulator`**, **`PsePaymentAction`**, **`QuotePayment`**: 🔵 **sin uso en las pantallas actuales** o por retirar (PSE ya no está en el wizard). **No portar** `RadicadoTimeline` ni PSE; `ChatCoverage` solo si Dr. FLIT lo conserva.
- Primitivas Radix/shadcn en `components/ui/*` (Accordion, Dialog, Tooltip, etc.): se reutilizan tal cual.

### 4.3 Componentes locales del wizard (`tramite-traspaso.tsx`) — extraer sin cambiar clases
| Local | Aspecto | Notas |
|---|---|---|
| `Card` | `rounded-[9px] border border-border bg-card p-6` + título `.wizard-section-title text-2xl mb-4` | |
| `Field` | `<label>` con texto `text-sm font-medium` + hijo + error `text-xs text-alert` con ⚠ | el error **no** tiene `role="alert"` |
| `Row` / `Badge` | fila del diagnóstico (`.wizard-diag-row`) y badge flecha con tonos `success`, `warning`, `info` | **no existe tono para rechazado/vencido** (ver §8) |
| `Btn` | envoltorio de `Button` (`primary→flit`, `secondary→back`, `positive`, `alert`) `min-h-11 px-6` | |
| `Nav` | fila `flex justify-end gap-3` con Atrás + Continuar | |
| `Drop` | zona punteada `h-44`, ícono Upload (azul) / FileText (verde si hay archivo), «Opcional», error borde `destructive` | `accept=".pdf,.jpg,.jpeg,.png,.webp"`; **solo guarda el nombre**; sin estados de subida |
| `AddressField` | input `autocomplete="street-address"` + tooltip «dirección de envío por defecto de la tarjeta de propiedad» (solo comprador) | botón de info de 44 px |

### 4.4 Patrón de modal (Radix Dialog) — se repite en 5 modales
- Overlay `fixed inset-0 z-50 bg-primary/50 backdrop-blur-sm` (visor de documento: `bg-secondary/50`; legal: `bg-primary/40`).
- Contenido centrado `left-1/2 top-1/2 -translate-x/y-1/2`, `w-[calc(100%-2rem)]`, `max-h-[90dvh]`, `overflow-y-auto`, `rounded-[9px] border bg-card p-6`; anchos: **`max-w-4xl`** (Requisitos, Consultar, Cotizar, Tarifas), **`max-w-2xl`** (visor), **`max-w-md`** (legal).
- Cabecera: `ModalTitleIcon` (círculo 40 px `bg-primary/10` con ícono lucide 20 px) + `Dialog.Title text-xl font-bold text-primary` centrados; `Dialog.Description` `text-xs/text-sm text-foreground/70`.
- Cierre: botón `ghost` ícono `X`, `absolute right-2 top-2 h-11 w-11`, `aria-label="Cerrar"`.
- 🟢 Medido en «Consultar»: ancho 896 px, radio 9 px, fondo blanco, `aria-labelledby` y `aria-describedby` presentes; **el foco inicial cae en el botón Cerrar** (comportamiento por defecto de Radix).

---

## 5. Especificación por pantalla

> Para cada pantalla: **Lleno** = lo que se ve hoy con datos; **Vacío / Cargando / Error** = lo que hay hoy (🔵/🟢) y lo que se **propone** con piezas existentes (§7.4).

### 5.1 Landing `/` y `/dashboard` (misma pantalla, `head` distinto)
**Estructura (🟢 vista en 1536×735; resto 🔵):** header (logo FLIT 36 px a la izquierda; nav `Trámites · Nosotros · Servicios · FAQ` a la derecha, anclas `#nuevo-tramite #nosotros #servicios #faq`) → grilla `3fr / 1fr`:
- **Columna principal:** banner de 240 px (carrusel de 2 imágenes, autoplay 4,5 s, puntos) → **acciones** (`Iniciar mi trámite` azul 2/3 con 3 campos: *Número de Cédula*, *Placa del vehículo* (mayúsculas, máx. 6), *Tipo de Trámite* (`TransactionSelect`) + botón `institutional` «Iniciar trámite»; a la derecha dos filas clicables «Consultar mi trámite» y «Cotizar mi trámite») → «4 simples pasos para tu trámite» (4 SVG con número) → «¿Cómo radicar mi trámite?» (marco punteado «Video explicativo disponible próximamente») → «Somos FLIT» (3 tarjetas 01/02/03).
- **Columna lateral:** «Servicios Adicionales» (carrusel 240 px, 5 ítems, autoplay) y «Organismos de tránsito integrados a nuestro ecosistema» (buscador + lista con «Ver tarifas»).
- **Fila completa:** «Otros Servicios Vehiculares» (slider zig-zag, autoplay 4 s) y «Preguntas Frecuentes» (Accordion de 5).
- **Fijos:** pestaña Dr. FLIT (1/4 del ancho, 104 px de alto) y footer legal.
**Validaciones (🔵):** botón «Iniciar trámite» deshabilitado hasta `cédula ≥ 6`, `placa ≥ 5` y tipo elegido; cédula solo dígitos.
**Estados:**
| Estado | Hoy | Propuesto |
|---|---|---|
| Vacío | «No hay organismos que coincidan.» en la lista | igual; si el catálogo viene vacío del API, mismo texto |
| Cargando | ninguno (datos estáticos); `MobilityLoader` overlay 1,8 s al iniciar | catálogos: listas con texto «Cargando…» + `mobility-mini`; al iniciar: overlay `MobilityLoader` (sin temporizador falso: dura lo que dure la llamada) |
| Error | ninguno | caja de alerta (§7.4) bajo la tarjeta afectada con «Reintentar» |
| Lleno | 🟢 como se ve | igual |
**Responsive 360 px (🟢 en iframe):** sin scroll horizontal; acciones en 1 columna; campos de «Iniciar» en 1 columna; banner 190 px. ⚠ **La pestaña Dr. FLIT fija (ancho `100vw-24px`) y el footer fijo tapan los campos del formulario en la primera pantalla** (visto). 15 controles con alto < 44 px; 40 nodos de texto < 12 px (🟢 conteo).
**A11y:** `header/nav/main/aside/footer` con `aria-label`; carruseles con puntos `aria-current`. Ver brechas D-05, D-06.

### 5.2 Modal «Requisitos para tu trámite» (tras «Iniciar trámite») 🔵
`max-w-4xl`; ícono `ClipboardList`; descripción; 6 tarjetas (`grid-cols-2 sm:3 lg:6`): documento de identidad, paz y salvo de impuestos, paz y salvo de multas, SOAT, RTM (si aplica), improntas. Botones: `back` «Iniciar más tarde» (cierra) y `flit` «Iniciar ahora» (inicia, muestra `MobilityLoader`).
*Con la integración:* aquí se agrega el **captcha** (E5/A5) sobre los botones, centrado, sin marco propio nuevo (§7.4); «Iniciar ahora» llama al BFF para abrir la sesión efímera. Error de captcha/429 → caja de alerta dentro del modal. «Iniciar más tarde» no crea nada en el servidor.

### 5.3 Modal «Consultar mi trámite» 🟢
Ícono `Search`, descripción «Ingresa la placa y el número de radicado o cédula.» Campos en 2 columnas (*Placa*, *Número de Radicado o Cédula*) + botón `flit` «Consultar» (deshabilitado hasta que ambos tengan texto). 🟢 **Ya pide dos datos** (placa + radicado/cédula): coincide con la regla anti-enumeración.
**Resultado (🟢 con 222):** titular de color por estado (`text-success` / `text-warning` / `text-alert`: «Tu trámite ha sido aprobado» / «está en proceso» / «ha sido rechazado»), `RadicadoTracking` de 5 pasos, lista `dl` (ID del trámite, Placa asociada, Tipo de trámite), caja de mensaje tintada, y según estado: *en proceso* → «En revisión en la Secretaría de Movilidad.» + `flit` «Entendido»; *aprobado* → correo + `positive` «Descargar comprobante» + link «Enviar al correo»; *rechazado* → caja con la causal de rechazo y «Entendido». No hay ruta para corregir el trámite desde la consulta.
**Estados nuevos a definir:** *no encontrado* (hoy el mock devuelve «rechazado» para cualquier número desconocido, D-14), *error de red*, *429*, *captcha*.

### 5.4 Modal «Cotizar mi trámite» 🔵
Ícono `CircleDollarSign`; descripción «…Valores simulados de demostración.» (el texto «simulados» **debe retirarse** con datos reales). 2 campos: *Organismo* (dropdown propio con buscador, `listbox`) y *Tipo de trámite* (`TransactionSelect`); botón `flit` «Calcular» (deshabilitado hasta tener ambos). Resultado: tabla de 2 líneas («Valor del Servicio de tránsito», «Valor de Servicio FLIT» con subtítulo explicativo) + fila «Total» y formulario «Enviar al correo» (`back`). Hoy: **Servicio de tránsito $185.000 + Servicio FLIT $181.000 = $366.000 COP** (fijos).
*Estados:* cargando = botón con `mobility-mini` + deshabilitado; error = caja de alerta; sin tarifa para esa combinación = texto en la fila de la tabla.

### 5.5 Modal «Tarifas · {Organismo}» 🔵
Ícono `Scale`; tabla *Trámite / Tarifa* con los 7 trámites. Hoy valores ilustrativos (120.000–190.000). Con API: tarifas reales del organismo.

### 5.6 Modal legal / «Próximamente» 🔵
`max-w-md`, ícono `CircleHelp`, píldora «Próximamente» (servicios) o «El documento oficial aún no está disponible.» (políticas). **Regla del repo (roadmap):** *nunca inventar contenido legal*; se mantiene hasta que el PO entregue los documentos oficiales.

### 5.7 Dr. FLIT 🔵
Pestaña fija con degradado `bg-flit-gradient`, avatar, «En línea» con punto pulsante; panel con cabecera degradada, conversación (`ai-elements`), botón «Conocer organismos de tránsito habilitados», 4 preguntas rápidas, caja de texto. Respuestas por coincidencia de palabras (`responderConsulta`). **Se mantiene simulado (S4).** Texto del repo menciona PSE (D-08).

### 5.8 Wizard `/tramite-traspaso` — carcasa 🟢
Header: logo + (md+) **«Juan Pérez»** fijo, `Placa {placa}`, tipo, campana. Migas «Dashboard > Trámites > Traspaso > {paso}» (paso actual subrayado azul; en el Paso 5 dice «Confirmación»). `h1` «Trámite de traspaso» 32 px. Dos columnas:
- **Sidebar (300 px, sticky):** ícono coche + «Mi trámite» + chip de placa; «Configuración del trámite» con *Organismo de Tránsito* (solo lectura), *Tipo de Trámite* (select) y *Valor del trámite* (solo lectura, hoy `$ 285.400 COP`); stepper vertical de 5 botones `Paso n · …` (círculo 36 px: verde ✓ hecho, azul activo, gris pendiente; `aria-current="step"`), con «Novedad: subsanar documento» en el Paso 3 si aplica. Pasos futuros no alcanzables → `disabled`. 🟢 **Tras enviar (Paso 5) los 5 botones quedan deshabilitados** (no se puede volver).
- **Área de trabajo:** tarjetas del paso + fila de navegación `flex justify-end` (Atrás `back` + Continuar/Siguiente `flit`; en Pago, «Enviar trámite» `positive`).
**Validación (🔵):** `touched` global; al intentar avanzar con campos inválidos se marcan en naranja (`aria-invalid` + texto) y no avanza. Avanzar hace `scrollTo(top, smooth)`; 🟢 **el foco queda en `BODY`** tras cada cambio de paso.
**Responsive 360 (🟢):** una columna; el sidebar pasa a `static` y mide **656 px de alto** antes de la primera tarjeta (hay que desplazar mucho para llegar al contenido); sin scroll horizontal.

### 5.9 Pasos
**Paso 1 · Diagnóstico (🟢).** Tarjeta «Diagnóstico de requisitos previos» («Consultado en RUNT el …»; 6 filas con badge flecha: Estado del vehículo, SOAT, RTM, Gravámenes y limitaciones, Impuestos pendientes, Multas asociadas; caja amarilla «Atención: para continuar debes estar al día en SOAT, multas e impuestos») y «Datos técnicos del vehículo» (12 datos en `dl`, **incluye Propietario Actual y VIN**). Navegación: «Atrás» (vuelve al dashboard, **sin confirmación**) y «Continuar».
**Paso 2 · Actores (🟢).** «Propietario (vendedor)»: nombre y documento de solo lectura, *Teléfono\**, *Correo\**, *Dirección*; fila de badges (`Multas: paz y salvo`, `Licencia activa · vence …`, `Validación: pendiente`) + `flit` «Reenviar Validación». «Comprador»: *Tipo de documento*, *Número de documento\** con lupa + «Consultar comprador»; al consultar habilita nombre, teléfono, correo, dirección (con tooltip de entrega) y los mismos badges + «Reenviar Validación». Teléfono 7–10 dígitos; correo con regex simple.
**Paso 3 · Requisitos (🟢 vía subsanación).** Tarjetas en vertical: *Datos comerciales* (Avalúo Fasecolda deshabilitado «—», Fuentes «Fasecolda: No disponible», *Valor de venta ($)\**), *Prenda / limitación* (radio No/Sí → acreedor, NIT/documento select, `Drop` certificado), *Documentos* (`Drop` Improntas opcional, Documentos del trámite opcional, **Paz y salvo de impuestos\*** + en subsanación alerta naranja «Observación del OT…»), *Observaciones* (textarea), *Transformaciones* (3 switches con precio `+ $…` que suman al total) y «Valor total liquidado» a la derecha; navegación Atrás/Siguiente.
**Paso 4 · Resumen (🟢).** Placa, tipo de trámite y valor cotizado; Atrás + «Enviar trámite». No hay facturación, datos bancarios ni carga de comprobante.
**Paso 5 · Envío y confirmación (🟢).** Círculo verde ✓, «¡Muchas gracias, tu trámite se ha enviado con éxito!» (verde 32 px), tabla azul *ID del trámite · Tipo · Nº radicado · Organismo* (🟢 fijos: `TR-2026-004821`, `RAD-88231`), `RadicadoTracking` (enviado: 2 verdes, «Enviado al OT» amarillo), texto de ayuda, tarjetas *Vehículo, Actores, Condiciones Comerciales, Valor Total del Trámite*, *Comprobante de pago* («Comprobante cargado - Pendiente de aprobación» + «Visualizar»), *Documentos cargados del traspaso* (4 tarjetas «Cargado / Verificado» + «Ver documento»), `flit` «Descargar comprobante» y `back` «Ir al Dashboard».
**Visor de documento (🔵):** modal `max-w-2xl` con vista previa simulada (`h-80`, marco punteado).

### 5.10 Subsanación (`?subsanar=true`) 🟢
Arranca en el Paso 3 con Pasos 1–2 en verde, «Novedad: subsanar documento» en el stepper, alerta naranja «Observación del OT» y scroll suave al campo *Paz y salvo de impuestos*. Se llega desde la caja naranja del modal «Consultar» (rechazado). «No requiere pago adicional y pasa a En Revisión» (texto del repo).

---

## 6. Accesibilidad (WCAG 2.1 AA)

### 6.1 Contraste 🧮 (calculado con los tokens; AA = 4,5:1 texto normal, 3:1 texto grande/UI)
| Par | Razón | ¿AA? |
|---|---|---|
| Texto navy sobre fondo `soft` / sobre tarjeta blanca | 12,9 / 14,9 | ✅ |
| Blanco sobre `secondary` navy (botón `back`) | 14,9 | ✅ |
| Texto secundario `foreground/70` sobre `soft` | 5,25 | ✅ |
| Navy sobre teal | 8,7 | ✅ |
| `destructive` rojo sobre blanco | 7,5 | ✅ |
| **Blanco sobre `primary` (botón `flit`, chips, tarjetas `#557EFF`)** | **3,63 / 3,61** | ❌ texto normal (14 px 600 no es «grande») |
| **`primary` sobre blanco / sobre `soft` (títulos, links)** | **3,63 / 3,14** | ❌ texto normal; ✅ solo títulos ≥ 24 px |
| **`muted-foreground` y `disabled` (gris) sobre blanco / `soft`** (`text-xs font-light` de ayudas, pasos pendientes) | **2,56 / 2,21** | ❌ |
| **Blanco sobre `success` (badge OK, botón `positive`)** | **2,13** | ❌ |
| **`warning` amarillo sobre `warning/10` (badge «pendiente», paso en curso)** | **1,85** | ❌ |
| `alert` naranja sobre blanco / sobre `alert/10` (errores de campo) | 3,43 / 3,01 | ❌ texto normal |
| `primary` sobre `primary/10` (chip de placa) | 3,25 | ❌ |

### 6.2 Brechas funcionales (🟢 = verificado en ejecución)
1. **Autoplay sin pausa** en 3 carruseles (banner, servicios, otros servicios; 4–4,5 s): incumple 2.2.2. `prefers-reduced-motion` solo quita la transición, no detiene el avance. 🔵
2. **Foco:** al cambiar de paso el foco queda en `BODY` 🟢 (el scroll suave no mueve el foco). Los errores de `Field` no usan `role="alert"` ni `aria-live`; solo `aria-invalid`.
3. **Dropdown de organismo (Cotizar)** hecho con botones: sin flechas/Home/End, sin Escape para cerrar ni devolución del foco. 🔵
4. **`Drop`:** el `<input type="file">` es `sr-only`; el foco del teclado no tiene indicador visible en la zona. 🔵
5. **Switches de transformaciones:** `role="switch"` sobre `Button` con `<label>` envolvente: nombre accesible OK, pero el precio dentro del `<label>` también se lee como nombre. 🔵
6. **Texto muy pequeño:** `text-[10px]/[11px]` (fechas del tracking, etiquetas del dashboard en ≤1100 px); 🟢 40 nodos < 12 px en 360 px.
7. **Objetivos táctiles:** la convención del repo es 44 px (`h-11`); 🟢 15 controles en la landing quedan por debajo a 360 px. (WCAG 2.1 AA no fija tamaño; es regla de FLIT.)
8. **Color como único indicador:** los estados del tracking añaden ícono + texto (✅); los badges llevan texto (✅).
9. **Idioma:** `lang="es"` (✅). Páginas 404/500 en inglés (D-11).
10. **Diálogos:** Radix aporta foco atrapado, Escape y restauración del foco; el foco inicial cae en «Cerrar» 🟢 (aceptable; no mover sin aprobación).

### 6.3 Decisión que necesita el PO (fidelidad vs. AA)
La paleta heredada **no alcanza AA** en los pares de §6.1. Opciones:
- **A. Fidelidad estricta** (no cambiar color) y compensar con tamaño/peso/íconos/subrayado en links; se documenta la excepción.
- **B. Ajuste tonal mínimo** manteniendo el matiz, bajando la luminosidad hasta AA 🧮: `primary` y fondos `flit` a **L ≈ 0,575**, `gray` (texto) **L ≈ 0,565**, `success` (fondo con texto blanco) **L ≈ 0,555**, `alert` (texto) **L ≈ 0,59**, `warning` (texto) **L ≈ 0,57**. Cambia el aspecto, **requiere aprobación**.
- **C. Híbrida:** conservar los colores en superficies decorativas (tarjetas de acción, degradados) y aplicar el ajuste solo a texto pequeño de ayuda/estado.
*Recomendación:* **C**. Hasta que el PO decida, los criterios de aceptación de contraste de §10 quedan **condicionados**.

---

## 7. Datos reales: estados y patrones nuevos

### 7.1 Capa de datos del front (E1)
- Cliente tipado (OpenAPI de `contracts/openapi`) + `@tanstack/react-query` (ya montado). Un solo módulo `api/` con manejo uniforme de `application/problem+json`, `Retry-After` y reintentos.
- Los módulos de `src/lib/*` se conservan como **formateadores y reglas de presentación** (por ejemplo `trackingRadicado`, `formatCOP`, `isStepValid`), no como fuentes de datos; sus pruebas se mantienen.
- La **sesión efímera** viaja en memoria + cookie `HttpOnly` del BFF; no en `localStorage` ni en la URL (la URL del wizard hoy lleva `cedula` y `placa` como query params: **debe dejar de llevar la cédula**).

### 7.2 Variables de despliegue
`vite.config.ts` usa `@lovable.dev/vite-tanstack-config` con **target nitro por defecto = Cloudflare** (build). Fijar el target real y variables de entorno del BFF en A7/E1. `reportLovableError` es inofensivo fuera de Lovable (usa `window.__lovableEvents?.`), pero no aporta nada: reemplazar por el reporte de errores del portal.

### 7.3 Persistencia del avance
Hoy el estado del wizard vive solo en `useState` (se pierde al recargar). «Iniciar más tarde» y la reanudación con radicado + cédula (A4) exigen guardar el borrador por paso (D1); la UI no cambia, pero cada «Continuar» pasa a ser una llamada con estado `cargando` en el botón.

### 7.4 Vocabulario visual para estados nuevos (propuesto; se vuelve vigente al aprobar este documento)
| Necesidad | Pieza existente que se reutiliza |
|---|---|
| Cargando (pantalla/acción larga) | `MobilityLoader` en overlay (`bg-background/80 backdrop-blur-sm`) |
| Cargando (botón/lista) | botón `disabled` + `.mobility-mini`; en listas, texto `text-sm` «Cargando…» |
| Error / aviso bloqueante | caja de la «Observación del OT»: `rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert`, ícono `AlertTriangle`, `role="alert"`, y botón `back` «Reintentar» |
| Éxito de sub-acción | `rounded-full bg-success/10 px-3 py-1 text-xs font-semibold text-success` (como «Cargado / Verificado») |
| En curso | `bg-warning/10 text-warning` (como «Pendiente de aprobación») |
| Sesión vencida | modal estilo *legal* (`max-w-md`, `ModalTitleIcon`, un botón `flit`) con copy nuevo «Tu sesión venció. Reanuda con tu radicado y cédula.» |
| Subida de archivo | `Drop` con nombre + `mobility-mini` mientras sube; error = borde `destructive` + texto `text-xs text-destructive` (ya existe) |
| Captcha | widget del proveedor centrado en el modal, **sin** estilos propios; contenedor neutro (no tarjeta nueva) |
| Tono de badge «rechazada/vencida» (validación) | **no existe**: se propone `bg-alert/10 text-alert` (misma forma `badge-arrow`) → **requiere aprobación** |
| Estado «Procesando» tras `202` (Paso 5) | card con `MobilityLoader` en línea, título neutro «Estamos radicando tu trámite…» y celda *Nº radicado* = «Generando…»; el tracking muestra «Enviado al OT» en `current`. **Requiere aprobación de copy**; al llegar `creado` se muestra el éxito verde actual |

---

## 8. Mapa «mock actual → llamada real al BFF»

> Nombres de endpoint **tentativos**; la fuente de verdad es `docs/design/PERSONA-NATURAL-arquitectura-y-contratos.md` y `contracts/openapi`. Todas las llamadas salen del BFF `core-persona-natural`; el front nunca llama a FLIT 2.

| # | Mock actual (archivo → qué simula) | Llamada real (propuesta) | HU back | HU front | Notas de UI |
|---|---|---|---|---|---|
| 1 | `radicado-demo.ts::consultarRadicado` — `111/222/333`; cualquier otro → «rechazado» | `POST /solicitudes/consulta {placa, radicadoOCedula, captcha}` → `{estado, pasos[], mensaje, id, placa, tipo}`; `404 not_found`; `429` | B4 | E3 | Definir estado «no encontrado» (hoy inexistente). El mapeo a tracking sigue en `radicado-tracking.ts` |
| 2 | `radicado-tracking.ts::trackingRadicado` + `FECHAS` fijas | Los `pasos[]` (estado + fecha real) llegan del espejo de estados | D5 / B4 | E3, F6 | Conservar títulos y colores; fechas `dd/mm/aaaa hh:mm` (hora Colombia) |
| 3 | `b2c-demo.ts::prefactura` — 185.000 + 181.000 | `POST /cotizaciones {organismoId, tipo}` → `lineas[]`, `total` | B5 | E4 | Quitar «Valores simulados de demostración» |
| 4 | `b2c-demo.ts::TARIFAS_ESTIMADAS` (7) / modal «Tarifas» | `GET /catalogos/organismos/{id}/tarifas` | B5 | E2, E4 | |
| 5 | `radicado-demo.ts::ORGANISMOS_INTEGRADOS` (16) **y** `b2c-demo.ts::ORGANISMOS_B2C` (5) | `GET /catalogos/organismos` (una sola lista) | B5 | E2, E4, E6 | Hoy son dos listas distintas (D-09) |
| 6 | `b2c-demo.ts::B2C_TRAMITES` (7 tipos) | `GET /tipos-tramite` (con `disponible` y `motivoNoDisponible`) y `GET /tipos-tramite/{code}/definicion` | B5 + Feature de tipos | E2, E5 + Feature de tipos | **S3 anulado:** el catálogo ya no es fijo; lo manda FLIT 2 (lista blanca). Los no disponibles se muestran «Próximamente» (pieza existente, §12.5; **aprobar copy**). Hoy los 7 llevan al mismo wizard de traspaso (D-10) |
| 7 | Iniciar: `setTimeout 1800` + navegación con `cedula/placa` en la URL | `POST /tramites/borradores {cedula, placa, tipo, organismoId, captcha}` → sesión efímera + `borradorId` | A4 / D1 | E5 | El loader dura lo que tarde la llamada; la cédula sale de la URL |
| 8 | Paso 1: tarjetas fijas («Consultado en RUNT el 15/10/2026 16:23», datos técnicos) | `POST /consultas/vehiculo {placa}` → dato mínimo; detalle completo solo con partes validadas | B1 / B3 | F2 | Ver D-16 (propietario/VIN visibles antes de validar) |
| 9 | Paso 2 «Consultar comprador»: `compradorConsultado = true` + nombre fijo | `POST /consultas/persona {tipoDoc, numero}` → dato mínimo | B2 / B3 | F3 | Ver D-16 (hoy muestra nombre, licencia y multas de un tercero) |
| 10 | Paso 2 «Reenviar Validación» (botón sin acción) y badge «Validación: pendiente» | `POST /borradores/{id}/actores/{rol}/validacion` y `…/reenviar`; estado por `GET` o evento | C1, C2 | F3 | Estados necesarios: pendiente / validada / vencida / rechazada (§7.4 tono nuevo) |
| 11 | Paso 3: `Drop` guarda solo `file.name`; `valorVenta`, prenda, observaciones, transformaciones en `useState` | `PUT /borradores/{id}/comercial`, `/prenda`, `/transformaciones`; `POST /borradores/{id}/adjuntos` (multipart, PDF/JPG/PNG/WEBP ≤ 20 MB) | D1, D2 | F4 | `Drop` con subida real y error; NIT acreedor desde catálogo (hoy 2 bancos fijos) |
| 12 | Paso 3: `BASE_TRAMITE 285.400` + `TRANSFORMACIONES` fijas, `totalTramite` | `GET /borradores/{id}/resumen-cobro` (fuente única = cotización) | B5 / D1 | F4, F5 | Hoy el wizard muestra 285.400 + 60.000 = **345.400** y la cotización **366.000** (D-09) |
| 13 | Paso 4: resumen del trámite (placa, tipo y valor cotizado) | Cotización del BFF | — | F5 | Sin facturación, datos bancarios ni comprobante |
| 14 | Paso 4: «Enviar trámite» | `POST /solicitudes/{id}/radicar` | D3, D4 | F6 | El botón radica desde el resumen |
| 15 | Paso 4→5: «Enviar trámite» = `go(4)` con IDs fijos (`TR-2026-004821`, `RAD-88231`) | `POST /borradores/{id}/radicar` con `Idempotency-Key` → `202 {solicitudId}`; luego `GET /solicitudes/{id}` hasta `creado {radicado}` | D3, D4 | F6 | Estado intermedio «Procesando» (§7.4); idempotencia ante doble clic (botón deshabilitado) |
| 16 | Paso 5: «Descargar comprobante» con `jsPDF` («Sin validez oficial. Pago simulado») | `GET /solicitudes/{id}/comprobante.pdf` (servidor) | D5 | F6, E3 | Retirar el texto de simulación |
| 17 | Modal Consultar/Cotizar: «Enviar al correo» → «El envío por correo aún no está conectado» | `POST /cotizaciones/{id}/enviar` y `/solicitudes/{id}/enviar-comprobante` | **sin HU** | E3, E4 | ⚠ **No hay HU de envío por correo**: crear o retirar el botón |
| 18 | Consulta de un trámite rechazado | `POST /radicados/consulta` muestra la causal | — | E4 | Sin ruta para corregir el trámite |
| 19 | Dr. FLIT: `chat-demo.ts::responderConsulta` | Sin cambio (S4) | — | E6 | Corregir menciones a PSE |
| 20 | PSE (`PseSimulator`, `PsePaymentAction`, `QuotePayment`, `quote-payment-demo.ts`) | **Retirar**: ya no hay pago en línea | — | E1 | No portar |

---

## 9. Desviaciones y deuda visible del repo (no cambiar sin aprobación)

| ID | Tema | Evidencia | Acción propuesta |
|---|---|---|---|
| D-01 | README (carrusel horizontal, swipe) ≠ código (render por paso + sidebar) | 🔵 `.wizard-slide*` huérfanos; sin `scroll-snap` en ningún `.tsx` | Seguir el código; PO confirma. Ajustar título de F1 |
| D-02 | `roadmap.md` desactualizado | 🔵 Bloque «Regla de oro (Oct 25)» sin marcar aunque está implementado; «Activar contacto comercial… WhatsApp» obsoleto (no existe módulo de flotas); «cancelación confirmada» figura `[x]` pero «Atrás» del Paso 1 navega sin confirmación (sin coincidencias de «cancel») | No copiar el roadmap como estado; decidir si falta el diálogo de confirmación |
| D-03 | Activos gráficos no versionados | 🟢 11 imágenes rotas; solo `*.asset.json` | Exportar a `public/` (bloqueante) |
| D-04 | Contraste AA | 🧮 §6.1 | Decisión PO §6.3 |
| D-05 | Autoplay sin pausa (3 carruseles) | 🔵 `setInterval` en `index.tsx` | Añadir control pausa/detener (no visual si es solo al foco/hover; si añade botón, aprobar) |
| D-06 | Foco a `BODY` tras cambio de paso; errores sin `role="alert"` | 🟢 / 🔵 | Permitido: mover foco al `h1`/título del paso y `role="alert"` |
| D-07 | Dropdown de organismo sin teclado de listbox | 🔵 | Permitido (no visual): flechas/Escape/foco |
| D-08 | Texto desactualizado: FAQ «Cómo hago el pago» y chat nombran **PSE** | 🔵 `B2C_FAQS`, comentarios | Cambiar a transferencia Bancolombia (**copy: aprobar**) |
| D-09 | Datos incoherentes | 🟢 Paso 4: 285.400 + 60.000 = 345.400; 🔵 cotización 366.000; dos listas de organismos (5 y 16) | Fuente única en B5 |
| D-10 | 7 tipos de trámite, un solo wizard (traspaso) | 🔵 cualquier tipo navega a `/tramite-traspaso` | **Superada por el alcance «cualquier tipo» (S3 anulado):** wizard dinámico por definición (§12); ver fila 6 del mapa y D-21 a D-28 |
| D-11 | 404/500 en inglés y sin estilo FLIT | 🔵 `__root.tsx` («Page not found», «This page didn't load») | Traducir y usar tokens (**aprobar copy**) |
| D-12 | Placeholders `XXXXXXX/XXXXX` en datos bancarios | 🟢 | Datos reales por configuración; no publicar sin ellos |
| D-13 | Nombre «Juan Pérez» fijo en el header del wizard y en el Paso 2; campana sin función | 🟢 | Nombre real del solicitante (sesión efímera) o retirar; la campana no tiene alcance (no hay notificaciones) → **decidir** |
| D-14 | Cualquier número desconocido = «rechazado»; no existe «no encontrado» | 🔵 test `consultarRadicado("444")` | Estado nuevo (§5.3) |
| D-15 | El Paso 2 permite continuar con «Validación: pendiente» | 🔵 `isStepValid(1)` solo valida correo/teléfono/consulta | Conflicto con la compuerta C3: la UI debe impedir **enviar** sin ambas validaciones, con aviso (§7.4) |
| D-16 | Pasos 1 y 2 muestran datos de **terceros** completos (propietario, VIN, nombre, licencia y multas del comprador) | 🟢 Paso 1/2 | Choca con la política **C (dato mínimo)**. Propuesta: enmascarar (p. ej. «JUAN P*** G***»), booleanos «paz y salvo / licencia activa», y completo solo con partes validadas. **Requiere decisión PO + legal** |
| D-17 | La **cédula** y la placa viajan en la URL del wizard (`?cedula=…&placa=…`) | 🟢 URL usada | PII en URL; mover a sesión (A4) |
| D-18 | En 360 px: Dr. FLIT fijo y footer fijo tapan campos; sidebar de 656 px antes del contenido; 15 controles < 44 px | 🟢 | Ajuste responsive (**aprobar**: es visible) |
| D-19 | Select «Tipo de Trámite» del Paso 1 se ve vacío con `tipo=Traspaso vehicular` (el catálogo usa «Traspaso de vehículo usado») | 🟢 primera pantalla del Paso 1 | Unificar el nombre con el catálogo real (fila 6) |
| D-20 | Boilerplate Lovable (`error-capture`, `lovable-error-reporting`), target nitro Cloudflare, `bun.lock` | 🔵 | Sustituir en E1/A7 |

---

## 10. Criterios de aceptación visual (verificables)

### 10.1 Método
1. **Baseline:** capturar con Playwright las pantallas del repo en `6fd389f` (con los activos exportados) en **1536×735** y **360×740**: landing; modales Requisitos, Consultar (con `111/222/333`), Cotizar (con cálculo), Tarifas, legal; wizard Pasos 1–5 y subsanación; visor de documento. Guardar en `frontend-persona-natural/e2e/__baseline__/`.
2. **Comparación:** `toHaveScreenshot` con `maxDiffPixelRatio ≤ 0,002` **enmascarando** datos dinámicos (fechas, radicados, valores, nombres) y animaciones desactivadas (`prefers-reduced-motion: reduce`).
3. **Estilos computados:** pruebas que lean `getComputedStyle` de los elementos clave (lista abajo).
4. Todo desvío se justifica en el PR con captura antes/después y referencia a la fila de §9 que lo autoriza.

### 10.2 Criterios (marcar en cada HU E*/F*)
| ID | Criterio verificable | HU |
|---|---|---|
| VA-01 | `body` usa Poppins; fondo `oklch(0.95 0.02 258)` y texto `oklch(0.274 0.059 260.48)` (🟢 valores del baseline) | E1 |
| VA-02 | `.field` mide ≥ 44 px de alto, radio 9 px, borde `--border`; con foco, borde `--primary` + halo 2 px; con `aria-invalid="true"` borde `--alert` | E1, F2–F5 |
| VA-03 | Botones: `flit` azul `rounded-full`, `positive` verde, `back` navy; altura mínima 44 px; sin variantes nuevas | todas |
| VA-04 | Landing: orden de secciones idéntico (§5.1); grilla `3fr/1fr` ≥ 901 px y una columna ≤ 900 px; padding lateral 50 px (12 px ≤ 640) | E2 |
| VA-05 | Tarjetas de acción: `#557EFF`, radio 9, ícono en círculo de 64 px (48 px en filas), hover `translateY(-4px)` | E2, E5 |
| VA-06 | Modales: overlay `primary/50` + blur, contenido `max-w-4xl` (o `md`/`2xl` según §4.4), radio 9, cierre 44×44 arriba a la derecha, `flit-modal-in` activo | E3–E6 |
| VA-07 | Modal Consultar: dos campos (placa + radicado/cédula), botón `flit` deshabilitado hasta completar; titular verde/amarillo/naranja según estado; `RadicadoTracking` con 5 pasos, fechas `dd/mm/aaaa hh:mm` | E3 |
| VA-08 | Modal Cotizar: 2 líneas + total; **sin** texto «simulado» | E4 |
| VA-09 | Wizard: `h1` 32 px bold; `.wizard-cols` `300px 1fr` ≥ 901 px y una columna ≤ 900 px; sidebar `sticky top 24px`; stepper con círculos de 36 px (verde/azul/gris) y `aria-current="step"` | F1 |
| VA-10 | Cada `Card` = `rounded-[9px] border bg-card p-6`, título `text-2xl font-bold` en `--primary`; orden de tarjetas por paso exactamente como §5.9 | F2–F6 |
| VA-11 | Badges del diagnóstico con forma de flecha (`badge-arrow`) y tonos `success/warning/info` (+ `alert` si se aprueba) | F2, F3 |
| VA-12 | `Drop`: altura 176 px (`h-44`), borde punteado; con archivo, ícono verde + «Da clic para reemplazar el archivo»; con error, borde `destructive` + texto | F4, F5 |
| VA-13 | Paso 5: título verde 32 px, tabla azul de 4 columnas, tracking con «Enviado al OT» en `current`, botones `flit` «Descargar comprobante» y `back` «Ir al Dashboard» | F6 |
| VA-14 | Estados nuevos usan **solo** las piezas de §7.4 (verificable por revisión de clases) | todas |
| VA-15 | 360×740: **sin scroll horizontal** en landing y wizard (🟢 hoy cumple); el Dr. FLIT fijo no oculta campos de formulario (hoy ❌, D-18) | E2, F1 |
| VA-16 | Sin texto «XXXXX», «simulado», «demostración», «sin validez oficial» ni IDs fijos en cualquier pantalla con backend real | E3, E4, F5, F6 |
| VA-17 | La URL del wizard no contiene cédula ni datos personales | E5, F1 |
| VA-18 | Accesibilidad (no visual): al cambiar de paso el foco queda en el título del paso; errores de campo con `role="alert"`; dropdown de organismo operable con teclado; los modales devuelven el foco al disparador | F1, E4 |
| VA-19 | Contraste: según la decisión de §6.3 (A: excepción documentada; B/C: pares de §6.1 ≥ 4,5:1 medidos con axe en CI) | todas |
| VA-20 | Carruseles automáticos con mecanismo para pausar/detener (WCAG 2.2.2) | E2 |
| VA-21 a VA-28 | Wizard dinámico (cualquier tipo de trámite): ver §12.8 | Feature de tipos, F1, E2, E5 |

### 10.3 Pruebas a conservar del repo (42, hoy en verde 🟢)
`b2c-demo`, `chat-demo`, `prefactura`, `radicado-demo`, `radicado-tracking`, `traspaso-costs`, `traspaso-validation` (incl. `isPagoListo`), `quote-payment-demo` (**retirar**), `app-routing`. Adaptarlas, no borrarlas, salvo las de PSE.

---

## 11. Decisiones pendientes para el PO (resumen)
1. **D-01:** ¿se confirma que el carrusel horizontal del README quedó obsoleto (se sigue el wizard con barra lateral)?
2. **§6.3:** fidelidad estricta (A), ajuste tonal (B) o híbrida (C, recomendada) para el contraste AA.
3. **D-16:** ¿enmascarar datos de terceros en Pasos 1–2 como propone la política C? (requiere visto bueno legal).
4. **D-15:** ¿bloquear **Enviar trámite** hasta que ambas partes estén validadas (y no el avance de pasos)?
5. **Fila 17:** ¿se construye el envío por correo (cotización/comprobante) o se retira el botón?
6. **Fila 6 (S3 anulado):** los tipos no elegibles, ¿visibles «Próximamente» o fuera del catálogo? (Ver §12.5.)
7. **D-13:** ¿se conserva la campana del header del wizard?
8. **D-02:** ¿falta el diálogo de confirmación al cancelar (Paso 1 → Atrás)?
9. **D-03:** entregar/exportar los 20 activos de Lovable (bloqueante).
10. **§12.7 (nuevas, por «cualquier tipo»):** diseño de los demás tipos; renderer de campos genéricos; pantalla de `fur`/firma; adición de copropietarios; stepper con N pasos; copy de los tipos nuevos. Detalle en §12.7.

---

## 12. Wizard dinámico (cualquier tipo de trámite elegible)

> **Decisión del PO (2026-10-27):** el portal sirve cualquier tipo de trámite que FLIT 2 declare disponible para persona natural; el traspaso es el piloto (ADR-0075, decisiones 10 a 13; arquitectura §4.6 y §6.4–6.5).
> **Marcas de esta sección:** ✅ **hecho verificado** (código del repo de UI, del frontend de FLIT 2 o de la investigación del 2026-10-27) · 💡 **propuesta** (no está en el repo ni aprobada). No se levantó nada nuevo en ejecución para esta sección: lo del repo de UI es 🔵 de §1–§11.

### 12.1 Qué se mantiene y qué se generaliza

| | Piloto traspaso hoy ✅ (repo) | Generalización 💡 | ¿Rompe fidelidad? |
|---|---|---|---|
| Carcasa | Header, migas «Dashboard > Trámites > Traspaso > {paso}», `h1` «Trámite de traspaso», sidebar 300 px, `.wizard-cols` | Mismos elementos; `h1` y migas toman el **nombre del tipo** de la definición («Trámite de {nombre}») | No (solo texto) |
| Ruta | `/tramite-traspaso?placa=&cedula=&tipo=&organismo=` | `/tramite/{slug-del-tipo}` sin datos personales (ya exigido por VA-17) | No visual |
| Pasos | 5 fijos: Diagnóstico, Actores, Requisitos, Pago, Envío | **N pasos**: los de la definición + **Pago** y **Envío** (siempre, propios del portal) | Sí, si N > 5 (ver §12.7-5) |
| Tarjetas | Orden fijo por paso (§5.9) | Una tarjeta por sección, en el orden de la definición, con el **mismo marcado** (`Card`, `Field`, `Row`, `Badge`, `Drop`) | No si se reutilizan las piezas |
| Textos | Títulos y etiquetas del traspaso («Propietario (vendedor)», «Comprador», «Valor de venta ($)») | Salen de `etiqueta`/`titulo` de la definición; **sin copy nuevo inventado** (D-27) | Posible: el copy debe aprobarlo el PO |
| Precio | `traspaso-costs.ts` fijo (`BASE_TRAMITE`, 3 transformaciones) | Fuente única = cotización (B5/J1) por organismo y tipo | No visual |
| Resumen del Paso 5 | «Vehículo, Actores, Condiciones Comerciales, Valor Total, Documentos del traspaso» | Se arma desde la definición: vehículo, actores por rol, campos, documentos cargados, total | Sí si cambian los bloques (§12.7-6) |

**Lo que no cambia sin aprobación (regla §2.1 se mantiene):** tokens, Poppins, radios, variantes de `Button`, patrón de modal, animaciones y clases CSS referenciadas por selectores. La generalización **usa las clases existentes**; no se crean estilos nuevos.

### 12.2 Mapeo `section_type` → tarjeta o renderer del repo

El wizard de FLIT 2 pinta por `section_type` con un registro fijo ✅ (`sectionRendererRegistry.ts`: consulta, documentos, actores, prenda, identidad, fur, genérico). El portal necesita un registro equivalente 💡. Mapeo contra las piezas **del repo `transfer-step-trail`**:

| `section_type` (FLIT 2) ✅ | Tarjeta/renderer del repo que lo cubre | Cobertura 💡 | Qué hay que generalizar |
|---|---|---|---|
| `consulta` | Paso 1: «Diagnóstico de requisitos previos» (`Row` + `Badge` flecha, `.wizard-diag-row`, caja amarilla de atención) y «Datos técnicos del vehículo» (`dl`) | **Parcial** | Hoy son 6 filas RUNT fijas (Estado, SOAT, RTM, Gravámenes, Impuestos, Multas). Cada tipo tiene su preflight (`EvaluarRequisitos`): las filas salen de `Requisito[]` (código, descripción, estado `cumple/falta/no_aplica`) con los tonos `success/warning/info` existentes. Datos técnicos: solo los que la política de dato mínimo permita (D-16) |
| `actores` | «Propietario (vendedor)» y «Comprador» (`Field`, `AddressField` con tooltip, badges de validación, «Reenviar Validación», «Consultar comprador») | **Buena** | Una tarjeta **por `ActorRol`** de la definición, mismo marcado; el título es la `etiqueta` del rol. «Consultar {rol}» solo si el rol lo admite. Roles con `max > 1`: control de **agregar actor** (no existe en el repo, §12.7-3) |
| `identidad` | No hay paso propio: la validación vive **dentro** de la tarjeta de actor (badge «Validación: …» + «Reenviar Validación») | **Buena** | Se mantiene así: `identidad` no abre una pantalla; los roles con `biometria = true` muestran el badge y el reenvío. Sin renderer nuevo |
| `documentos` | Tarjeta «Documentos» con `Drop` (Improntas opcional, Documentos del trámite opcional, Paz y salvo de impuestos\*) | **Buena** | Un `Drop` por `DocumentoRequerido` (`nombre`, `obligatorio`, `instruccion_cargue`, `orden`); obligatorio = título con \* y sin «Opcional». `accept` y límite de 20 MB se conservan. `condicion` ya llega resuelta en texto |
| `prenda` | Tarjeta «Prenda / limitación a la propiedad» (radio No/Sí, acreedor, NIT/documento, `Drop` del certificado) | **Parcial** | Sirve cuando el tipo solo **declara** prenda. El `PrendaGate` completo (acreedor, soporte) vive en código y el acreedor suele ser persona jurídica → esos tipos quedan no disponibles hasta cerrarse (ADR decisión 12) |
| `generico` (`form_fields`) | **No existe en el repo** ⚠ | **Ninguna** | Pieza nueva: un renderer que pinte cada `Campo` (`tipo`, `obligatorio`, `opciones`, `validation_schema`) con `Field` + `.field` + `TransactionSelect` (select) + `Switch` (checkbox) + `Drop` solo si aplica. Es el **cambio de mayor tamaño**: ver D-25 |
| `fur` | **No existe en el repo** ⚠ (FLIT 2: `FirmaFurStep.tsx`) | **Ninguna** | El repo cierra con «Envío y confirmación» sin firma del FUR. Un tipo con `section_type = fur` **no es elegible** (E5 de arquitectura §6.4) hasta que el PO decida cómo se firma (§12.7-4) |

Flags de la definición que activan tarjetas del repo 💡: `requiere_comercial` → «Datos comerciales» (Avalúo Fasecolda, Valor de venta); `permite_transformaciones` → «Transformaciones del vehículo» (3 `Switch` con precio `+ $…`, precios desde J1 y solo en tipos que las permiten); `requiere_prenda` / `permite_prenda_complementaria` → «Prenda»; `entry_mode` → el formulario de inicio pide **placa**, **VIN** o ambos (hoy solo placa, §5.1).

**Siempre presentes (no vienen de la definición) ✅:** **Pago** (tarjetas «Elección de facturación», «Resumen del cobro», «Datos bancarios FLIT», «Comprobante de pago» — S1) y **Envío** («Envío y confirmación», con tracking). Se mantienen como están.

### 12.3 Stepper y sidebar con N pasos 💡

- El stepper vertical de la sidebar (`Paso n · …`, círculo de 36 px verde/azul/gris, `aria-current="step"`) se genera desde `pasos[]` + «Pago» + «Envío». Mismo marcado y mismos tres estados.
- «Configuración del trámite»: *Organismo* (hoy solo lectura; F1 ya propone volverlo seleccionable), *Tipo de Trámite* **solo lectura** una vez abierta la sesión (cambiar el tipo cambia la definición y el snapshot: ver §12.5 «definición obsoleta»), *Valor del trámite* desde la cotización.
- Con más de 5 pasos el sidebar crece (hoy ya mide 656 px a 360 px, D-18): límite sugerido de **7 pasos**; si un tipo supera ese número, se agrupan secciones en un mismo paso (decisión del PO, §12.7-5).
- El chip «Novedad: subsanar documento» y la subsanación (`?subsanar=true`, §5.10) se conservan; la subsanación apunta al paso que contiene el documento observado, no necesariamente al «Paso 3».

### 12.4 Pantalla de elección de tipo (landing y modales) 💡

- **«Tipo de Trámite»** (`TransactionSelect` de la tarjeta «Iniciar mi trámite») lista los tipos de `GET /tipos-tramite`; los no disponibles se muestran **deshabilitados con la píldora «Próximamente»** (la misma de los servicios adicionales, §5.6).
- **«Requisitos para tu trámite»** (§5.2): las 6 tarjetas fijas dejan de ser fijas; se generan de `documentos` obligatorios y de los requisitos previos del tipo, en la misma grilla `grid-cols-2 sm:3 lg:6`. Si hay más de 6, la grilla hace wrap (no se inventa otro diseño).
- **«Cotizar»** y **«Tarifas»**: el select de tipo viene del catálogo real; una combinación sin tarifa vigente muestra el mensaje de §5.4 («sin tarifa para esa combinación») y el tipo no se ofrece.
- **«Consultar»**: sin cambio estructural; «Tipo de trámite» sale del radicado.

### 12.5 Estados nuevos del wizard dinámico (propuestos; solo piezas de §7.4)

| Estado | Cuándo | Presentación 💡 |
|---|---|---|
| **Cargando definición** | Entre elegir el tipo/organismo y mostrar el primer paso | Overlay `MobilityLoader` (`bg-background/80 backdrop-blur-sm`); dura lo que dure la llamada, sin temporizador falso |
| **Error al cargar la definición** | Red, `503`, `429` | Caja de alerta (`border-alert bg-alert/10`, `role="alert"`, ícono `AlertTriangle`) con botón `back` «Reintentar»; no se pinta un wizard a medias |
| **Tipo no disponible** (elección) | Tipo con `disponible = false` en el catálogo | Opción deshabilitada con la píldora «Próximamente» en el select y en la landing |
| **Tipo no disponible** (deep link / ya no elegible) | `404 tipo_no_disponible` al abrir `/tramite/{slug}` o al iniciar | Caja de alerta «Este trámite aún no está disponible en línea.» con el `motivo` si es presentable + botón `flit` «Volver al inicio» |
| **Definición sin pasos** o con `section_type` desconocido | Defensa del front (el BFF ya no debería entregarlo) | Mismo estado que «tipo no disponible»; **nunca** pintar solo una parte |
| **Definición obsoleta** | `409 definicion_obsoleta` al radicar, o `solicitud_resuelta` con ese `errorCode` | Modal estilo *legal* (`max-w-md`, `ModalTitleIcon`, botón `flit`): «La información de este trámite cambió. Inicia uno nuevo para continuar.» **Requiere aprobación de copy** |
| **Sin datos** (vacío) | Rol con `min = 0` sin actores aún; sección sin documentos | Texto `text-sm text-muted-foreground`, sin tarjeta vacía |
| **Lleno** | Definición cargada | Como el piloto |

### 12.6 Desviaciones nuevas por el alcance (no cambiar sin aprobación)

| ID | Tema | Evidencia | Acción propuesta |
|---|---|---|---|
| D-21 | Wizard hardcodeado al traspaso: ruta `/tramite-traspaso`, `h1`, migas, textos y `isStepValid` por paso fijo | ✅ 🔵 `tramite-traspaso.tsx`, `traspaso-validation.ts` | Wizard por definición; validación por campo según `obligatorio`/`validation_schema`; renombrar sin cambiar clases |
| D-22 | Stepper fijo de 5 pasos | ✅ `STEPS` y `BREADCRUMB_STEPS` en el código | Generar N pasos (§12.3); límite 7 a decidir |
| D-23 | Diagnóstico = 6 filas RUNT fijas | ✅ Paso 1 | Filas desde `Requisito[]` del preflight del tipo (§12.2) |
| D-24 | Resumen del Paso 5 y «Documentos cargados del traspaso» fijos | ✅ Paso 5 | Resumen y lista de documentos desde la definición |
| D-25 | **No existe renderer de campos genéricos** (`form_fields`) | ✅ el repo solo tiene campos concretos | Pieza nueva con `Field`/`.field`/`TransactionSelect`/`Switch`; sin estilos nuevos; **aprobar** el aspecto de los tipos `date`, `textarea`, `number` |
| D-26 | **Sin diseño para `fur`/firma** ni para agregar copropietarios | ✅ no hay pantalla ni control | Decidir antes de habilitar tipos con esas secciones (§12.7) |
| D-27 | **No existe copy** de los otros tipos (requisitos del modal «Requisitos», ayudas, FAQ, textos legales por tipo) | ✅ solo hay copy de traspaso | No inventar: entrega el PO; mientras tanto se usan `etiqueta`/`titulo`/`instruccion_cargue` de la definición |
| D-28 | `TRANSFORMACIONES` y `BASE_TRAMITE` son del traspaso | ✅ `traspaso-costs.ts` | Transformaciones solo si `permite_transformaciones`; precios desde J1 |

### 12.7 Decisiones pendientes para el PO (por el alcance «cualquier tipo»)

1. **Diseño de los demás tipos:** el repo solo diseña el traspaso. ¿El PO acepta que el wizard dinámico (misma carcasa y tarjetas, §12.1–§12.2) sea el diseño de todos los tipos, o prefiere diseños específicos para algunos (p. ej. duplicados, prenda)?
2. **Renderer de campos genéricos (D-25):** aprobar su aspecto (reutiliza `.field`, selects y `Switch`).
3. **Agregar copropietarios** (`max > 1`): ¿control «Agregar {rol}» con `Btn` `back`, o solo un actor por rol en el portal? (FLIT 2 admite hasta 4 por rol, ADR-0053.)
4. **`fur`/firma:** ¿cómo firma el ciudadano el FUR en el portal (enlace de firma, firma en el Paso Envío, o el tipo no es elegible)? Hasta decidirlo, los tipos con `fur` quedan fuera.
5. **N pasos:** ¿límite de 7 con agrupación de secciones, o el sidebar con scroll?
6. **Resumen del Paso 5:** ¿se acepta el resumen genérico por definición en lugar de los bloques del traspaso?
7. **Copy de los tipos nuevos (D-27):** entrega del PO de requisitos, ayudas y FAQ por tipo; mientras tanto, solo los textos de la definición.
8. **Lista blanca y orden de habilitación:** ¿qué tipos entran después del piloto (probables: `DUPLICADO_PLACA`, `DUPLICADO_TARJETA`, `CAMBIO_COLOR`, `CONVERSION_COMBUSTIBLE`, según la inferencia de la arquitectura §6.5)?

### 12.8 Criterios de aceptación visual nuevos (verificables; complementan §10)

| ID | Criterio verificable | HU |
|---|---|---|
| VA-21 | Con el tipo piloto (`TRASPASO_STANDARD`), el wizard dinámico es **visualmente idéntico** al baseline del repo (mismas capturas de §10.1 con `maxDiffPixelRatio ≤ 0,002`, datos dinámicos enmascarados) | Feature de tipos, F1–F6 |
| VA-22 | `h1` y migas usan el nombre del tipo de la definición; sin la palabra «traspaso» fija en rutas, `h1`, migas ni etiquetas de tipos distintos del traspaso | F1 |
| VA-23 | El stepper pinta N botones (`pasos[]` + Pago + Envío) con el mismo marcado (círculo de 36 px, `aria-current="step"`); el último paso alcanzable depende de la validación de los pasos previos | F1 |
| VA-24 | Cada sección de la definición se pinta con la tarjeta de §12.2 (mismas clases); un `section_type` desconocido **no** pinta parcialmente: muestra el estado «tipo no disponible» | Feature de tipos |
| VA-25 | El renderer genérico usa solo `.field`, `TransactionSelect`, `Switch` y `Drop`; sin clases ni estilos nuevos (verificable por revisión de clases); campos obligatorios con \*, errores con `aria-invalid` y `role="alert"` | Feature de tipos |
| VA-26 | «Tipo de Trámite» lista los tipos del catálogo real; los no disponibles quedan deshabilitados con la píldora «Próximamente» | E2, E5 |
| VA-27 | Estados de §12.5 (cargando, error, no disponible, definición obsoleta) usan **solo** piezas de §7.4 (verificable por revisión de clases) | Feature de tipos |
| VA-28 | No hay texto, ruta ni constante de un tipo concreto en el código del front fuera del registro de renderers y de las pruebas del piloto (verificable con búsqueda de `TRASPASO`/`traspaso` en `src/` salvo pruebas) | Feature de tipos |

---

### Anexo — cómo se obtuvo la evidencia
- Copia del repo (sin `.git`) en el scratchpad → `npm install --legacy-peer-deps` (773 paquetes) → `vite dev --port 5199` → Chrome (extensión). El repo original quedó **sin cambios** (`git status` vacío).
- Contraste: script Node propio (oklch → sRGB → luminancia relativa WCAG) sobre los valores de `:root`; mezclas `/10` calculadas contra blanco.
- 360 px: `iframe` del mismo origen de 360×735 (viewport efectivo 357 px) porque el redimensionado de ventana no surtió efecto.
