# FEATURE — Mejoras transversales: documentos generados, generación documental, trámites, compañías y usabilidad

> **✅ REGISTRADO EN ADO** (2026-07-29). Feature **#11045** y 20 HUs **#11046–#11065** creadas en
> `FLIT - EVOLUTION\Sprint 3`, estado `New`, asignadas a Juan Felipe Montoya Garcia, con descripción,
> criterios de aceptación en Gherkin, story points y vínculo padre-hijo verificados (20 hijos).
>
> **Tags aplicados** (2026-07-29): `adopcion-ia; DOR; fase-1-diseño`. El `403 Forbidden` inicial no
> era una limitación del PAT sobre los Features: el script enviaba `fase-1-diseno` **sin tilde**, un
> tag que no existe en el proyecto, y el PAT aplica tags existentes pero no crea nuevos.

| Campo | Valor |
|-------|-------|
| Tipo | `[FULLSTACK]` |
| Estado | `New` (sin activar — la activación es gate humano) |
| Sprint | El **siguiente** al activo (nunca el activo) |
| AssignedTo | Juan Felipe Montoya Garcia (juan.montoya@flitsas.com) |
| Tags | `DOR; adopcion-ia; fase-1-diseño` |
| ADO ID | **#11045** |
| Origen | `modificaciones.txt` |
| Diseño técnico | `docs/plan-tecnico-mejoras-modificaciones.md` |
| Rama de trabajo | `wip/mejoras-modificaciones` (auxiliar; se renombra a `feature/AB-<featureId>-mejoras-modificaciones` al registrar en ADO) |

---

## OBJETIVO

Atender el paquete de mejoras reportado por el negocio sobre los documentos que genera FLIT, la
generación documental del expediente, el módulo de trámites, la administración de compañías y
representantes legales, y la usabilidad de tablas y formularios.

## DESCRIPCIÓN

Paquete de 19 ajustes reportados por el negocio, descompuesto en 20 Historias de Usuario sobre cinco
frentes:

- **Documentos generados** — la firma del mandato y de la solicitud de trámite virtual debe quedar
  sobre la línea; el mandato debe traer los datos completos del mandante; la razón social larga no
  debe desbordarse del FUR; las fechas de los certificados deben ir sin hora.
- **Generación documental** — con el trámite aprobado la documentación no se regenera, y el
  expediente consolidado queda como único disparador de generación.
- **Trámites** — consulta de documentos y del consolidado desde el listado, y rediseño de columnas.
- **Compañías** — precarga de compañías asociadas del representante legal, renovación de identidad y
  firma vencidas, elección de la firma que se registra en el trámite, identificación de la compañía
  en configuración y carga de escrituras más simple.
- **Usabilidad** — área clickeable de los botones de icono, y orden del resumen de identidad.

### Ajustes del reporte original que ya estaban resueltos en `develop`

No generan HU (verificado contra código):

| Ajuste reportado | Dónde quedó resuelto |
|------------------|----------------------|
| Quitar el botón de solicitar firma de la compraventa | HU #11019 — `FirmaFurStep.tsx:1175` |
| Vendedor antes que comprador en el resumen de firmas y el expediente | HU #11019 / #11020 |
| Generar en cascada al consolidar (FUR + impronta) | HU #10860 / #11017 — `ConsolidadoCommand.cs` |
| Permitir el mismo correo en comprador y vendedor | HU #11019 — front `ActorsForm.tsx:196-203` y back `TraspasoPartes.MensajeDuplicadas` |

## CRITERIOS FUNCIONALES

1. La estampa de firma, por validación de identidad o por baúl, se plasma sobre la línea de firma en
   el mandato y en la solicitud de trámite virtual, y corresponde a la firma registrada con el trámite.
2. El bloque de identificación del mandato incluye empresa, NIT, nombre, documento, celular y correo,
   y omite empresa y NIT en persona natural.
3. Una razón social larga no se desborda del campo de nombre del FUR.
4. Las fechas de los certificados generados que entran al consolidado se muestran en formato
   `AÑO/MES/DÍA` sin hora.
5. Con el trámite aprobado o anulado el gestor no puede regenerar documentación, y la regeneración que
   dispara la aprobación del organismo de tránsito sigue funcionando.
6. El expediente consolidado es el único disparador de generación y produce en cascada lo que falte.
7. El aviso del detalle del trámite describe el estado real del trámite.
8. Desde el listado de trámites se consultan en línea los documentos del expediente y el consolidado,
   sin entrar al detalle.
9. La tabla de trámites muestra las columnas acordadas con el negocio y respeta el diseño UI vigente.
10. El directorio de representantes legales permite ver las compañías asociadas al editar, renovar
    identidad y firma vencidas, y elegir la firma que se registra en el trámite.
11. El administrador de configuración identifica en todo momento la compañía que se está modificando y
    la carga de escrituras se resuelve sin recorrer varios niveles.
12. Los botones de icono de las tablas responden en toda su superficie y el resumen de identidad
    muestra primero el vendedor.

---

## Historias de Usuario

| HU | ADO | Tipo | SP | Título | Estado |
|----|-----|------|----|--------|--------|
| [HU01](HU01-firma-sobre-linea-mandato-virtual.md) | #11046 | BACKEND | 5 | Estampa de firma sobre la línea en mandato y solicitud de trámite virtual | **Implementada y verificada** (`c436b4bf`) |
| [HU02](HU02-datos-mandante-mandato.md) | #11047 | BACKEND | 3 | Bloque de identificación completo del mandante en el mandato | **Implementada y verificada** (`c436b4bf`) |
| [HU03](HU03-fur-nombre-empresa-largo.md) | #11048 | BACKEND | 5 | Ajuste automático del nombre de empresa largo en el FUR | **Implementada y verificada** (`6f7fd6f7`) |
| [HU04](HU04-fecha-sin-hora-certificados.md) | #11049 | BACKEND | 3 | Fecha AÑO/MES/DÍA sin hora en los certificados del consolidado | **Implementada y verificada** (`b37f5310`) |
| [HU05](HU05-cascada-consolidado-completa.md) | #11050 | BACKEND | 5 | Cascada del consolidado: avisos de lo que no se pudo generar (AC1/AC2 ya existían) | **Implementada y verificada** (`3163fc19`) |
| [HU06](HU06-bloqueo-regeneracion-aprobado.md) | #11051 | BACKEND | 5 | Bloqueo de regeneración documental con el trámite aprobado | **Implementada y verificada** (`db22eb0d`) |
| [HU07](HU07-consolidado-unico-disparador.md) | #11052 | FRONTEND | 3 | Consolidado como único disparador de generación en el paso FUR | **Implementada y verificada** (`298111f4`) |
| [HU08](HU08-aviso-estado-real.md) | #11053 | FRONTEND | 3 | Aviso del detalle del trámite acorde al estado real | **Implementada y verificada** (`0efe1b33`) |
| [HU09](HU09-ver-documentos-desde-listado.md) | #11054 | FRONTEND | 5 | Ver los documentos del expediente desde el listado de trámites | **Implementada y verificada** (`2af73838`) |
| [HU10](HU10-consolidado-en-tabla.md) | #11055 | FRONTEND | 3 | Acceso al PDF consolidado desde la tabla de trámites | **Implementada y verificada** (`2af73838`) |
| [HU11](HU11-resumen-listado-ampliado.md) | #11056 | BACKEND | 5 | Resumen del listado con actualización, gestor, fuente y firma por parte | **Implementada y verificada** (`baf8b100`) |
| [HU12](HU12-columnas-tabla-tramites.md) | #11057 | FRONTEND | 8 | Rediseño de columnas de la tabla de trámites | **Implementada y verificada** (`2af73838`) |
| [HU13](HU13-precarga-companias-rl.md) | #11058 | FULLSTACK | 5 | Precarga de las compañías asociadas al editar un representante legal | **Implementada y verificada** (`5354d155`) |
| [HU14](HU14-renovar-identidad-firma-rl.md) | #11059 | FULLSTACK | 5 | Renovación de identidad o firma del baúl vencidas de un representante legal | **Implementada y verificada** (`5354d155`) |
| [HU15](HU15-renovar-identidad-mandatario.md) | #11060 | FULLSTACK | 5 | Renovación de la identidad vencida del mandatario del OT | **Implementada y verificada** (`5354d155`) |
| [HU16](HU16-seleccion-firma-tramite.md) | #11061 | FULLSTACK | 5 | Selección de la firma que se registra en el trámite | **Implementada y verificada** (`5354d155`) |
| [HU17](HU17-titulo-compania-configuracion.md) | #11062 | FRONTEND | 2 | Identificación de la compañía en el administrador de configuración | **Implementada y verificada** (`5354d155`) |
| [HU18](HU18-carga-escrituras-intuitiva.md) | #11063 | FRONTEND | 5 | Carga y actualización de escrituras por compañía más intuitiva | **Implementada y verificada** (`5354d155`) |
| [HU19](HU19-area-clickeable-iconos.md) | #11064 | FRONTEND | 3 | Área clickeable de los botones de icono en las tablas | **Implementada y verificada** (`4605c4c0`) |
| [HU20](HU20-correo-repetible-DESCARTADA.md) | — | — | 0 | Correo repetible entre comprador y vendedor | **Descartada — ya resuelta en `develop`; NO se registró en ADO** |
| [HU21](HU21-orden-identidad-vendedor-primero.md) | #11065 | FRONTEND | 3 | Vendedor antes que comprador en el resumen de identidad | **Implementada y verificada** (`c2c60902`) |

**Total: 86 SP en 20 HUs activas.**

## Orden de ejecución

1. **Usabilidad** — HU19, HU21 ✅
2. **Generación documental** — HU06 → HU07 → HU08 (el guard de backend antes de ocultar botones) ✅
3. **Documentos generados** — HU01, HU02, HU04 en paralelo → HU03 → HU05 (después de HU06) ✅
4. **Trámites** — HU11 → HU12; HU09 y HU10 en paralelo desde el inicio ✅
5. **Compañías** — HU13, HU17 → HU14, HU15 → HU16 → HU18 ✅

**Progreso: 20/20 HUs · 86/86 SP — las 20 HUs implementadas y verificadas.**

Pendiente para cerrar el Feature: verificación **visual** de los documentos generados y de las
pantallas nuevas, renombre de la rama y **PR a `develop`** (gate humano de merge). El `Closed` del
Feature es exclusivo del PO.

## Trazabilidad al registrar en ADO

Al recuperar el acceso a la API:

1. Ejecutar `ado-create.ps1` (crea el Feature y las HUs, idempotente por título vía WIQL).
2. Anotar el ID real en la tabla de arriba y en el encabezado de cada HU.
3. Renombrar la rama: `git branch -m wip/mejoras-modificaciones feature/AB-<featureId>-mejoras-modificaciones`.
4. Reescribir los mensajes de commit al formato `HU<id>: descripción` (los commits locales usan el
   código provisional `HU01:`…`HU21:` mientras no exista el ID de ADO).
