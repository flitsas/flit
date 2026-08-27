# HU-L10 — Plantilla por OT, sin mandato compuesto

> Generado: 2026-08-27  
> Registro local (sin work item ADO). Rama: `review/rl-ficha-aislada-escritura-unica`.

## Objetivo

Dejar de **recomponer** la plantilla al emitir (PN/PJ/modo → otra redacción). Cada organismo usa **la suya**; el resto nace en genérico. Quién firma sigue la cascada HU-L8 y los tres modos de negocio.

## Plantilla al nacer / por defecto

| OT (código RUNT) | Plantilla |
|------------------|-----------|
| Sabaneta `5631000` | `sabaneta` |
| Bello `5088000` | `bello` |
| Envigado `5266000` | `municipio` |
| Funza `25286000` | `municipio` |
| Medellín `5001000` | `municipio` |
| Cualquier otro | `generico` |

El **modo** al nacer sigue siendo `open` (sin firmante persona). El OT puede pasar a Mandato OT o abierto; el cliente configura Mandato cliente.

## Quién firma (sin cambios)

| Tipo | Quién lo configura | Plantilla | Firmante |
|------|--------------------|-----------|----------|
| Mandato OT (`institutional` / firmante del OT) | OT | Plantilla del organismo | Default OT > default compañía (HU-L8); institucional sin bloque persona |
| Mandato abierto (`open`) | OT | Plantilla del organismo | Sin firmante; líneas / recuadro abierto |
| Mandato cliente (`signer` en regla compañía×OT) | Cliente | Genérica (salvo PDF/editor propio) | Default OT > default compañía |

## Qué se retiró

`MandatoTemplateResolver.ResolveEmissionCode`: ya no se fuerza Sabaneta en PJ/institucional ni genérico en abierto/PN.

## Datos

- Nacimiento: `MandatoOtBirthDefaults.ForOffice`.
- Backfill: `96-ot-birth-template-by-office.sql` (solo filas `generico` sin plantilla propia).
