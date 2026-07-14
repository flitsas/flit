# Plan de ajustes RUES — Persona jurídica (representante legal / apoderado)

> Rama: `feature/AB-10583-10586-rues-rnmc-ajustes` · Fecha: 2026-07-10
> Objetivo: cerrar el ciclo de persona jurídica en matrículas y traspasos **sin afectar la ruta de
> persona natural** (hoy 100% funcional). Fuente: `ajustes.md`.
>
> **Decisión de alcance (2026-07-10):** se **descarta** el campo _ciudad del representante legal_
> (ajuste 1b original). El RL solo suma la obligatoriedad del **correo** (campo ya existente). Sin
> campo nuevo, sin cambio de tipo FE ni de forma del `metadata`.

---

## 0. Resumen ejecutivo

La consulta RUES ya funciona (razón social por NIT vía `RuesPersonLookupHandler` + provider `verifik_rues`).
Lo que falta es que el **representante legal / apoderado** deje de ser _solo un objeto de contacto embebido
en `actor.metadata`_ y pase a **cargar la validación de identidad de la persona jurídica**. Hoy:

- El RL se captura y se guarda en `actor.metadata` (JSON), **ningún handler lo lee** salvo `ActorsCommand`.
- La validación biométrica y el correo de validación se anclan **siempre al documento y correo del actor**
  (`comprador`/`vendedor`). Para PJ ese documento es el NIT (no validable biométricamente) → la parte
  jurídica sale **"NO FIRMADO"** en el FUR y su único respaldo es el `certificado_rues`.
- En el FUR el nombre de la PJ se toma de `actor.FullName` (= razón social) pero se **trocea con `SplitName`**
  en casillas de apellidos/nombres pensadas para persona natural.

**Eje del cambio:** introducir un resolvedor único de "sujeto de identidad de la parte" que devuelva los
datos del **actor** cuando es natural y los del **RL/apoderado** cuando es jurídico. Con eso, los ajustes de
Identidad, FUR (validaciones) y Consolidado caen en cascada. Persona natural queda intacta porque la rama
jurídica solo se activa con `PersonType == juridical`.

---

## 1. Diagnóstico verificado (estado actual)

### 1.1 Captura de actores
| Elemento | Ruta | Estado |
|---|---|---|
| Entidad actor | `ProcedureInstanceActor.cs` | `Email` nullable, `PersonType`, `EsRepresentanteLegal`, `Metadata` (jsonb). Sin columnas de RL. |
| RL (backend) | `ActorsCommand.cs:52-57` `ActorRepresentanteLegal` | `TipoDocumento?/NumeroDocumento?/NombreCompleto?/Email?/Telefono?` — **todos opcionales**. |
| RL persistencia | `ActorsCommand.cs:303-311` | Serializado dentro de `actor.metadata`. |
| Validación email actor | `ActorsCommand.cs:123-124` | Email del **actor** obligatorio; **RL sin validación**. |
| RL (frontend) | `ActorsForm.tsx:808-936` `rlSection` | Solo si `isJuridical`. Email/teléfono marcados `(opcional)`. |
| Tipo FE del RL | `procedure-runtime.ts:193-199` | `email?` opcional. |
| Validación FE | `ActorsForm.tsx:145-191` `validateActors` | Solo valida campos del actor; **no valida el RL**. |

### 1.2 Validación de identidad (biométrica)
| Elemento | Ruta | Estado |
|---|---|---|
| Sujeto biométrico (Kyverum) | `KyverumVerifyCommand.cs:95-100` | `nombre/tipoDoc/documento/email = actor.*`. **RL nunca se consulta.** |
| Sujeto biométrico (mock) | `BiometricaCommand.cs` (`SimularBiometria`, `IniciarBiometria`) | Igual, datos del actor. |
| Asegurar identidad | `EnsureIdentityCommand.cs:55-66` | Resuelve por documento del actor; expira validaciones con doc distinto (paso 0). |
| Aprobación por parte | `IdentityApprovalResolver.cs:83-88` `ActorDoc` | Devuelve el documento del **actor**. Único punto a bifurcar. |
| Correo de validación (FE) | `BiometricStep.tsx:378-379` | Muestra/usa `v.email` (correo del actor). Sin tarjeta para RL. |
| Gate de radicación | `SubmitGate.cs:69-92` | Exige identidad de `comprador` (matrícula) / `comprador`+`vendedor` (traspaso). |

### 1.3 FUR
| Elemento | Ruta | Estado |
|---|---|---|
| Nombre de la parte | `FurCommand.cs:406-420` `AddParte` | Usa `actor.FullName` sin distinguir PN/PJ. |
| Mapeo al PDF | `FurFieldMapper.cs` `SplitName` (`:372-383`) | Trocea el nombre en apellido1/apellido2/nombres → **rompe la razón social**. |
| Sello de identidad | `FurCommand.cs:281-321` | Resuelve validación por documento del actor (NIT en PJ → no hay → "NO FIRMADO"). |
| Certificado de identidad | `FurCommand.cs:353-404` | `certificado_identidad` (comprador) / `certificado_identidad_vendedor`. Por documento del actor. |
| Certificado RUES | `FurCommand.cs:328-343` | Se genera para actor NIT (razón social + estado ACTIVA). |

### 1.4 Consolidado
| Elemento | Ruta | Estado |
|---|---|---|
| Fusión | `ConsolidadoCommand.cs` | Fusiona los adjuntos que selecciona el resolver de orden. |
| Orden matrícula | `MatriculaConsolidadoOrdering.cs` | Incluye `certificado_identidad`, `certificado_rues`. |
| Orden traspaso | `TraspasoConsolidadoOrdering.cs` | Incluye `certificado_identidad`, `certificado_identidad_vendedor`, `certificado_rues`. |

> **Consecuencia clave:** el consolidado ya incluye `certificado_identidad(_vendedor)` **por tipo**. Si la
> biométrica del RL produce ese adjunto (vía el flujo FUR existente), **entra al consolidado sin tocar el
> consolidado**. El Ajuste 4 es prácticamente gratis.

---

## 2. Brechas por ajuste solicitado

| # | Ajuste (ajustes.md) | Brecha | Dónde |
|---|---|---|---|
| 1a | Correo del RL obligatorio (PJ) | Hoy opcional | FE + BE validación |
| ~~1b~~ | ~~Ciudad del RL obligatoria (PJ)~~ | **DESCARTADO** (decisión 2026-07-10) | — |
| 2a | Identidad validada por el RL (PJ) | Se valida el NIT del actor | Resolvedor de sujeto de identidad (transversal) |
| 2b | Correo de validación al RL (PJ) | Va al correo del actor | Mismo resolvedor + `BiometricStep.tsx` |
| 3a | Nombre completo de la compañía en el FUR | `SplitName` trocea la razón social | `DocumentParte` + `FurFieldMapper` |
| 3b | Documentos de validación del RL en el FUR | La PJ sale "NO FIRMADO", sin certificado | Cae de 2a (resolver sello/cert por doc del RL) |
| 4 | Adjuntar validación del RL al consolidado | Sin certificado de identidad para PJ | Cae de 2a/3b (el orden ya incluye el tipo) |

---

## 3. Diseño propuesto — pieza central

### `IdentitySubjectResolver` (nuevo helper de Application)

Resuelve el **sujeto de identidad de una parte** a partir del actor:

```
IdentitySubject { Nombre, TipoDocumento, NumeroDocumento, Email }

For(actor):
  si actor.PersonType == "juridical" y tiene RL en metadata con documento →
      devuelve datos del RL (persona natural que valida)
  si no →
      devuelve datos del actor (comportamiento actual, persona natural)
```

- Reutiliza el parseo de `actor.metadata` que ya existe (extender para exponer el `RepresentanteLegal`).
- **Es el único lugar donde se decide "quién valida"**; todos los consumidores lo usan → sin lógica
  jurídica dispersa y sin regresión en persona natural (para PN devuelve exactamente lo de hoy).

Consumidores a migrar a este resolvedor:
1. `KyverumVerifyCommand.cs:95-100` — sujeto + correo de captura.
2. `BiometricaCommand.cs` — `SimularBiometriaHandler`, `IniciarBiometriaHandler`, y el display de `ListBiometria`.
3. `EnsureIdentityCommand.cs:55-66` (y el paso 0 de expiración por documento).
4. `IdentityApprovalResolver.cs:83-88` `ActorDoc` (afecta a `ResolveApprovedPartiesAsync` y `ApprovedPartiesFromKeys`).
5. `FurCommand.cs` `ResolveApprovedValidationAsync` (`:281-304`) y `TryDownloadIdentityCertificateAsync` (`:353-377`).

> Con esto, la validación biométrica de una PJ se crea con el **documento y correo del RL**, y toda la
> cadena (gate, sello del FUR, certificado, consolidado, reuso 30 días por persona) opera sobre el RL de
> forma consistente. `PartyRole` sigue siendo `comprador`/`vendedor` (no se toca el gate ni el enum).

---

## 4. Plan de cambios por fases

### Fase 1 — Correo del RL obligatorio en PJ (Ajuste 1a) · FRONTEND + BACKEND

> Ciudad **descartada**. El campo `Email` del RL ya existe en toda la cadena; solo se agrega su
> obligatoriedad cuando el actor es jurídico. **Sin campo nuevo, sin migración, sin cambio de tipos.**

**Backend — `ActorsCommand.cs`**
1. En `PutActorsHandler.HandleAsync`, dentro del bucle de validación de forma (`:107-128`), añadir tras la
   validación del actor: si `ActorPersonTypes.Normalize(a.PersonType) == "juridical"`, exigir que
   `a.RepresentanteLegal?.Email` no sea vacío y pase `TramiteDocumento.EmailValido` → devolver nuevo error
   `rl_email_requerido`. (Nombre/documento del RL siguen opcionales; solo el correo pasa a obligatorio.)
2. No se toca `SerializeMetadata`/`ParseMetadata`/`NormalizeRepresentanteLegal` — el `Email` ya se persiste.

**Frontend — `ActorsForm.tsx`**
1. `rlSection` (`:907-919`): quitar el `(opcional)` del correo del RL y marcarlo con `*` (obligatorio).
2. `validateActors` (`:145-191`): si `isJuridical(actor)`, validar `representanteLegal?.email` (requerido +
   `EMAIL_RE`). Añadir el error a un nuevo canal de errores del RL (p.ej. `ActorErrors.rlEmail`) y pintarlo
   bajo el campo del RL en `rlSection`.
3. `ProcedureActor`/`RepresentanteLegal` en `procedure-runtime.ts` **no cambian** (email ya es `email?`).

**Tests**
- `ActorsHandlerTests.cs`: PJ sin `RepresentanteLegal.Email` → `rl_email_requerido`; PJ con email válido → OK.
  PN sin RL → sin regresión (no aplica la regla).
- Front (si hay cobertura de `validateActors`): caso jurídica sin email de RL → inválido.

**No afecta PN:** la regla (BE y FE) solo corre cuando `PersonType == "juridical"` / `isJuridical`.

### Fase 2 — Identidad validada por el RL (Ajustes 2a, 2b) · BACKEND + FRONTEND (núcleo)

**Backend**
- Nuevo `IdentitySubjectResolver` (§3) + extensión del parseo de metadata para exponer el RL.
- Migrar los 5 consumidores (§3) a `IdentitySubjectResolver.For(actor)`.
  - `EnsureIdentityCommand`: comparar el "documento del sujeto" (RL en PJ) tanto en el paso 0 (expiración)
    como en `DocCoincide`.
  - `IdentityApprovalResolver.ActorDoc`: devolver documento del sujeto (RL en PJ). Cuidar `ApprovedPartiesFromKeys`
    (listado en lote) para que la clave `IdentidadKey` use el documento del RL.
- Tests: `IniciarKyverumVerifyHandlerTests`, `BiometricaCommand` tests, `EnsureIdentityHandlerTests`,
  `SubmitGateKyverumTests` — nuevos casos PJ (sujeto = RL, correo = RL, gate aprueba por identidad del RL).

**Frontend**
- `BiometricStep.tsx`: para parte jurídica, mostrar el correo destino = correo del RL y el nombre del RL como
  sujeto de validación (el `StartAction` sigue enviando solo `{ parte }`; el backend resuelve el sujeto).

**No afecta PN:** el resolvedor devuelve el actor para `PersonType != juridical`.

### Fase 3 — FUR: nombre de compañía + validaciones del RL (Ajustes 3a, 3b) · BACKEND

- `IFurDocumentGenerator.cs`: añadir a `DocumentParte` un flag `EsJuridica` (y opcionalmente `RazonSocial`
  explícita) para que el mapper no trocee.
- `FurCommand.AddParte`: setear `EsJuridica` desde `actor.PersonType`/`DocumentType == NIT`.
- `FurFieldMapper.cs`: cuando `EsJuridica`, escribir la razón social completa en el campo "nombre" sin
  `SplitName` (dejar apellidos vacíos o mapear a la casilla de nombre única, según manifest del FUR).
- Ajuste 3b **cae de Fase 2**: al validar la identidad con el documento del RL, `ResolveApprovedValidationAsync`
  y `TryDownloadIdentityCertificateAsync` (ya migrados al resolvedor) estampan el sello y descargan
  `certificado_identidad(_vendedor)` del RL. Verificar que `FirmaFurStep.tsx:1264` los liste (ya incluye
  `certificado_identidad`/`_vendedor`).
- Tests: `FurHandlerTests` — PJ con RL validado → FUR con razón social en "nombre", sello del RL y
  `certificado_identidad`; PJ sin validar → "NO FIRMADO" + `certificado_rues` (como hoy).

### Fase 4 — Consolidado (Ajuste 4) · VERIFICACIÓN

- **Sin cambios de código esperados**: `certificado_identidad(_vendedor)` ya está en el orden de matrícula y
  traspaso. Basta con que Fase 2/3 los generen para la PJ.
- Test de regresión en `ConsolidadoHandlerTests`: fixture PJ con `certificado_identidad` del RL → aparece en
  `paginas_incluidas`.

---

## 5. Estrategia para NO romper persona natural

1. **Toda** la lógica jurídica se activa con `PersonType == "juridical"` (o RL con documento presente); el
   resolvedor de sujeto devuelve el actor tal cual para PN.
2. Sin migraciones ni cambios de esquema (el RL sigue en `metadata`).
3. `PartyRole` (`comprador`/`vendedor`), `SubmitGate` y el enum `ParteRol` **no cambian**.
4. Cobertura de regresión: correr la suite existente de PN (biométrica, FUR, consolidado) sin cambios de
   comportamiento; los nuevos tests cubren solo PJ.
5. Feature flag opcional (si se quiere despliegue gradual): gate de la validación por RL detrás de una
   opción, con fallback al comportamiento actual.

---

## 6. Decisiones abiertas (requieren confirmación de negocio)

1. **Cédula manual de la PJ**: hoy `nit_sin_cedula` (`ConditionalDocumentRules.cs:43`) oculta la cédula para
   NIT porque el RUES cubre la identificación. Con el RL validando biométricamente, ¿se mantiene oculta la
   cédula manual (respaldo = certificado biométrico del RL) o se exige? Propuesta: mantener oculta.
2. **Reuso de identidad 30 días**: al anclar la identidad al documento del RL, un mismo RL que valida en otro
   trámite del tenant se **reutiliza** (comportamiento HU #10350). Confirmar que es deseable para PJ.
3. **Correo del actor vs correo del RL**: se mantiene el correo del actor (contacto, "está ok" según ajuste)
   y se añade el del RL (validación). Confirmar que ambos coexisten.
4. **`certificado_rues` + `certificado_identidad` juntos**: la PJ tendrá ambos en el consolidado. Confirmar
   que es el resultado esperado (identificación de la empresa + identidad del RL).

---

## 7. Trazabilidad ADO (sugerido)

Estos ajustes encajan en la 2ª ola (Features 10583-10586, RUES/RNMC). Descomposición sugerida en HUs:

- **HU (FRONTEND+BACKEND)** — Correo del RL obligatorio en persona jurídica (Fase 1).
- **HU (BACKEND)** — Identidad de PJ validada por el RL: sujeto + correo (Fase 2, núcleo).
- **HU (FRONTEND)** — Paso de validación biométrica muestra RL como sujeto (Fase 2, FE).
- **HU (BACKEND)** — FUR: razón social en "nombre" + validaciones del RL (Fase 3).
- **HU (QA)** — Regresión consolidado PJ + no-regresión persona natural (Fase 4).

> Gate de proceso: activar cada HU (`Active`) requiere confirmación humana explícita antes de implementar
> (CLAUDE.md §2). Este documento es solo el plan; no inicia implementación.
