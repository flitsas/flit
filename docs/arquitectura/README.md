# Arquitectura de FLIT 2.0 como código

Modelos [LikeC4](https://likec4.dev) de FLIT 2.0 y del landscape de la empresa. Son la fuente de los diagramas que
se publican en Flit Atlas. **El repo es público:** lo que se escribe aquí queda a la vista de todos.

| Carpeta | Proyecto LikeC4 | Qué tiene |
|---|---|---|
| `arquitectura/landscape/` | `landscape` | Especificación común (la fuente), actores, sistemas de FLIT, externos y la vista `landscape` |
| `docs/arquitectura/` | `flit` | Contenedores y componentes de FLIT 2.0, sus vistas y sus flujos. Incluye el landscape con `include.paths` y extiende `flit2` |

FLITO se modela en su repo, `flitsas/flito`, en `docs/arquitectura/`. Lleva una copia idéntica de
`arquitectura/landscape/specification.c4`, y su workflow falla si las dos se separan. Si cambias la especificación aquí,
abre también el cambio allá.

## Vistas

| Vista | Tipo | Archivo |
|---|---|---|
| `landscape` y su detalle `landscape_usuarios`, `landscape_satelites`, `landscape_fuentes`, `landscape_entidades`, `landscape_terceros` | Estática | `arquitectura/landscape/views.c4` |
| `flit2_contexto`, `flit2_contenedores` y su detalle `flit2_aplicaciones`, `flit2_negocio`, `flit2_negocio_salidas`, `flit2_plataforma`, `eventos_rabbitmq` | Estática | `views.c4` |
| `flit2_core_api_componentes` y su detalle `flit2_core_api_entrada`, `flit2_core_api_dominio`, `flit2_core_api_transversal`, `flit2_core_api_ia`; `flit2_gateway_componentes` | Componentes | `componentes/` |
| `flujo_radicacion_tramite`, `flujo_login_oidc_suite`, `flujo_login_marca_blanca`, `flujo_ocr_ia`, `flujo_ocr_comparendos`, `flujo_consulta_runt` | Dinámica | `dinamicas/` |

Las vistas de despliegue por ambiente no se escriben a mano ni viven en este repo: las genera Atlas desde gitops.

## Cómo editar

1. Instala la extensión de LikeC4 en tu editor si quieres autocompletado; no es obligatoria.
2. Edita los `.c4`. Un elemento nuevo va con su `metadata`; una vista nueva va en el archivo de su tipo.
3. Valida y previsualiza:

```bash
npx --yes likec4@1.59.4 validate arquitectura/landscape
npx --yes likec4@1.59.4 validate docs/arquitectura
npx --yes likec4@1.59.4 start docs/arquitectura     # abre las vistas en el navegador

npx --yes likec4@1.59.4 export json --project flit -o /tmp/flit.json docs/arquitectura
node docs/arquitectura/tools/validar-metadata.mjs /tmp/flit.json arquitectura/landscape docs/arquitectura
```

El último comando se corre desde la raíz del repo. El workflow `.github/workflows/arquitectura.yml` hace lo mismo en
cada PR que toque estas carpetas o `contracts/`.

La versión de LikeC4 está fijada en `1.59.4`. Para subirla, cámbiala en el workflow y en este README, en la misma PR, con
una versión de al menos dos semanas de publicada.

## Reglas del modelo

- **Nada de** IP, puertos internos de administración, hosts internos, secretos, nombres de usuario de base de datos ni
  rutas de secretos. Tampoco nombres de personas: `owner` lleva un rol o un equipo. El validador busca lo que tiene
  forma de IP, token o correo, pero no lo reemplaza: revisa tu diff.
- **Modela lo que está en el código.** Lo que no puedas comprobar lleva la marca `[por verificar]` en el texto o en el
  metadato.
- **Ids en `snake_case`.** El de un contenedor es su workload con `-` cambiado por `_`: `core-api` es `core_api`.
- **Relaciones:** verbo u objeto en español, de 60 caracteres como máximo. `-[grpc]->` para gRPC y `-[async]->` para el
  bus y los webhooks.

### Metadatos obligatorios

| Clave | Dónde | Valor |
|---|---|---|
| `owner` | Sistema y contenedor | Rol o equipo responsable |
| `repo` | Sistema | `org/repo` |
| `repo_path` | Contenedor | Ruta en el repo; debe existir |
| `criticidad` | Sistema y contenedor | `critica`, `alta`, `media` o `baja` |
| `datos_personales` | Sistema y contenedor | `si` o `no`; `si` exige el tag `#pii`, y al revés |
| `k8s_workload` | Aplicación y servicio | Nombre exacto del Deployment; es la llave del drift |
| `ambientes` | Opcional | Dónde corre, si no es en todos (`dev`) |
| `openapi*`, `asyncapi*`, `proto*` | Si tiene contrato | Ruta en `contracts/`; debe existir |
| `aplicativo` | Si tiene slug | Slug del catálogo de Atlas |
| `adr` | Opcional | `ADR-NNNN` separados por coma; deben existir en el repo |

### Tags

`#pii` datos personales · `#critico` su caída detiene a los clientes · `#legado` FLIT 1 o código a retirar ·
`#objetivo` diseñado, aún no en producción · `#vigente` camino actual que será reemplazado · `#temporal` existe por una
migración · `#ia` usa un LLM · `#externo_publico` expuesto a terceros · `#suite` parte de la FLIT Suite ·
`#referencia` elemento de otro repo, redeclarado para poder relacionarlo.

## Publicación en Atlas

El job `publicar` de `arquitectura.yml` llama al workflow reutilizable `atlas-publicar-arquitectura.yml@v1` de `flitsas/flit-atlas`, una
vez por proyecto, en push a `develop`, `staging` o `release`, solo si están `ATLAS_URL` y `ATLAS_PUBLICAR_ARQUITECTURA=true`. Hoy no publica: la
segunda variable no existe. `metadata.aplicativo` lleva el slug del elemento en el catálogo de Atlas; si no hay slug, no se inventa.

## Que se lea sin zoom

Atlas muestra cada vista en un marco de 928 px de ancho. Una vista de resumen lleva hasta unos 15 nodos, agrupados con el tipo `grupo`, y
`navigateTo` hacia su detalle. Los elementos usan `size sm` y `textSize xl` desde la especificación. `likec4 validate` no avisa de una vista
ilegible: dibújala con `likec4 export png`, encógela a 928 px y mírala antes de subirla.
