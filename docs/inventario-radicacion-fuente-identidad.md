# Inventario — la ruta de radicación frente a la fuente de identidad

> HU #11760 (CF-15 a) · Feature #11689 · 2026-08-21
> Rama `feature/AB-11687-ola-identidad`, sobre `develop` @ `13fb9cf9`.
> Contrato: `ADR-0050-identidad-fuente-unica-y-disparo-unico.md` (`Propuesto`).

Este documento es el entregable del CF-15 (a): qué puntos de la ruta de radicación dependían de la
**fuente administrativa** de identidad (`admin.admin_identity_validations`) o del **envío tardío** del
correo de validación, y en qué estado quedan tras las HUs de esta ola.

**Es el insumo del gate humano.** El alcance del CF-15 (b) — HU #11761 — no se compromete hasta que
el PO o el Líder Técnico lo lea.

---

## 1. Dependencias de la fuente administrativa

| # | Punto | Ancla | Antes de la ola | Ahora |
|---|---|---|---|---|
| 1 | Gate de comprador y vendedor | `SubmitGate.cs:43-152` → `IdentityApprovalResolver.cs:14-167` | **Nunca dependió** de la tabla admin. Resuelve en 3 pasos: baúl (`ISignatureVaultPolicy`, D8 del ADR-0025), fila propia del trámite, y referencia cross-trámite por documento (`ProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync:330-350`) | Sin cambios |
| 2 | Directorio de mandatarios y certificado del sello | `MandateSignerDirectory.cs` | Leía `admin.admin_identity_validations` con `SET LOCAL row_security = off` | **Migrado** (HU #11752) a `tramites.procedure_instance_biometric_validations` |
| 3 | Rótulo de identidad de la ficha del representante legal | `DbLegalRepresentativeReader.LoadIdentityVigenciaAsync` | Leía la tabla admin. **Era el defecto original de las novedades 8 y 9** | **Migrado** (HU #11765) |
| 4 | Rótulo de identidad de la ficha del mandatario | `DbMandateSignerReader.LoadIdentityVigenciaAsync` | Ídem | **Migrado** (HU #11765) |
| 5 | Política de firma del mandato | `MandatoFirmaPolicy.cs:38-59` | **No lee identidad en absoluto** | Sin cambios |

**Estado verificado el 2026-08-21:** `grep -rn "\.AdminIdentityValidations\b" services/core-api/src`
fuera de las migraciones devuelve **cero resultados**. La tabla sigue existiendo, sin ningún lector de
producción — retirarla es otra decisión y está fuera del alcance de esta ola.

### Lo que el refinamiento no había visto

El refinamiento (2026-08-20) daba por hecho que **el único** punto era el directorio de mandatarios
(fila 2). Eran **tres**: se le escaparon las filas 3 y 4, que son precisamente las que producen el
rótulo que ve el operador — es decir, **el requerimiento que originó la ola**. El propio ADR-0050
arrastró el error en su §Contexto, al atribuir la lectura de la ficha a `MandateSignerDirectory`;
está corregido en los comentarios del código de la HU #11765.

---

## 2. Dependencia del envío tardío del correo

La premisa del CF-15 (b) era: *si nadie prevalida, el envío del correo se corre al momento de radicar,
y eso desplaza carga y modos de fallo al gate.*

**Esa premisa es falsa.** El disparo del correo de validación al representante legal de una parte
jurídica **no está en la radicación**: está en el **paso 2 del asistente**, al guardar actores.

- `ActorsCommand.cs:305` → `EnviarValidacionAlRepresentanteDeLaParteJuridicaAsync` (`:347-390`).
- Quien decide si de verdad se envía es el handler de Kyverum, que evalúa la **precedencia única del
  ADR-0039**: si la persona está cubierta por el baúl, ya tiene identidad vigente, o ya tiene una
  validación en vuelo, devuelve la decisión y no crea nada.
- Cada omisión deja un motivo tipificado (`EnvioValidacionBloqueoRules`, HU #11665) en un log de
  negocio, derivado del estado y no persistido.

Ese disparador pertenece a la **ruta del trámite**, no al área admin, y **esta ola no lo tocó**: lo que
se retiró fueron los disparos administrativos (HUs #11757, #11758 y #11764). Por tanto **nada se
desplaza al momento de radicar**: el envío ya ocurría antes, en el paso 2, y sigue ocurriendo ahí con
la misma precedencia.

---

## 3. Conclusión y recomendación

**El CF-15 (a) se cierra sin hallazgos abiertos.** Los cinco puntos de la ruta de radicación están
inventariados: dos nunca dependieron de la fuente admin, tres dependían y los tres están migrados
dentro de esta misma ola.

**Recomendación sobre el CF-15 (b) / HU #11761: no requiere cambios.** Su alcance estaba condicionado
a que el envío se corriera al radicar, y eso no ocurre. Se propone cerrarla como *sin cambios
necesarios*, dejando constancia del porqué, en vez de inventarle contenido.

**La dependencia hacia el Feature #11348 no se activa.** Estaba condicionada a la misma premisa: si el
envío se hubiera desplazado a la radicación, habría sido un caso de enrutamiento nuevo. Como no se
desplaza, el #11348 no hereda nada de esta ola.

> **Decisión pendiente del gate humano.** Cerrar la HU #11761 sin cambios es una decisión del PO o del
> Líder Técnico, no de quien escribe este inventario.
