# Diligenciamiento del FUR (Anexo 46) — ejemplares FLIT

El blank oficial se titula **FORMULARIO DE SOLICITUD DE TRÁMITES DEL REGISTRO NACIONAL AUTOMOTOR** (Ministerio de Transporte). En código FLIT: overlay FUR (`FurOverlayDocumentGenerator` + `FurFieldMapper`).

**Carpeta de verdad visual:** `docs/ot/fur/` (PDFs de 2 páginas, diligenciados). Abrir el archivo más cercano al escenario; no inventar un layout distinto.

**Reglas de casillas y observaciones (desarrollo):** `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md`. Casillas = tipo base ∪ prenda ∪ transformaciones. Ese archivo manda sobre esta nota operativa.

---

## Estructura del anverso (campos que el equipo nombra a diario)

1. **Organismo de tránsito** — nombre, ciudad, código.
2. **Placa** — letras / números y fecha del trámite (día, mes, año).
3. **Trámite solicitado** — rejilla de 18 casillas (ver abajo). Varias X si hay trámites simultáneos del mismo vehículo (art. 5.1.8).
4–18 (aprox.): clase, marca, línea, combustible, colores, modelo, cilindraje, carrocería, motor/chasis/serie/VIN, servicio, empresa vinculadora, observaciones.
**Firmas:** propietario (matrícula / vendedor en traspaso) y comprador (solo cuando el trámite lo exige). Junto a la imagen: auditoría tipo `Doc. / NOMBRE / Vig. AAAA/MM/DD — AAAA/MM/DD / Hash:` cuando la firma viene del baúl.

Plantillas FLIT: automotor, remolques, maquinaria (`FurTemplateFormat`). Los ejemplares de `docs/ot/fur` son el formato **automotor**.

---

## Numeral 3 — rótulos oficiales (1–18)

| N.º | Rótulo en el blank |
|-----|-------------------|
| 1 | Matrícula / Registro |
| 2 | Traspaso |
| 3 | Traslado matrícula / registro |
| 4 | Radicado matrícula / registro |
| 5 | Cambio de color |
| 6 | Cambio de servicio |
| 7 | Regrabar motor |
| 8 | Regrabar chasis |
| 9 | Transformación |
| 10 | Duplicado licencia tránsito |
| 11 | Inscrip. prenda |
| 12 | Levanta prenda |
| 13 | Cancelación matrícula / registro |
| 14 | Cambio de placas |
| 15 | Duplicado de placas |
| 16 | Rematrícula |
| 17 | Cambio de carrocería |
| 18 | Otros |

---

## Qué marca FLIT (`FurNumeral3Marks` + `FurFieldMapper.MarkTramite`)

Casillas = tipo base ∪ prenda ∪ transformaciones. Detalle y literales del párrafo 23: `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md`.

El mapper emite X en las casillas 1–5, 7, 8, 10–13, 15–18 que el resolver marque. **No** declara 6 ni 14.

Blindaje: SI/NO en características (`is_armored_vehicle_*`), no una casilla 3–18 dedicada.

Firmas: matrícula → solo propietario. Traspaso estándar → vendedor + comprador. Traspaso unilateral (art. 5.3.2.2) → **no** firma del locatario.

---

## Índice de ejemplares (`docs/ot/fur`)

Usar el nombre del archivo como clave de escenario. PN = persona natural, PJ = jurídica, VID = validación de identidad, baúl = firma custodiada.

| Archivo | Escenario a estudiar |
|---------|----------------------|
| `Fur_Matricula_PN Normal.pdf` | Matrícula, propietario PN, caso base |
| `Fur_Matricula_MultiplesP (Ambos PN).pdf` | Matrícula con múltiples propietarios PN |
| `Fur_Matricula_PJ_Firma VID.pdf` | Matrícula PJ, sello de identidad |
| `Fur_Matricula_PJ_Firma Baul.pdf` | Matrícula PJ, firma de baúl + auditoría |
| `Fur_Matricula_PJ_Con Transformaciones.pdf` | Matrícula PJ + transformaciones simultáneas |
| `Fur_MatriculaLeasing+Transformacion.pdf` | Matrícula leasing + transformación |
| `Fur_Traspaso_PNvidxPNvid.pdf` | Traspaso PN×PN, ambas identidades |
| `Fur_Traspaso_PJvid xPNvid.pdf` | Traspaso PJ×PN |
| `Fur_Traspaso_PJbaulxPJvid.pdf` | Traspaso PJ baúl × PJ VID |
| `Fur_TraspasoUnilateral_PJxPJ.pdf` | Traspaso unilateral (leasing) PJ×PJ |
| `Fur_TraspasoPrenda_PJxPN.pdf` | Traspaso + prenda |
| `Fur_Traspaso+Transformacion_PJxPN.pdf` | Traspaso + transformación |
| `Fur_Traspaso+Transformacion+Prenda_PNxPJ.pdf` | Traspaso + transformación + prenda |
| `Fur_OtroTramite_CambioColor_PJ.pdf` | Cambio de color (casilla 5) |
| `Fur_OtroTramite_CambioCombus.pdf` | Conversión de combustible (en FLIT: 18) |
| `Fur_OtroTramite_Blindaje.pdf` | Blindaje |
| `Fur_OtroTramite_InscrPrenda PN.pdf` | Inscripción de prenda (11) |
| `Fur_OtroTramite_LevantPrenda.pdf` | Levantamiento de prenda (12) |
| `Fur_OtroTramite_CancelacionMatricula.pdf` | Cancelación (rótulo 13; mapper FLIT aún no) |
| `Fur_OtroTramite_DuplicadoPlaca.pdf` | Duplicado de placas (rótulo 15; mapper FLIT aún no) |
| `Fur_OtroTramite_DuplicadoTarjeta PJ.pdf` | Duplicado licencia/tarjeta (rótulo 10; mapper FLIT aún no) |

Al leer un PDF, anota organismo, placa, fecha, marcas del numeral 3, datos de vehículo y bloque de firmas. El OCR del blank incluye todos los rótulos aunque no estén marcados: **no** tomes la presencia del texto “TRASPASO” como X marcada.

---

## Relación con el código

- Overlay: `services/core-api/src/Flit.Infrastructure/Documents/Fur/`
- Simulador SuperAdmin: `frontend/app/admin/plataforma/fur/`
- Catálogo de tipos: `tramites.procedure_types`

Si el usuario pregunta “¿el simulador está igual que un trámite real?”, el motor de PDF es el mismo; los datos del simulador son sintéticos. El ExpertDocEngine dictamina la **forma** del documento, no el expediente RUNT.
