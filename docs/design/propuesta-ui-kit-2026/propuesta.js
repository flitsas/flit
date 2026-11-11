// Propuesta navegable del Diseño Inteligente para FLIT 2.0: todos los módulos, vista de Super Admin.
// Datos de muestra (ficticios) en datos.js. Todo lo que parece botón hace algo.
(function () {
  "use strict";

  var D = window.FLIT;
  var ILUS = "assets/ilustraciones/";
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var esc = function (t) { return String(t).replace(/[&<>"]/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]; }); };
  // UI Kit 2026: cada ilustración de la propuesta anterior se resuelve a un ícono del repositorio de íconos de FLIT.
  var KIT = "assets/kit/", KI = window.KIT_ICONOS || {};
  var MAPA_ICONO = { auditoria: "historial", ayuda: "ayuda", banners: "megafono", biometria: "rostro", calendario: "calendario", causales: "causal", colaboradores: "usuarios",
    comparendos: "ticket", configuracion: "ajustes", "consulta-placa": "buscar", contrasena: "candado", diagnostico: "medidor", documentos: "documento", empresas: "empresas",
    filtros: "filtro", firmas: "firma", "fuentes-oficiales": "entidad", fur: "fur", "generacion-documental": "pila", "historial-placa": "reloj-doc", identidad: "identidad",
    improntas: "impronta", inicio: "inicio", integraciones: "enchufe", mandatos: "mandato", matricula: "@tramite-matricula", notificaciones: "campana", organismos: "banco",
    pendientes: "espera", perfil: "persona", preasignacion: "credencial", procesos: "proceso", productos: "rejilla", reportes: "reportes", revocatorias: "deshacer", roles: "rol",
    seguridad: "escudo", tramites: "tramites", traspaso: "@tramite-traspaso", trazabilidad: "ruta", vencidos: "vencido" };
  var kico = function (nombre, cls) { return '<span class="kico ' + (cls || "") + '" aria-hidden="true">' + (KI[nombre] || KI.documento || "") + "</span>"; };
  var ilus = function (n, cls) {
    var m = MAPA_ICONO[n] || "documento";
    if (m.charAt(0) === "@") return '<img class="kimg ' + (cls || "") + '" src="' + KIT + m.slice(1) + '.svg" alt="" aria-hidden="true" width="48" height="48">';
    return kico(m, cls);
  };
  var almacen = { getItem: function (k) { try { return window.localStorage.getItem(k); } catch (e) { return null; } }, setItem: function (k, v) { try { window.localStorage.setItem(k, v); } catch (e) {} }, removeItem: function (k) { try { window.localStorage.removeItem(k); } catch (e) {} } };
  var CARGADOR = '<span class="cargador" aria-hidden="true"><img class="cargador__pista" src="' + KIT + 'carga-pista.svg" alt=""><span class="cargador__linea"><img src="' + KIT + 'carga-linea.svg" alt=""></span><span class="cargador__carro"><img src="' + KIT + 'carga-carro.svg" alt=""></span></span>';
  var placaTxt = function (p) { return p.slice(0, 3) + " " + p.slice(3); };
  var pesos = function (n) { return "$ " + Number(n).toLocaleString("es-CO"); };
  var norm = function (t) { return String(t).toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/\s/g, ""); };
  var ET = D.ESTADOS_TRAMITE;
  var cuantos = function (id) { return D.TRAMITES.filter(function (t) { return t.estado === id; }).length; };
  // UI Kit 2026, «Uso de gráficos»: barras, dona y leyenda con la paleta del manual.
  var COLOR_ESTADO = { borrador: "#91a5b5", validacion: "#557eff", firma: "#ff4e00", radicado: "#00dbd5", finalizado: "#8cc63f", rechazado: "#c10000" };
  function dona(partes, etiqueta) {
    var total = partes.reduce(function (a, x) { return a + x.v; }, 0), C = 2 * Math.PI * 42, ido = 0;
    if (!total) return '<p class="grafico-vacio">Sin trámites en el periodo</p>';
    return '<div class="dona"><svg viewBox="0 0 120 120" role="img" aria-label="' + esc(etiqueta) + ": " + partes.filter(function (x) { return x.v; }).map(function (x) { return x.t + " " + x.v; }).join(", ") + '">' +
      partes.filter(function (x) { return x.v; }).map(function (x) {
        var largo = C * x.v / total, arco = '<circle cx="60" cy="60" r="42" fill="none" stroke="' + x.c + '" stroke-width="18" stroke-dasharray="' + largo.toFixed(2) + " " + (C - largo).toFixed(2) + '" stroke-dashoffset="' + (-ido).toFixed(2) + '" transform="rotate(-90 60 60)"/>';
        ido += largo; return arco;
      }).join("") + '<text x="60" y="59" text-anchor="middle" class="dona__total">' + total + '</text><text x="60" y="73" text-anchor="middle" class="dona__pie">trámites</text></svg>' +
      '<ul class="leyenda leyenda--lista">' + partes.filter(function (x) { return x.v; }).map(function (x) { return '<li><i style="background:' + x.c + '"></i>' + x.t + "<strong>" + x.v + "</strong></li>"; }).join("") + "</ul></div>";
  }
  function barrasGrupo(etiquetas, series, titulo) {
    var max = Math.max.apply(null, series.reduce(function (a, x) { return a.concat(x.v); }, [1])), paso = max <= 10 ? 2 : max <= 25 ? 5 : 10, tope = Math.ceil(max / paso) * paso;
    var W = 560, H = 250, x0 = 34, y0 = 214, alto = 190, ancho = W - x0 - 8, grupo = ancho / etiquetas.length, bw = Math.min(46, (grupo - 24) / series.length), svg = "";
    for (var t = 0; t <= tope; t += paso) { var y = y0 - alto * t / tope; svg += '<line x1="' + x0 + '" x2="' + W + '" y1="' + y + '" y2="' + y + '" class="grafico__guia"/><text x="' + (x0 - 8) + '" y="' + (y + 4) + '" text-anchor="end" class="grafico__eje">' + t + "</text>"; }
    etiquetas.forEach(function (e, i) {
      var ini = x0 + grupo * i + (grupo - bw * series.length - 3 * (series.length - 1)) / 2;
      series.forEach(function (sr, j) { var h = alto * sr.v[i] / tope; svg += '<rect x="' + (ini + j * (bw + 3)).toFixed(1) + '" y="' + (y0 - h).toFixed(1) + '" width="' + bw.toFixed(1) + '" height="' + h.toFixed(1) + '" rx="3" fill="' + sr.c + '"><title>' + sr.t + ", " + e + ": " + sr.v[i] + "</title></rect>"; });
      svg += '<text x="' + (x0 + grupo * i + grupo / 2) + '" y="' + (y0 + 18) + '" text-anchor="middle" class="grafico__eje">' + e + "</text>";
    });
    return '<div class="grafico"><svg viewBox="0 0 ' + W + " " + H + '" role="img" aria-label="' + esc(titulo) + '">' + svg + '</svg><ul class="leyenda">' + series.map(function (sr) { return '<li><i style="background:' + sr.c + '"></i>' + sr.t + "</li>"; }).join("") + "</ul></div>";
  }
  var MESES = ["Ago", "Sep", "Oct", "Nov"];
  var SERIES_OPERACION = [{ t: "Matrículas", c: "#557eff", v: [12, 18, 15, 9] }, { t: "Traspasos", c: "#00dbd5", v: [8, 12, 17, 11] }, { t: "Otros", c: "#f9ac00", v: [4, 3, 6, 2] }];
  function porEstado(filtro) { return Object.keys(ET).map(function (k) { return { t: ET[k][0], c: COLOR_ESTADO[k] || "#91a5b5", v: D.TRAMITES.filter(function (t) { return t.estado === k && (!filtro || filtro(t)); }).length }; }); }
  // Banner del tablero (manual, «Banner»): carrusel con indicadores de posición y controles; cada contenido trae su fondo.
  var BANNER_I = 0;
  var BANNERS_INICIO = [
    ["Hola, Laura", "Tienes " + D.TRAMITES.length + " trámites en este periodo. Tu operación está al día y sin identidades pendientes de atención.", "reportes", "degradado"],
    ["Novedades de la plataforma", "Publicamos mejoras en la validación de identidad, los reportes ejecutivos y la trazabilidad de firmas digitales.", "escudo", "azul"],
    ["Placas preasignadas", "Desde el listado ya puedes ver qué trámites tienen placa preasignada y cuáles siguen a la espera.", "credencial", "tinta"]
  ];
  function bannerInicio() {
    var n = BANNERS_INICIO.length;
    return '<section class="banner-kit" aria-roledescription="carrusel" aria-label="Novedades">' + BANNERS_INICIO.map(function (b, i) {
      return '<div class="banner-kit__lamina banner-kit__lamina--' + b[3] + '"' + (i === BANNER_I ? "" : " hidden") + '><h1><span class="banner-kit__chip" aria-hidden="true">' + (KI[b[2]] || "") + "</span>" + b[0] + "</h1><p>" + b[1] + "</p></div>";
    }).join("") + '<div class="banner-kit__pie">' + BANNERS_INICIO.map(function (b, i) { return '<button class="banner-kit__punto" type="button" data-banner="' + i + '" aria-label="Contenido ' + (i + 1) + " de " + n + '"' + (i === BANNER_I ? ' aria-current="true"' : "") + "></button>"; }).join("") +
      '<span class="banner-kit__esp"></span><button class="banner-kit__flecha" type="button" data-banner="' + ((BANNER_I + n - 1) % n) + '" aria-label="Contenido anterior">' + KI.izquierda + '</button><button class="banner-kit__flecha" type="button" data-banner="' + ((BANNER_I + 1) % n) + '" aria-label="Contenido siguiente">' + KI.derecha + "</button></div></section>";
  }
  document.addEventListener("click", function (ev) {
    var b = ev.target.closest("[data-banner]"); if (!b) return;
    BANNER_I = Number(b.getAttribute("data-banner")); var viejo = b.closest(".banner-kit"), caja = document.createElement("div");
    caja.innerHTML = bannerInicio(); viejo.replaceWith(caja.firstChild); var p = document.querySelector('.banner-kit__punto[aria-current="true"]'); if (p) p.focus();
  });
  var ICONO_ESTADO = { borrador: "borrador", validacion: "preparado", firma: "pendiente", radicado: "entregado", finalizado: "aprobado", rechazado: "rechazado", preparado: "preparado",
    preasignacion: "sin-placa", asignado: "placa", entregado: "entregado", subsanacion: "subsanado", aprobado: "aprobado", revocado: "revocado", anulado: "anulado" };
  var ICONO_TONO = { ok: "aviso-ok", warn: "estado-pendiente", danger: "estado-rechazado", info: "aviso-info", "": "estado-borrador" };
  var iconoEstado = function (k, tono) { return KIT + (ICONO_ESTADO[k] ? "estado-" + ICONO_ESTADO[k] : ICONO_TONO[tono || ""] || "estado-borrador") + ".svg"; };
  var imgEstado = function (k, tono) { return '<img class="estado-ico" src="' + iconoEstado(k, tono) + '" alt="" aria-hidden="true" width="20" height="20">'; };
  var badge = function (estados, k) { var e = estados[k] || [k, ""]; return '<span class="badge badge--icono ' + e[1] + '" data-estado="' + esc(k) + '">' + imgEstado(k, e[1]) + e[0] + "</span>"; };

  // ---------- Modo real: la propuesta servida por servidor-local.mjs, con la API de FLIT en solo lectura ----------
  var MODO = "muestra", SESION = null, AMBIENTE = "";
  var CONECTADAS = { inicio: 1, tramites: 1, tramite: 1, ingreso: 1, marca: 1, ayuda: 1, perfil: 1 };
  var ESTADOS_REALES = {
    borrador: ["Borrador", ""], preparado: ["Listo para radicar", "info"], preasignacion: ["Preasignación", "info"], asignado: ["Placa asignada", "info"],
    entregado: ["Entregado al organismo", "info"], subsanacion: ["En subsanación", "warn"], aprobado: ["Aprobado", "ok"], rechazado: ["Rechazado", "danger"],
    revocado: ["Revocado", "danger"], anulado: ["Anulado", ""]
  };
  var CACHE_TRAMITES = {};
  function api(ruta) {
    return fetch(ruta, { credentials: "same-origin", headers: { Accept: "application/json" } }).then(function (r) {
      if (r.status === 401) { SESION = null; irA("#ingreso"); throw new Error("sesion"); }
      if (!r.ok) throw new Error("http " + r.status);
      return r.json();
    });
  }
  function fecha(iso) {
    if (!iso) return "";
    var d = new Date(iso), hoy = new Date(), ayer = new Date(Date.now() - 864e5);
    var hora = d.toLocaleTimeString("es-CO", { hour: "2-digit", minute: "2-digit", timeZone: "America/Bogota" });
    var dia = function (x) { return x.toLocaleDateString("es-CO", { timeZone: "America/Bogota" }); };
    if (dia(d) === dia(hoy)) return "Hoy, " + hora;
    if (dia(d) === dia(ayer)) return "Ayer, " + hora;
    return d.toLocaleDateString("es-CO", { day: "numeric", month: "short", year: d.getFullYear() === hoy.getFullYear() ? undefined : "numeric", timeZone: "America/Bogota" }).replace(".", "");
  }
  function errorCarga(msg) {
    return '<div class="vacio" role="alert">' + ilus("vencidos") + "<p><strong>No pudimos traer los datos de FLIT.</strong></p><p>" + esc(msg) + ' Revisa tu conexión y vuelve a intentar; si sigue, escribe a soporte@flitsas.com.</p><button class="btn secondary" type="button" data-reintentar>Reintentar</button></div>';
  }
  function cargandoHtml(texto) { return '<div class="vacio cargando" role="status">' + CARGADOR + '<p>' + texto + "</p></div>"; }
  var EN_CURSO = ["borrador", "preparado", "preasignacion", "asignado", "entregado", "subsanacion"];

  function vInicioReal() {
    var nombre = (SESION && SESION.nombre ? SESION.nombre.split(" ")[0] : "");
    setTimeout(function () {
      Promise.all([api("/api/v1/tramites/instances/estado-counts"), api("/api/v1/tramites/instances?take=6&skip=0")]).then(function (r) {
        var c = (r[0] && r[0].counts) || {}, items = (r[1] && r[1].items) || [];
        items.forEach(function (t) { CACHE_TRAMITES[t.id] = t; });
        var enCurso = EN_CURSO.reduce(function (a, k) { return a + (c[k] || 0); }, 0);
        var m = [["Trámites en curso", enCurso, "tramites", "#tramites", "Ver todos"], ["En subsanación", c.subsanacion || 0, "pendientes", "#tramites?vista=subsanacion", "Corregirlos", true],
          ["Entregados al organismo", c.entregado || 0, "organismos", "#tramites?vista=entregado", "Ver entregados"], ["Aprobados", c.aprobado || 0, "trazabilidad", "#tramites?vista=aprobado", "Ver aprobados"]];
        var cif = $("#cifras-reales"); if (!cif) return;
        cif.innerHTML = m.map(function (x) {
          return '<a class="metrica' + (x[5] && x[1] ? " metrica--pendiente" : "") + '" href="' + x[3] + '"><span class="metrica__etiqueta">' + x[0] + '</span><span class="metrica__cifra">' + x[1].toLocaleString("es-CO") + "</span>" + ilus(x[2], "metrica__ilus") + '<span class="metrica__pie">' + x[4] + "</span></a>";
        }).join("");
        $("#actividad-real").innerHTML = items.length ? '<ul class="actividad">' + items.map(function (t) {
          return '<li><a class="placa" href="#tramite?id=' + t.id + '">' + (t.placa ? placaTxt(t.placa) : "SIN PLACA") + "</a><span>" + esc(t.tipoNombre || t.modalidad || "Trámite") + (t.compradorNombre ? " para " + esc(t.compradorNombre) : "") + "</span>" + badge(ESTADOS_REALES, t.estado) + "<time>" + fecha(t.updatedAt || t.createdAt) + "</time></li>";
        }).join("") + "</ul>" : '<p class="vacio">Todavía no hay trámites en tu compañía.</p>';
        actualizarCampana(c.subsanacion || 0);
        var lt = $("#pend-tramites"); if (lt && c.subsanacion) { lt.hidden = false; lt.textContent = c.subsanacion + (c.subsanacion === 1 ? " pendiente" : " pendientes"); }
      }).catch(function (e) { if (e.message !== "sesion") { var z = $("#cifras-reales"); if (z) z.outerHTML = errorCarga("La consulta respondió con error."); } });
    });
    return cabecera({ il: "inicio", ante: "Resumen de la operación", titulo: "Hola" + (nombre ? ", " + esc(nombre) : ""), texto: "Tus datos reales de FLIT " + AMBIENTE + ". Esta propuesta solo consulta: no cambia nada en la plataforma." }) +
      '<section class="metricas" id="cifras-reales" aria-label="Cifras de la operación" aria-busy="true">' + [1, 2, 3, 4].map(function () { return '<div class="metrica metrica--esqueleto" aria-hidden="true"></div>'; }).join("") + "</section>" +
      '<div class="rejilla rejilla--2"><section class="card"><div class="card__cabeza"><div><h2>Accesos rápidos</h2><p>Lo que más se hace en el día.</p></div></div><div class="accesos">' +
      '<a class="acceso" href="#tramites">' + ilus("tramites") + "<span><strong>Trámites</strong><span>Todos los de tu compañía</span></span></a>" +
      '<a class="acceso" href="#tramites?vista=subsanacion">' + ilus("pendientes") + "<span><strong>En subsanación</strong><span>Los que el organismo devolvió</span></span></a>" +
      '<a class="acceso" href="#tramites?vista=preparado">' + ilus("firmas") + "<span><strong>Listos para radicar</strong><span>Completos, sin enviar</span></span></a>" +
      '<button class="acceso" type="button" data-abrir="buscador">' + ilus("consulta-placa") + "<span><strong>Buscar</strong><span>Una placa o un módulo</span></span></button></div></section>" +
      '<section class="card"><div class="card__cabeza"><div><h2>Actividad reciente</h2><p>Los últimos trámites que se movieron.</p></div><a class="btn sm secondary" href="#tramites">Ver trámites</a></div><div id="actividad-real">' + cargandoHtml("Trayendo la actividad") + "</div></section></div>" +
      '<section class="card"><div class="card__cabeza"><div><h2>Módulos</h2><p>Los que dicen "Datos de muestra" todavía no están conectados a tus datos.</p></div><button class="btn sm secondary" type="button" data-abrir="buscador">Buscar un módulo</button></div><div class="acordeones">' + mapaModulos() + "</div></section>";
  }

  // Filtros con datos reales: los mismos parámetros que usa el frontend de FLIT en GET /tramites/instances.
  var DEFS_REALES = [
    { id: "rango", tipo: "rango" },
    { id: "comprador", t: "Comprador", tipo: "texto", ayuda: "Nombre o documento" },
    { id: "vendedor", t: "Vendedor", tipo: "texto", ayuda: "Nombre o documento" },
    { id: "gestor", t: "Gestor", tipo: "texto", ayuda: "Nombre" },
    { id: "organismo", t: "Organismo de tránsito", tipo: "texto", ayuda: "Por ejemplo, Medellín" },
    { id: "prioritario", tipo: "si", texto: "Solo prioritarios" },
    { id: "firma", tipo: "si", texto: "Solo con firma pendiente" }
  ];
  function filtrosApi(q) {
    var p = [], e = encodeURIComponent;
    var dia = function (iso, fin) { var x = iso.split("-").map(Number); return new Date(x[0], x[1] - 1, x[2], fin ? 23 : 0, fin ? 59 : 0, fin ? 59 : 0, fin ? 999 : 0).toISOString(); };
    var campo = q.fechade === "actualizado" ? "updated" : "created";
    if (q.q) p.push("busqueda=" + e(q.q));
    if (q.desde) p.push(campo + "From=" + e(dia(q.desde)));
    if (q.hasta) p.push(campo + "To=" + e(dia(q.hasta, true)));
    if (q.comprador) p.push("comprador=" + e(q.comprador));
    if (q.vendedor) p.push("vendedor=" + e(q.vendedor));
    if (q.gestor) p.push("gestor=" + e(q.gestor));
    if (q.organismo) p.push("organismoTransito=" + e(q.organismo));
    if (q.prioritario) p.push("prioritario=true");
    if (q.firma) p.push("firmado=false");
    return p.length ? "&" + p.join("&") : "";
  }
  function vTramitesReal(q) {
    var vista = ESTADOS_REALES[q.vista] ? q.vista : "", texto = q.q || "";
    setTimeout(function () {
      var filtro = filtrosApi(q);
      Promise.all([api("/api/v1/tramites/instances/estado-counts" + (filtro ? "?" + filtro.slice(1) : "")), api("/api/v1/tramites/instances?take=" + POR_PAGINA + "&skip=" + ((Math.max(parseInt(q.pag, 10) || 1, 1) - 1) * POR_PAGINA) + (vista ? "&estado=" + vista : "") + filtro)]).then(function (r) {
        var c = (r[0] && r[0].counts) || {}, items = (r[1] && r[1].items) || [], total = r[1] && r[1].total;
        items.forEach(function (t) { CACHE_TRAMITES[t.id] = t; });
        actualizarCampana(c.subsanacion || 0);
        var todos = Object.keys(c).reduce(function (a, k) { return a + c[k]; }, 0);
        var chips = '<button class="chip" type="button" data-vista="" aria-pressed="' + (!vista) + '">' + kico("carpeta", "estado-ico") + '<span>Todos</span><strong>' + todos.toLocaleString("es-CO") + "</strong></button>" +
          Object.keys(ESTADOS_REALES).filter(function (k) { return c[k]; }).map(function (k) {
            return '<button class="chip' + (k === "subsanacion" ? " chip--pendiente" : "") + '" type="button" data-vista="' + k + '" aria-pressed="' + (vista === k) + '">' + imgEstado(k, ESTADOS_REALES[k][1]) + "<span>" + ESTADOS_REALES[k][0] + "</span><strong>" + c[k].toLocaleString("es-CO") + "</strong></button>";
          }).join("");
        var nSub = c.subsanacion || items.length;
        var barra = vista === "subsanacion" && items.length ? '<div class="barra-accion"><p><strong>' + nSub + (nSub === 1 ? " trámite</strong> espera corrección." : " trámites</strong> esperan corrección.") + (nSub === 1 ? " Ábrelo" : " Ábrelos") + " para ver qué pidió el organismo.</p><a class=\"btn sm\" href=\"#tramite?id=" + items[0].id + "\">Abrir el primero</a></div>" : "";
        var filas = items.map(function (t) {
          var av = t.totalPasos ? Math.round((t.pasoActual / t.totalPasos) * 100) : 0;
          return '<tr><td data-etiqueta="' + esc(t.tipoNombre || t.modalidad || "Trámite") + '">' + (t.placa ? '<span class="placa">' + placaTxt(t.placa) + "</span>" : '<span class="badge">Sin placa</span>') + "</td>" +
            '<td class="solo-escritorio" data-etiqueta="Tipo">' + esc(t.tipoNombre || t.modalidad || "") + "</td>" +
            '<td data-etiqueta="Partes"><span class="personas"><span>' + esc(t.vendedorNombre || t.companiaNombre || "Sin vendedor") + "</span><small>" + (t.compradorNombre ? "a " + esc(t.compradorNombre) : "Sin comprador") + "</small></span></td>" +
            '<td data-etiqueta="Estado">' + badge(ESTADOS_REALES, t.estado) + "</td>" +
            '<td data-etiqueta="Avance"><span class="avance"><span class="avance__barra"><span style="width:' + av + '%"></span></span><small>' + av + " %</small></span></td>" +
            '<td data-etiqueta="Actualizado">' + fecha(t.updatedAt || t.createdAt) + '</td><td class="fila-abrir"><a class="btn sm ghost" href="#tramite?id=' + t.id + '" aria-label="Abrir el trámite ' + esc(t.referenceNumber || t.placa || "") + '">Abrir</a></td></tr>';
        }).join("");
        var z = $("#tramites-reales"); if (!z) return;
        z.innerHTML = '<div class="embudo" role="group" aria-label="Filtrar por estado">' + chips + "</div>" + barra +
          (items.length ? '<div class="tabla-wrap"><table><caption class="sr-only">Trámites</caption><thead><tr><th scope="col">Placa</th><th scope="col">Tipo</th><th scope="col">Vendedor y comprador</th><th scope="col">Estado</th><th scope="col">Avance</th><th scope="col">Actualizado</th><th scope="col"><span class="sr-only">Acción</span></th></tr></thead><tbody>' + filas + "</tbody></table></div>" +
            paginador(Math.max(parseInt(q.pag, 10) || 1, 1), total || items.length)
            : '<div class="vacio">' + ilus("consulta-placa") + "<p><strong>No hay trámites con ese filtro.</strong></p><p>Prueba con otra placa o quita el filtro de estado.</p><button class=\"btn secondary\" type=\"button\" data-limpiar>Quitar filtros</button></div>");
        z.removeAttribute("aria-busy");
        enlazarFiltros();
      }).catch(function (e) { if (e.message !== "sesion") { var z = $("#tramites-reales"); if (z) z.innerHTML = errorCarga("La consulta de trámites respondió con error."); } });
    });
    return cabecera({ il: "tramites", ante: "Gestión integral", titulo: "Trámites", texto: "Los trámites reales de tu compañía en FLIT " + AMBIENTE + "." }) +
      '<section class="card"><div class="card__cabeza"><div><h2>Estado de los trámites</h2><p>Pulsa un estado para ver solo esos trámites.</p></div>' +
      '</div>' + panelFiltros(DEFS_REALES, q, "Buscar por placa, VIN o persona") +
      '<div id="tramites-reales" aria-busy="true">' + cargandoHtml("Trayendo tus trámites") + "</div></section>";
  }

  function vTramiteReal(q) {
    var t = CACHE_TRAMITES[q.id];
    if (!t) {
      setTimeout(function () {
        api("/api/v1/tramites/instances?take=50&skip=0").then(function (r) {
          ((r && r.items) || []).forEach(function (x) { CACHE_TRAMITES[x.id] = x; });
          if (CACHE_TRAMITES[q.id]) pintar();
          else { var z = $(".vista .card"); if (z) z.innerHTML = '<div class="vacio">' + ilus("consulta-placa") + '<p><strong>No encontramos ese trámite entre los recientes.</strong></p><p>Búscalo por placa en Trámites.</p><a class="btn secondary" href="#tramites">Ir a Trámites</a></div>'; }
        }).catch(function () {});
      });
      return cabecera({ il: "traspaso", titulo: "Trámite", volver: ["tramites", "Trámites"] }) + '<section class="card">' + cargandoHtml("Buscando el trámite") + "</section>";
    }
    var av = t.totalPasos ? Math.round((t.pasoActual / t.totalPasos) * 100) : 0;
    var dl = [["Referencia", t.referenceNumber], ["Placa", t.placa ? '<span class="placa">' + placaTxt(t.placa) + "</span>" : "Sin placa"], ["VIN", t.vin], ["Vehículo", [t.vehiculoMarca, t.vehiculoLinea].filter(Boolean).join(" ")],
      ["Organismo de tránsito", t.organismoTransito], ["Compañía", t.companiaNombre], ["Gestor", t.gestorNombre], ["Paso actual", (t.pasoNombre || "Paso " + t.pasoActual) + " de " + t.totalPasos], ["Creado", fecha(t.createdAt)], ["Actualizado", fecha(t.updatedAt)]];
    return cabecera({ il: "traspaso", ante: esc(t.tipoNombre || t.modalidad || "Trámite"), titulo: (t.placa ? placaTxt(t.placa) : esc(t.referenceNumber || "Trámite")), texto: esc([t.vehiculoMarca, t.vehiculoLinea].filter(Boolean).join(" ")), volver: ["tramites", "Trámites"],
      acciones: [["Abrir en FLIT", "abrir-flit:" + t.id]] }) +
      '<section class="card"><div class="vehiculo">' + ilus("traspaso") + '<div class="vehiculo__datos"><h3>Estado</h3><div class="vehiculo__estado">' + badge(ESTADOS_REALES, t.estado) +
      (t.signaturePending ? badge({ a: ["Firma pendiente", "warn"] }, "a") : "") + (t.prioritario ? badge({ a: ["Prioritario", "info"] }, "a") : "") + (t.subsanacionActiva ? badge({ a: ["Subsanación activa", "warn"] }, "a") : "") + "</div></div>" +
      '<span class="avance" style="min-width:220px"><span class="avance__barra"><span style="width:' + av + '%"></span></span><small>' + av + " %</small></span></div></section>" +
      '<div class="rejilla rejilla--2"><section class="card"><h2>Datos del trámite</h2><dl class="dl espacio">' + dl.filter(function (x) { return x[1]; }).map(function (x) { return "<div><dt>" + x[0] + "</dt><dd>" + (x[0] === "Placa" ? x[1] : esc(x[1])) + "</dd></div>"; }).join("") + "</dl></section>" +
      '<section class="card"><h2>Partes</h2><ul class="firmantes espacio"><li><div><strong>' + esc(t.vendedorNombre || "Sin vendedor") + "</strong><small>Vendedor" + (t.vendedorDocumento ? " · " + esc(t.vendedorDocumento) : "") + "</small></div></li>" +
      "<li><div><strong>" + esc(t.compradorNombre || "Sin comprador") + "</strong><small>Comprador" + (t.compradorDocumento ? " · " + esc(t.compradorDocumento) : "") + "</small></div>" + (t.signaturePending ? badge({ a: ["Firma pendiente", "warn"] }, "a") : "") + "</li></ul>" +
      (t.ultimoRechazoMotivo ? '<div class="barra-accion espacio"><p><strong>Último rechazo:</strong> ' + esc(t.ultimoRechazoMotivo) + "</p></div>" : "") + "</section></div>";
  }

  // Rutas: módulos propios + los definidos en datos.js
  var PROPIAS = {
    inicio: ["Inicio", "inicio"], tramites: ["Trámites", "tramites"], traspaso: ["Nuevo traspaso", "traspaso"], tramite: ["Traspaso KLM 482", "traspaso"],
    reportes: ["Reportes", "reportes"], marca: ["Juego de marca", "configuracion"], ingreso: ["Ingreso", "seguridad"], ayuda: ["Ayuda", "ayuda"], perfil: ["Mi perfil", "perfil"]
  };
  function titulo(id) { return PROPIAS[id] ? PROPIAS[id][0] : D.MODULOS[id] ? D.MODULOS[id].titulo : "Inicio"; }

  function cabecera(o) {
    return '<header class="page-header">' + ilus(o.il, "page-header__ilus") +
      '<div class="page-header__texto">' + '<nav class="miga" aria-label="Miga de pan"><a href="#inicio">Inicio</a><i aria-hidden="true">›</i>' + (o.volver ? '<a href="#' + o.volver[0] + '">' + o.volver[1] + '</a><i aria-hidden="true">›</i>' : o.ante ? "<span>" + o.ante + '</span><i aria-hidden="true">›</i>' : "") + '<span class="miga__actual" aria-current="page">' + o.titulo + "</span></nav>" +
      (MODO === "real" && !CONECTADAS[ruta.id] ? '<span class="badge info muestra">Datos de muestra: este módulo aún no está conectado</span>' : "") +
      (o.ante && o.volver ? '<p class="antetitulo">' + o.ante + "</p>" : "") + "<h1>" + o.titulo + "</h1>" + (o.texto ? "<p>" + o.texto + "</p>" : "") + "</div>" +
      (o.acciones && o.acciones.length ? '<div class="page-header__acciones">' + botones(o.acciones) + "</div>" : "") + "</header>";
  }
  // Acción: [texto, qué hace, "secundario"?]. Qué hace: "#ruta", "form:id", "abrir:dialogo", "aviso:texto", "descargar:archivo", "detalle:i".
  function botones(acciones) {
    return acciones.map(function (a, i) {
      var cls = "btn" + (a[2] === "secundario" || i > 0 ? " secondary" : "");
      return a[1].charAt(0) === "#" ? '<a class="' + cls + '" href="' + a[1] + '">' + a[0] + "</a>" : '<button class="' + cls + '" type="button" data-hacer="' + esc(a[1]) + '">' + a[0] + "</button>";
    }).join("");
  }
  function pestanas(base, lista, actual) {
    return '<nav class="pestanas" aria-label="Secciones de ' + esc(base.titulo) + '">' + lista.map(function (p) {
      return '<a href="#' + base.id + "?tab=" + p[0] + '"' + (p[0] === actual ? ' aria-current="page"' : "") + ">" + p[1] + "</a>";
    }).join("") + "</nav>";
  }

  // ---------- Filtros profesionales: rango de fechas, selectores, texto y sí/no; viven en la dirección ----------
  // Definición: { id, t (etiqueta), tipo: "rango" | "select" | "texto" | "si", opciones, texto (para "si") }
  var FILTROS_ABIERTOS = false;
  var hoyIso = function (d) { d = d || new Date(); return new Date(d.getTime() - d.getTimezoneOffset() * 6e4).toISOString().slice(0, 10); };
  var PRESETS = [["hoy", "Hoy"], ["7d", "Últimos 7 días"], ["mes", "Este mes"], ["anio", "Este año"]];
  function rangoPreset(p) {
    var h = new Date(), d = new Date(h);
    if (p === "7d") d.setDate(h.getDate() - 6);
    if (p === "mes") d = new Date(h.getFullYear(), h.getMonth(), 1);
    if (p === "anio") d = new Date(h.getFullYear(), 0, 1);
    return [hoyIso(d), hoyIso(h)];
  }
  var fechaCorta = function (iso) { var p = iso.split("-"); return p[2] + "/" + p[1] + "/" + p[0]; };
  function filtrosActivos(defs, q) {
    var a = [];
    defs.forEach(function (f) {
      if (f.tipo === "rango") {
        if (q.desde || q.hasta) a.push([["desde", "hasta", "fechade"], (q.fechade === "actualizado" ? "Actualizado" : "Creado") + ": " + (q.desde ? fechaCorta(q.desde) : "inicio") + " a " + (q.hasta ? fechaCorta(q.hasta) : "hoy")]);
      } else if (q[f.id]) a.push([[f.id], f.tipo === "si" ? f.texto : f.t + ": " + q[f.id]]);
    });
    return a;
  }
  function panelFiltros(defs, q, placeholder) {
    var act = filtrosActivos(defs, q);
    var campo = function (f) {
      var id = "flt-" + f.id;
      if (f.tipo === "rango") {
        return '<fieldset class="campo campo--ancho filtro-rango"><legend class="antetitulo">Fecha</legend>' +
          '<div class="filtro-rango__cabeza"><div class="segmentado" role="radiogroup" aria-label="Fecha de">' + [["creado", "Creación"], ["actualizado", "Actualización"]].map(function (o) {
            return '<label><input type="radio" name="fechade" value="' + o[0] + '"' + ((q.fechade || "creado") === o[0] ? " checked" : "") + "><span>" + o[1] + "</span></label>"; }).join("") + "</div>" +
          '<div class="presets">' + PRESETS.map(function (p) { return '<button class="chip chip--sm" type="button" data-preset="' + p[0] + '">' + p[1] + "</button>"; }).join("") + "</div>" +
          '<div class="filtro-rango__fechas"><label class="fecha-linea" for="flt-desde"><span>Desde</span><input class="control" type="date" id="flt-desde" name="desde" value="' + esc(q.desde || "") + '" max="' + hoyIso() + '"></label>' +
          '<span class="filtro-rango__guion" aria-hidden="true">a</span><label class="fecha-linea" for="flt-hasta"><span>Hasta</span><input class="control" type="date" id="flt-hasta" name="hasta" value="' + esc(q.hasta || "") + '" max="' + hoyIso() + '"></label></div></div>' +
          '<p class="error" id="flt-rango-error" role="alert" hidden></p></fieldset>';
      }
      if (f.tipo === "select") return '<div class="campo"><label for="' + id + '">' + f.t + '</label><select class="control" id="' + id + '" name="' + f.id + '"><option value="">Todos</option>' + f.opciones.map(function (o) { return "<option" + (q[f.id] === o ? " selected" : "") + ">" + esc(o) + "</option>"; }).join("") + "</select></div>";
      if (f.tipo === "si") return '<label class="filtro-si"><input type="checkbox" name="' + f.id + '" value="1"' + (q[f.id] ? " checked" : "") + '><svg class="i" aria-hidden="true"><use href="#i-check"/></svg><span>' + f.texto + "</span></label>";
      return '<div class="campo"><label for="' + id + '">' + f.t + '</label><input class="control" id="' + id + '" name="' + f.id + '" value="' + esc(q[f.id] || "") + '" placeholder="' + esc(f.ayuda || "") + '"></div>';
    };
    var sis = defs.filter(function (f) { return f.tipo === "si"; });
    return '<div class="filtros">' +
      '<div class="filtros__barra"><div class="control-icono filtros__buscar"><svg class="i"><use href="#i-buscar"/></svg><input class="control" id="filtro-q" type="search" placeholder="' + esc(placeholder) + '" aria-label="' + esc(placeholder) + '" value="' + esc(q.q || "") + '"></div>' +
      '<button class="btn secondary filtros__boton" type="button" id="filtros-boton" aria-expanded="' + FILTROS_ABIERTOS + '" aria-controls="filtros-panel">' + ilus("filtros") + '<span class="filtros__texto">Filtros</span>' + (act.length ? '<span class="filtros__cuenta" aria-label="' + act.length + ' activos">' + act.length + "</span>" : "") + "</button></div>" +
      (act.length ? '<div class="filtros__activos" aria-label="Filtros aplicados">' + act.map(function (a) { return '<button class="filtro-activo" type="button" data-quitar="' + a[0].join(",") + '" aria-label="Quitar filtro ' + esc(a[1]) + '"><span>' + esc(a[1]) + '</span><svg class="i" aria-hidden="true"><use href="#i-cerrar"/></svg></button>'; }).join("") + '<button class="btn sm ghost" type="button" data-limpiar>Limpiar todo</button></div>' : "") +
      '<form class="filtros__panel" id="filtros-panel" novalidate' + (FILTROS_ABIERTOS ? "" : " hidden") + '><div class="filtros__campos">' + defs.filter(function (f) { return f.tipo !== "si"; }).map(campo).join("") + "</div>" +
      (sis.length ? '<fieldset class="filtros__si"><legend class="antetitulo">Mostrar solo</legend><div class="filtros__si-lista">' + sis.map(campo).join("") + "</div></fieldset>" : "") +
      '<div class="filtros__pie"><button class="btn ghost" type="button" data-limpiar>Limpiar</button><button class="btn" type="submit">Aplicar filtros</button></div></form></div>';
  }
  function enlazarPanelFiltros() {
    var b = $("#filtros-boton"), panel = $("#filtros-panel");
    if (!b || b.hasAttribute("data-enlazado")) return;
    b.setAttribute("data-enlazado", "");
    b.addEventListener("click", function () {
      FILTROS_ABIERTOS = b.getAttribute("aria-expanded") !== "true";
      b.setAttribute("aria-expanded", String(FILTROS_ABIERTOS)); panel.hidden = !FILTROS_ABIERTOS;
      if (FILTROS_ABIERTOS) { var p = panel.querySelector("input:not([type=radio]), select"); if (p) p.focus(); }
    });
    $$("[data-preset]", panel).forEach(function (x) {
      x.addEventListener("click", function () {
        var r = rangoPreset(x.getAttribute("data-preset")); $("#flt-desde").value = r[0]; $("#flt-hasta").value = r[1];
        $$("[data-preset]", panel).forEach(function (y) { y.setAttribute("aria-pressed", String(y === x)); });
      });
    });
    panel.addEventListener("submit", function (ev) {
      ev.preventDefault();
      var d = $("#flt-desde"), h = $("#flt-hasta"), e = $("#flt-rango-error");
      if (d && h && d.value && h.value && d.value > h.value) { e.textContent = "La fecha Desde es posterior a Hasta. Corrige el rango."; e.hidden = false; d.setAttribute("aria-invalid", "true"); d.focus(); return; }
      var cambios = { vista: ruta.q.vista || "" };
      $$("[name]", panel).forEach(function (c) {
        if (c.type === "radio") { if (c.checked) cambios[c.name] = c.value === "creado" ? "" : c.value; }
        else if (c.type === "checkbox") cambios[c.name] = c.checked ? "1" : "";
        else cambios[c.name] = c.value.trim();
      });
      FILTROS_ABIERTOS = false;
      irA(conParams(cambios), true);
      var nb = $("#filtros-boton"); if (nb) nb.focus();
    });
  }
  // Filtros de una lista genérica: columnas de texto con pocas opciones se vuelven selectores
  function defsDeLista(spec) {
    var defs = [];
    spec.columnas.forEach(function (c, j) {
      if (c[1] !== "texto") return;
      var vals = spec.filas.map(function (f) { return f[j]; }).filter(function (v, i, a) { return v && a.indexOf(v) === i; });
      if (vals.length >= 2 && vals.length <= 8 && !/cuándo|fecha|recibido|actualizado|solicitada|enviado|radicado|eliminado|ingreso|uso|vigente/i.test(c[0])) defs.push({ id: "c" + j, t: c[0], tipo: "select", opciones: vals.sort(), col: j });
    });
    return defs;
  }

  // ---------- Lista genérica con conteos que filtran ----------
  function celda(tipo, v, spec) {
    if (tipo === "placa") return '<span class="placa">' + placaTxt(v) + "</span>";
    if (tipo === "estado") return badge(spec.estados, v);
    if (tipo === "personas") return '<span class="personas"><span>' + esc(v[0]) + "</span>" + (v[1] ? "<small>" + esc(v[1]) + "</small>" : "") + "</span>";
    if (tipo === "dinero") return pesos(v);
    if (tipo === "mono") return '<span class="mono">' + esc(v) + "</span>";
    if (tipo === "num") return '<span class="num">' + v + "</span>";
    if (tipo === "avance") return '<span class="avance"><span class="avance__barra"><span style="width:' + v + '%"></span></span><small>' + v + " %</small></span>";
    return esc(v);
  }
  function lista(spec, ruta, q, clave) {
    var iEst = spec.columnas.map(function (c) { return c[1]; }).indexOf("estado");
    var vista = q.vista && spec.estados && spec.estados[q.vista] ? q.vista : "";
    var texto = norm(q.q || "");
    var defs = spec.filtros || defsDeLista(spec);
    var pasaFiltros = function (x) {
      return defs.every(function (d) {
        if (d.fn) return d.fn(spec.meta ? spec.meta[x.i] : null, q);
        return d.col === undefined || !q[d.id] || x.f[d.col] === q[d.id];
      });
    };
    var filas = spec.filas.map(function (f, i) { return { f: f, i: i }; }).filter(function (x) {
      return (!vista || x.f[iEst] === vista) && (!texto || norm(JSON.stringify(x.f)).indexOf(texto) >= 0) && pasaFiltros(x);
    });
    var base = spec.filas.map(function (f, i) { return { f: f, i: i }; }).filter(function (x) { return (!texto || norm(JSON.stringify(x.f)).indexOf(texto) >= 0) && pasaFiltros(x); });
    var cuenta = function (k) { return base.filter(function (x) { return x.f[iEst] === k; }).length; };
    var chips = iEst < 0 ? "" : '<div class="embudo" role="group" aria-label="Filtrar por estado"><button class="chip" type="button" data-vista="" aria-pressed="' + (!vista) + '">' + kico("carpeta", "estado-ico") + "<span>Todos</span><strong>" + base.length + "</strong></button>" +
      Object.keys(spec.estados).filter(function (k) { return cuenta(k) > 0 || spec.estados === ET; }).map(function (k) {
        var n = cuenta(k);
        return '<button class="chip' + (spec.pendiente === k ? " chip--pendiente" : "") + '" type="button" data-vista="' + k + '" aria-pressed="' + (vista === k) + '"' + (n ? "" : " disabled") + ">" + imgEstado(k, spec.estados[k][1]) + "<span>" + spec.estados[k][0] + "</span><strong>" + n + "</strong></button>";
      }).join("") + "</div>";
    var barra = spec.barra && vista && spec.barra[vista] && filas.length
      ? '<div class="barra-accion"><p><strong>' + filas.length + " " + spec.barra[vista][0].split(" ")[0] + "</strong> " + spec.barra[vista][0].split(" ").slice(1).join(" ") + "</p>" + botonAccion(spec.barra[vista][1], spec.barra[vista][2], clave) + "</div>" : "";
    var abrir = spec.abrir, total = filas.length, pag = paginaActual(q, total);
    filas = filas.slice((pag - 1) * POR_PAGINA, pag * POR_PAGINA);
    var tabla = filas.length
      ? '<div class="tabla-wrap"><table><caption class="sr-only">' + esc(spec.titulo || "Lista") + "</caption><thead><tr>" + spec.columnas.map(function (c) { return '<th scope="col"' + (c[1] === "num" || c[1] === "dinero" ? ' class="der"' : "") + ">" + c[0] + "</th>"; }).join("") + '<th scope="col">Acciones</th></tr></thead><tbody>' +
        filas.map(function (x) {
          return "<tr>" + x.f.map(function (v, j) { var c = spec.columnas[j]; return '<td data-etiqueta="' + c[0] + '"' + (c[1] === "num" || c[1] === "dinero" ? ' class="der"' : "") + ">" + celda(c[1], v, spec) + "</td>"; }).join("") +
            '<td class="fila-abrir" data-etiqueta="Acciones"><button class="acciones-btn" type="button" aria-haspopup="menu" aria-expanded="false" data-acciones="' + clave + ":" + x.i + '"' + (abrir ? ' data-abrir-ruta="' + abrir + '"' : "") + ' aria-label="Acciones de ' + esc(Array.isArray(x.f[0]) ? x.f[0][0] : x.f[0]) + '">Acciones' + KI.abajo + "</button></td></tr>";
        }).join("") + "</tbody></table></div>" + paginador(pag, total)
      : '<div class="vacio">' + ilus("consulta-placa") + "<p><strong>No hay resultados con ese filtro.</strong></p><p>Prueba con otro texto o quita el filtro de estado.</p><button class=\"btn secondary\" type=\"button\" data-limpiar>Quitar filtros</button></div>";
    return '<section class="card"><div class="card__cabeza"><div><h2>' + (spec.subtitulo || "Listado") + "</h2><p>" + (iEst < 0 ? "Busca o filtra para encontrar lo que necesitas." : "Pulsa un estado o usa los filtros para ver solo esos registros.") + "</p></div></div>" +
      panelFiltros(defs, q, spec.buscar || "Buscar") + chips + barra + tabla + "</section>";
  }
  // Paginación: vistas de 10 como máximo. La página vive en la dirección (?pag=) para compartirla y volver.
  var POR_PAGINA = 10;
  function paginaActual(q, total) { var n = Math.max(1, Math.ceil(total / POR_PAGINA)); return Math.min(Math.max(parseInt(q.pag, 10) || 1, 1), n); }
  function paginador(pag, total) {
    var n = Math.ceil(total / POR_PAGINA); if (n <= 1) return "";
    var desde = (pag - 1) * POR_PAGINA + 1, hasta = Math.min(pag * POR_PAGINA, total), nums = [];
    for (var i = 1; i <= n; i++) if (i === 1 || i === n || Math.abs(i - pag) <= 1) nums.push(i); else if (nums[nums.length - 1] !== "…") nums.push("…");
    return '<nav class="paginador" aria-label="Páginas"><p class="paginador__texto" role="status">' + desde + "–" + hasta + " de " + total.toLocaleString("es-CO") + '</p><div class="paginador__botones">' +
      '<button class="paginador__num paginador__flecha" type="button" data-pag="' + (pag - 1) + '"' + (pag === 1 ? " disabled" : "") + ' aria-label="Página anterior">' + KI.izquierda + "</button>" +
      nums.map(function (x) { return x === "…" ? '<span class="paginador__salto" aria-hidden="true">…</span>' : '<button class="paginador__num" type="button" data-pag="' + x + '"' + (x === pag ? ' aria-current="page"' : "") + ' aria-label="Página ' + x + '">' + x + "</button>"; }).join("") +
      '<button class="paginador__num paginador__flecha" type="button" data-pag="' + (pag + 1) + '"' + (pag === n ? " disabled" : "") + ' aria-label="Página siguiente">' + KI.derecha + "</button></div></nav>";
  }
  function botonAccion(t, hacer, clave) {
    if (hacer.charAt(0) === "#") return '<a class="btn sm" href="' + hacer + '">' + t + "</a>";
    if (hacer.indexOf("detalle:") === 0) hacer = "detalle:" + clave + ":" + hacer.split(":")[1];
    return '<button class="btn sm" type="button" data-hacer="' + esc(hacer) + '">' + t + "</button>";
  }

  // ---------- Pantallas propias ----------
  function vInicio() {
    var m = [
      ["Trámites en curso", D.TRAMITES.filter(function (t) { return t.estado !== "finalizado" && t.estado !== "rechazado"; }).length, "tramites", "#tramites", "Ver todos"],
      ["Esperan firma", cuantos("firma"), "firmas", "#tramites?vista=firma", "Enviar recordatorio", true],
      ["En validación", cuantos("validacion"), "documentos", "#tramites?vista=validacion", "Revisar documentos"],
      ["Comparendos por vencer", 2, "comparendos", "#comparendos?vista=descuento", "Pagar con descuento", true]
    ];
    return '<div class="inicio-tope">' + bannerInicio() +
      '<section class="metricas" aria-label="Cifras de la operación">' + m.map(function (x) {
        return '<a class="metrica' + (x[5] ? " metrica--pendiente" : "") + '" href="' + x[3] + '"><span class="metrica__etiqueta">' + x[0] + '</span><span class="metrica__cifra">' + x[1] + "</span>" + ilus(x[2], "metrica__ilus") + '<span class="metrica__pie">' + x[4] + "</span></a>";
      }).join("") + "</section></div>" +
      '<div class="rejilla rejilla--2 espacio-abajo">' +
      '<section class="card"><div class="card__cabeza"><div><h2>Distribución general de trámites</h2><p>Por estado. Toca Trámites para ver cada grupo.</p></div><a class="btn sm secondary" href="#tramites">Ver trámites</a></div>' + dona(porEstado(), "Trámites por estado") + "</section>" +
      '<section class="card"><div class="card__cabeza"><div><h2>Seguimiento operativo</h2><p>Trámites iniciados por mes.</p></div><a class="btn sm secondary" href="#reportes">Ver reportes</a></div>' + barrasGrupo(MESES, SERIES_OPERACION, "Trámites iniciados por mes") + "</section></div>" +
      '<div class="rejilla rejilla--2">' +
      '<section class="card"><div class="card__cabeza"><div><h2>Accesos rápidos</h2><p>Lo que más se hace en el día.</p></div></div><div class="accesos">' +
      '<button class="acceso" type="button" data-hacer="form:nuevo-tramite">' + ilus("traspaso") + "<span><strong>Nuevo trámite</strong><span>Traspaso o matrícula inicial</span></span></button>" +
      '<button class="acceso" type="button" data-hacer="abrir:consulta">' + ilus("consulta-placa") + "<span><strong>Consultar placa</strong><span>Estado del vehículo en línea</span></span></button>" +
      '<a class="acceso" href="#identidad?vista=pendiente">' + ilus("identidad") + "<span><strong>Identidades pendientes</strong><span>Reenviar el enlace de validación</span></span></a>" +
      '<a class="acceso" href="#confirmacion-runt?vista=pendiente">' + ilus("fuentes-oficiales") + "<span><strong>Confirmación RUNT</strong><span>Radicados por confirmar</span></span></a>" +
      "</div></section>" +
      '<section class="card"><div class="card__cabeza"><div><h2>Actividad reciente</h2><p>Últimos movimientos en la plataforma.</p></div><a class="btn sm secondary" href="#auditoria">Ver auditoría</a></div><ul class="actividad">' +
      [["firma", "KLM482", "El vendedor firmó; falta el comprador.", "10:42"], ["validacion", "JNX219", "Se cargó el SOAT; queda en validación.", "09:15"], ["radicado", "GHT904", "Matrícula radicada ante el organismo de tránsito.", "Ayer"], ["finalizado", "RTY608", "Traspaso finalizado y entregado.", "3 nov"]].map(function (a) {
        return '<li><a class="placa" href="#tramite">' + placaTxt(a[1]) + "</a><span>" + a[2] + "</span>" + badge(ET, a[0]) + "<time>" + a[3] + "</time></li>";
      }).join("") + "</ul></section></div>" +
      '<section class="card"><div class="card__cabeza"><div><h2>Módulos</h2><p>Todo lo que puedes hacer en la plataforma. Los que tienen algo pendiente lo muestran.</p></div><button class="btn sm secondary" type="button" data-abrir="buscador">Buscar un módulo</button></div><div class="acordeones">' + mapaModulos() + "</div></section>";
  }
  // Lanzador de módulos: grupos con nombre, cada módulo como tarjeta con ilustración, para qué sirve
  // y, si tiene algo por resolver, una pastilla con cuántos (la tarjeta lleva directo a ese filtro).
  var LANZADOR = [
    ["Operación", "tramites", "El día a día de los trámites", [["tramites", "Trámites", "Traspasos y matrículas"], ["historial-placa", "Historial por placa", "Todo lo de un vehículo"], ["revocatorias", "Revocatorias", "Revertir un radicado"], ["identidad", "Identidad", "Validación antes de firmar"]]],
    ["Análisis", "reportes", "Cifras y trazabilidad", [["reportes", "Reportes", "La operación en cifras"], ["reportes-detallados", "Reportes detallados", "Cada trámite, para descargar"], ["auditoria", "Auditoría", "Quién hizo qué y cuándo"]]],
    ["Personas y permisos", "colaboradores", "Quién entra y qué puede hacer", [["usuarios", "Usuarios", "Invitar y administrar"], ["rbac", "Roles y permisos", "Qué puede hacer cada rol"]]],
    ["Compañías y tránsito", "empresas", "Clientes y organismos", [["companias", "Compañías", "Empresas y su configuración"], ["organismos", "Organismos de tránsito", "Dónde se radica cada trámite"], ["causales", "Causales de rechazo", "Por qué se devuelve un trámite"]]],
    ["Documentos", "documentos", "Lo que se genera y se radica", [["documental", "Documental", "Qué pide cada trámite"], ["improntas", "Improntas", "Motor y chasis"], ["generacion-documental", "Generación documental", "Contratos y mandatos en lote"], ["quipux", "Quipux", "Radicación por Quipux"]]],
    ["Configuración de trámites", "configuracion", "Reglas, formatos y RUNT", [["tipos-tramite", "Tipos de trámite", "Qué trámites están activos"], ["confirmacion-runt", "Confirmación RUNT", "Radicados por confirmar"], ["mandatos", "Mandatos", "Plantillas de mandato"], ["fur", "FUR", "Formulario único por trámite"]]],
    ["Comunicación y sistema", "notificaciones", "Avisos y tareas automáticas", [["notificaciones", "Notificaciones", "Correos y mensajes"], ["banners", "Banners", "Avisos para todos"], ["procesos", "Procesos periódicos", "Tareas automáticas"]]],
    ["Integraciones", "integraciones", "QX e ICT", [["log-qx", "Log QX", "Mensajes con QX"], ["ict-logs", "Log ICT", "Llamadas de los clientes"], ["ict-trazabilidad", "Trazabilidad ICT", "Del registro al trámite"], ["ict-reportes", "Reportes ICT", "Volumen por cliente"]]]
  ];
  var DESCRIPCION = {};
  LANZADOR.forEach(function (g) { g[3].forEach(function (it) { DESCRIPCION[it[0]] = it[2]; }); });
  function pendientesDe(id) {
    if (id === "tramites") return cuantos("firma");
    var m = D.MODULOS[id]; if (!m || !m.pendiente || !m.filas) return 0;
    var i = m.columnas.map(function (c) { return c[1]; }).indexOf("estado");
    return m.filas.filter(function (f) { return f[i] === m.pendiente; }).length;
  }
  // Módulos como acordeón de grupo (Diseño Inteligente): fila con contador a la izquierda, resumen de
  // pendientes a la derecha y flecha. Abre uno a la vez; el abierto vive en la dirección (?grupo=).
  function slugGrupo(t) { return t.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^a-z]+/g, "-").replace(/^-|-$/g, ""); }
  function mapaModulos() {
    var abierto = ruta && ruta.q && ruta.q.grupo;
    var items = LANZADOR.map(function (g) {
      var total = 0, slug = slugGrupo(g[0]), abre = abierto === slug;
      var filas = g[3].map(function (it) {
        var m = D.MODULOS[it[0]], n = pendientesDe(it[0]); total += n;
        var vista = it[0] === "tramites" ? "firma" : m && m.pendiente;
        var href = "#" + it[0] + (n ? "?vista=" + vista : "");
        return '<li><a class="modulo" href="' + href + '">' + ilus(m ? m.il : PROPIAS[it[0]][1]) + '<span class="modulo__texto"><strong>' + it[1] + "</strong><small>" + it[2] + "</small></span>" +
          (n ? '<span class="modulo__cuenta">' + n + '<span class="sr-only">' + (n === 1 ? " pendiente" : " pendientes") + "</span></span>" : "") + "</a></li>";
      }).join("");
      return '<section class="acordeon"><h3><button class="acordeon__fila" type="button" aria-expanded="' + abre + '" aria-controls="grupo-' + slug + '" data-grupo="' + slug + '">' +
        '<span class="acordeon__cuenta">' + g[3].length + '<span class="sr-only"> módulos</span></span>' + ilus(g[1]) +
        '<span class="acordeon__texto"><strong>' + g[0] + "</strong><small>" + g[2] + "</small></span>" +
        (total ? '<span class="badge warn">' + total + (total === 1 ? " pendiente" : " pendientes") + "</span>" : '<span class="acordeon__al-dia">Al día</span>') +
        '<svg class="i acordeon__flecha" aria-hidden="true"><use href="#i-arriba"/></svg></button></h3>' +
        '<div class="acordeon__panel" id="grupo-' + slug + '"' + (abre ? "" : " hidden") + '><ul class="grupo-mod__lista">' + filas + "</ul></div></section>";
    });
    var mitad = Math.ceil(items.length / 2);
    return '<div class="acordeones__col">' + items.slice(0, mitad).join("") + '</div><div class="acordeones__col">' + items.slice(mitad).join("") + "</div>";
  }



  // Filtros de Trámites con datos de muestra: rango de fechas, tipo, organismo, compañía, gestor, prioritario y firma.
  function filtrosTramites(lista) {
    var unicos = function (k) { return lista.map(function (t) { return t[k]; }).filter(function (v, i, a) { return v && a.indexOf(v) === i; }).sort(); };
    var dia = function (iso) { return hoyIso(new Date(iso)); };
    return [
      { id: "rango", tipo: "rango", fn: function (m, q) { var f = dia(q.fechade === "actualizado" ? m.actualizado : m.creado); return (!q.desde || f >= q.desde) && (!q.hasta || f <= q.hasta); } },
      { id: "tipo", t: "Tipo de trámite", tipo: "select", opciones: unicos("tipo"), fn: function (m, q) { return !q.tipo || m.tipo === q.tipo; } },
      { id: "organismo", t: "Organismo de tránsito", tipo: "select", opciones: unicos("organismo"), fn: function (m, q) { return !q.organismo || m.organismo === q.organismo; } },
      { id: "compania", t: "Compañía", tipo: "select", opciones: unicos("compania"), fn: function (m, q) { return !q.compania || m.compania === q.compania; } },
      { id: "gestor", t: "Gestor", tipo: "select", opciones: unicos("gestor"), fn: function (m, q) { return !q.gestor || m.gestor === q.gestor; } },
      { id: "prioritario", tipo: "si", texto: "Solo prioritarios", fn: function (m, q) { return !q.prioritario || m.prioritario; } },
      { id: "firma", tipo: "si", texto: "Solo con firma pendiente", fn: function (m, q) { return !q.firma || m.firmaPendiente; } }
    ];
  }
  function vTramites(q) {
    var spec = { titulo: "Trámites", subtitulo: "Estado de los trámites", estados: ET, pendiente: "firma", buscar: "Buscar por placa o persona", abrir: "#tramite",
      barra: { firma: ["trámites esperan firma. La plataforma le envía a cada firmante pendiente el enlace por correo y mensaje de texto.", "Enviar recordatorio a los firmantes", "aviso:Recordatorio enviado a los firmantes"], validacion: ["trámites tienen documentos por revisar.", "Revisar el primero", "#traspaso?paso=2"] },
      columnas: [["Placa", "placa"], ["Tipo", "texto"], ["Vendedor y comprador", "personas"], ["Estado", "estado"], ["Avance", "avance"], ["Actualizado", "texto"]],
      filas: D.TRAMITES.map(function (t) { return [t.placa, t.tipo, [t.vende, "a " + t.compra], t.estado, t.avance, fecha(t.actualizado)]; }),
      meta: D.TRAMITES,
      filtros: filtrosTramites(D.TRAMITES) };
    var html = lista(spec, "tramites", q, "tramites");
    return cabecera({ il: "tramites", ante: "Gestión integral", titulo: "Trámites", texto: "Traspasos y matrículas, del borrador a la entrega.", acciones: [["Nuevo trámite", "form:nuevo-tramite"], ["Consultar placa", "abrir:consulta"], ["Revocatorias", "#revocatorias"]] }) + html;
  }

  var PASOS = [["Consulta del vehículo", "Completo"], ["SOAT y revisión técnico-mecánica", "Completo"], ["Vendedor", "Completo"], ["Comprador", "En curso"], ["Firmas", "Pendiente"], ["Radicación", "Pendiente"]];
  var ACTUAL = 3;
  function vTraspaso(q) {
    var sel = Math.min(Math.max(parseInt(q.paso, 10) - 1 || ACTUAL, 0), ACTUAL);
    var pasos = PASOS.map(function (p, i) {
      var hecho = i < ACTUAL, alcanzable = i <= ACTUAL;
      var cont = '<span class="paso__num">' + (hecho ? '<svg class="i" style="width:16px;height:16px"><use href="#i-check"/></svg>' : i + 1) + '</span><span class="paso__texto"><strong>' + p[0] + "</strong><small>" + p[1] + "</small></span>";
      return "<li>" + (alcanzable ? '<button class="paso' + (hecho ? " paso--hecho" : "") + '" type="button" data-paso="' + (i + 1) + '"' + (i === sel ? ' aria-current="step"' : "") + ">" + cont + "</button>" : '<div class="paso">' + cont + "</div>") + "</li>";
    }).join("");
    var cuerpo = [
      '<h2>Consulta del vehículo</h2><dl class="dl espacio"><div><dt>Placa</dt><dd><span class="placa">KLM 482</span></dd></div><div><dt>Marca y línea</dt><dd>Mazda CX-30 Touring</dd></div><div><dt>Modelo</dt><dd>2022</dd></div><div><dt>Servicio</dt><dd>Particular</dd></div><div><dt>Propietario</dt><dd>Andrés Rojas</dd></div><div><dt>Multas</dt><dd>' + badge({ ok: ["Sin multas", "ok"] }, "ok") + "</dd></div></dl>",
      '<h2>SOAT y revisión técnico-mecánica</h2><div class="rejilla rejilla--2 espacio"><div class="carga carga--lista">' + ilus("seguridad") + "<strong>SOAT vigente hasta el 14 de marzo de 2027</strong><small>Leído del documento cargado</small></div><div class=\"carga carga--lista\">" + ilus("diagnostico") + "<strong>Revisión vigente hasta el 2 de agosto de 2027</strong><small>Leído del documento cargado</small></div></div>",
      '<h2>Vendedor</h2><dl class="dl espacio"><div><dt>Nombre</dt><dd>Andrés Rojas</dd></div><div><dt>Documento</dt><dd>CC 71 845 210</dd></div><div><dt>Correo</dt><dd>andres.rojas@correo.co</dd></div><div><dt>Identidad</dt><dd>' + badge({ ok: ["Validada", "ok"] }, "ok") + "</dd></div></dl>",
      '<h2>Comprador</h2><p class="nota">Con estos datos la plataforma le envía al comprador la validación de identidad y, después, el enlace de firma.</p>' +
      '<form class="formulario" id="form-comprador" novalidate>' +
      '<div class="campo"><label for="c-tipo">Tipo de documento</label><select class="control" id="c-tipo"><option>Cédula de ciudadanía</option><option>Cédula de extranjería</option><option>NIT</option><option>Pasaporte</option></select></div>' +
      '<div class="campo"><label for="c-num">Número de documento</label><input class="control" id="c-num" inputmode="numeric" value="1036 548 902"></div>' +
      '<div class="campo"><label for="c-nom">Nombres y apellidos</label><input class="control" id="c-nom" value="María Fernanda Ruiz"></div>' +
      '<div class="campo"><label for="c-correo">Correo</label><input class="control" id="c-correo" type="email" placeholder="nombre@correo.co" aria-describedby="c-correo-error"><p class="error" id="c-correo-error" role="alert" hidden></p></div>' +
      '<div class="campo"><label for="c-cel">Celular</label><input class="control" id="c-cel" inputmode="tel" value="300 482 1190"></div>' +
      '<div class="campo campo--ancho"><span class="etiqueta">Documento del comprador</span><button class="carga" type="button" data-cargar>' + ilus("documentos") + "<strong>Cargar el documento</strong><small>PDF o imagen, hasta 5 MB</small></button></div></form>"
    ][sel];
    var pie = '<div class="pie-acciones">' + (sel > 0 ? '<a class="btn secondary" href="#traspaso?paso=' + sel + '">Anterior</a>' : "<span></span>") + "<div>" +
      (sel === ACTUAL ? '<button class="btn ghost" type="button" data-hacer="aviso:Borrador guardado">Guardar borrador</button><button class="btn" type="button" id="enviar">Enviar validación</button>' : '<a class="btn" href="#traspaso?paso=' + (sel + 2) + '">Continuar</a>') + "</div></div>";
    return cabecera({ il: "traspaso", ante: "Traspaso estándar", titulo: "Traspaso KLM 482", texto: "Mazda CX-30 Touring 2022. Vende Andrés Rojas, compra María Fernanda Ruiz.", volver: ["tramites", "Trámites"] }) +
      '<div class="asistente"><nav class="card sin-relleno" aria-label="Pasos del traspaso"><ol class="pasos">' + pasos + '</ol><div class="pildora-tramite"><span>ID TRÁMITE: <strong>TRM-2026-000482</strong></span><span>TIPO: <strong>Traspaso estándar</strong></span><span>IDENTIFICADOR: <strong>PLACA KLM482</strong></span><span class="pildora-tramite__estado">En curso</span></div><p class="pasos__nota">Firmas y radicación se habilitan cuando el comprador valide su identidad.</p></nav>' +
      '<div><section class="card">' + vehiculo() + '</section><section class="card">' + cuerpo + pie + "</section></div></div>";
  }
  function vehiculo() {
    return '<div class="vehiculo"><span class="placa placa--grande">KLM 482</span><div class="vehiculo__datos"><p class="antetitulo">Vehículo</p><h2 class="vehiculo__nombre">Mazda CX-30 Touring</h2><p class="vehiculo__meta">Modelo 2022 · Particular · Gris cerámica</p><div class="vehiculo__estado">' +
      badge({ a: ["SOAT vigente", "ok"] }, "a") + badge({ a: ["Revisión vigente", "ok"] }, "a") + badge({ a: ["Sin multas", "ok"] }, "a") + "</div></div></div>";
  }

  // Seguimiento del trámite: qué falta arriba, vehículo y datos del trámite, avance, documentos, partes y actividad.
  function vTramite() {
    var linea = [["Creado", "28 oct", 1], ["Documentos", "29 oct", 1], ["Identidad", "Hoy", 1], ["Firmas", "En curso", 2], ["Radicado", "", 0], ["Finalizado", "", 0]];
    var docs = [["Improntas", "validada", "Validado por Sofía Cardona · 29 oct"], ["Paz y salvo", "validada", "Validado por Sofía Cardona · 29 oct"], ["SOAT", "validada", "Leído del documento · vence el 14 mar 2027"], ["Documento del vendedor", "validada", "Identidad confirmada · hoy"], ["Documento del comprador", "pendiente", "Lo carga María Fernanda Ruiz"], ["Contrato de compraventa", "generado", "Listo para firmar · 29 oct"]];
    var ED = { validada: ["Validado", "ok"], pendiente: ["Por cargar", "warn"], generado: ["Generado por FLIT", "info"] };
    var datos = [["Organismo de tránsito", "Secretaría de Movilidad de Medellín"], ["Compañía", "Concesionario Andino"], ["Creado por", "Sofía Cardona · 28 oct"], ["Radicado", "Cuando estén las dos firmas"]];
    var partes = [["Andrés Rojas", "Vendedor · CC 71 845 210", ["Firmó hoy, 09:58", "ok"], ""], ["María Fernanda Ruiz", "Compradora · CC 1036 548 902", ["Firma pendiente", "warn"], '<button class="btn sm" type="button" data-hacer="aviso:Enlace de firma reenviado a María Fernanda Ruiz">Reenviar enlace</button>']];
    var actividad = [["Hoy, 09:58", "Andrés Rojas firmó el contrato."], ["Hoy, 09:40", "Se confirmó la identidad del vendedor y de la compradora."], ["29 oct, 16:12", "Sofía Cardona cargó las improntas y el paz y salvo."], ["29 oct, 15:30", "La plataforma generó el contrato de compraventa."], ["28 oct, 11:05", "Sofía Cardona creó el trámite."]];
    var iniciales = function (n) { return n.split(" ").slice(0, 2).map(function (x) { return x.charAt(0); }).join(""); };
    var hechos = linea.filter(function (l) { return l[2] === 1; }).length;
    return cabecera({ il: "traspaso", ante: "Seguimiento", titulo: "Traspaso KLM 482", texto: "De Andrés Rojas a María Fernanda Ruiz.", volver: ["tramites", "Trámites"], acciones: [["Continuar el trámite", "#traspaso"], ["Descargar expediente", "descargar:expediente-KLM482.pdf"]] }) +
      '<div class="barra-accion"><p><strong>Faltan 2 cosas para radicar:</strong> la firma de María Fernanda Ruiz y su documento de identidad.</p><button class="btn sm" type="button" data-hacer="aviso:Le recordamos a María Fernanda Ruiz la firma y el documento">Recordarle a la compradora</button></div>' +
      '<section class="card"><div class="tramite-resumen">' + vehiculo() + '<dl class="dl detalle__fichas">' + datos.map(function (d) { return "<div><dt>" + d[0] + "</dt><dd>" + d[1] + "</dd></div>"; }).join("") + "</dl></div>" +
      '<div class="tramite-avance"><div class="card__cabeza"><div><h2>Avance</h2><p>Paso ' + (hechos + 1) + " de " + linea.length + ": firmas.</p></div></div>" +
      '<ol class="linea" aria-label="Avance del trámite">' + linea.map(function (l) {
        return '<li class="linea__paso' + (l[2] === 1 ? " linea__paso--hecho" : l[2] === 2 ? " linea__paso--actual" : "") + '"' + (l[2] === 2 ? ' aria-current="step"' : "") + '><span class="linea__punto" aria-hidden="true"></span><strong>' + l[0] + "</strong><small>" + (l[1] || "Pendiente") + "</small></li>";
      }).join("") + '</ol><div class="pildora-tramite"><span>ID TRÁMITE: <strong>TRM-2026-000482</strong></span><span>TIPO: <strong>Traspaso estándar</strong></span><span>IDENTIFICADOR: <strong>PLACA KLM482</strong></span><span class="pildora-tramite__estado">Pendiente de firma</span></div></div></section>' +
      '<div class="rejilla rejilla--2 rejilla--arriba"><section class="card"><div class="card__cabeza"><div><h2>Documentos</h2><p>5 de 6 listos.</p></div></div><ul class="lista-fila">' +
      docs.map(function (d) {
        return '<li><div class="lista-fila__texto"><strong>' + d[0] + "</strong><small>" + d[2] + "</small></div>" + badge(ED, d[1]) +
          (d[1] === "pendiente" ? '<button class="btn sm" type="button" data-hacer="aviso:Le pedimos el documento a la compradora">Pedirlo</button>' : '<button class="btn sm ghost" type="button" data-hacer="descargar:' + d[0] + '.pdf" aria-label="Ver ' + d[0] + '">Ver</button>') + "</li>";
      }).join("") + "</ul></section>" +
      '<div><section class="card"><div class="card__cabeza"><div><h2>Partes y firmas</h2><p>Falta la firma de la compradora.</p></div></div><ul class="lista-fila">' +
      partes.map(function (x) { return '<li><span class="persona__avatar" aria-hidden="true">' + iniciales(x[0]) + '</span><div class="lista-fila__texto"><strong>' + x[0] + "</strong><small>" + x[1] + "</small></div>" + badge({ a: x[2] }, "a") + x[3] + "</li>"; }).join("") + "</ul></section>" +
      '<section class="card"><div class="card__cabeza"><div><h2>Actividad</h2><p>Lo último que pasó con este trámite.</p></div><a class="btn sm secondary" href="#auditoria">Ver auditoría</a></div><ol class="actividad-linea">' +
      actividad.map(function (x) { return "<li><time>" + x[0] + "</time><span>" + x[1] + "</span></li>"; }).join("") + "</ol></section></div></div>";
  }


  function vReportes(q) {
    var actual = D.REPORTES.filter(function (r) { return r[0] === q.tab; })[0] || D.REPORTES[0];
    // Cada reporte: [id, nombre, cifras, barras]
    var cifras = actual[2], barras = actual[3];
    var max = Math.max.apply(null, barras.map(function (b) { return b[1]; }));
    return cabecera({ il: "reportes", ante: "Reportes", titulo: "Reportes", texto: "La operación en cifras. Cada reporte se descarga o se programa para que llegue al correo.", acciones: [["Descargar", "descargar:" + actual[1] + ".xlsx"], ["Programar envío", "form:programar-reporte"]] }) +
      pestanas({ id: "reportes", titulo: "Reportes" }, D.REPORTES.map(function (r) { return [r[0], r[1]]; }), actual[0]) +
      '<section class="metricas" aria-label="' + actual[1] + '">' + cifras.map(function (c, i) {
        return '<a class="metrica" href="#reportes-detallados"><span class="metrica__etiqueta">' + c[0] + '</span><span class="metrica__cifra">' + c[1] + "</span>" + ilus(["tramites", "trazabilidad", "procesos", "causales"][i], "metrica__ilus") + '<span class="metrica__pie">Ver el detalle</span></a>';
      }).join("") + "</section>" +
      '<div class="rejilla rejilla--3 espacio-abajo">' + [["Matrículas", function (t) { return t.tipo === "Matrícula inicial"; }], ["Traspasos", function (t) { return t.tipo === "Traspaso"; }], ["Otros", function (t) { return t.tipo !== "Matrícula inicial" && t.tipo !== "Traspaso"; }]].map(function (g) {
        return '<section class="card"><div class="card__cabeza"><h2>' + g[0] + '</h2><strong class="grafico-total">' + D.TRAMITES.filter(g[1]).length + "</strong></div>" + dona(porEstado(g[1]), g[0] + " por estado") + "</section>";
      }).join("") + "</div>" +
      '<section class="card espacio-abajo"><div class="card__cabeza"><div><h2>Tendencia mensual</h2><p>Trámites iniciados por mes y categoría.</p></div></div>' + barrasGrupo(MESES, SERIES_OPERACION, "Tendencia mensual por categoría") + "</section>" +
      '<section class="card"><div class="card__cabeza"><div><h2>' + actual[1] + "</h2><p>Últimos 30 días.</p></div>" + '<div class="campo campo--en-linea"><label for="periodo">Periodo</label><select class="control" id="periodo" data-aviso-cambio="Periodo actualizado"><option>Últimos 30 días</option><option>Este mes</option><option>Este año</option></select></div></div>' +
      '<ul class="barras">' + barras.map(function (b) { return '<li><span class="barras__et">' + b[0] + '</span><span class="barras__pista"><span style="width:' + Math.round(b[1] / max * 100) + '%"></span></span><span class="barras__val">' + String(b[1]).replace(".", ",") + "</span></li>"; }).join("") + "</ul></section>";
  }

  function vRbac() {
    var mods = ["Trámites", "Identidad", "Reportes", "Usuarios", "Compañías", "Organismos", "Auditoría", "Integraciones"];
    var permisos = ["Ver", "Crear", "Editar", "Eliminar"];
    // Permisos típicos del Radicador: crea y edita trámites, prevalida identidad y consulta reportes.
    var RADICADOR = { "Trámites": ["Ver", "Crear", "Editar"], "Identidad": ["Ver", "Crear"], "Reportes": ["Ver"] };
    var dado = function (m, p) { return (RADICADOR[m] || []).indexOf(p) >= 0; };
    return '<div class="rbac"><section class="card"><h2>Roles</h2><ul class="roles" role="list">' +
      [["Radicador", "Compañía", true], ["Admin de compañía", "Compañía"], ["Consulta", "Compañía"], ["Admin del organismo", "Organismo de tránsito"], ["Super Admin", "Plataforma"]].map(function (r) {
        return '<li><button class="rol" type="button" aria-pressed="' + !!r[2] + '" data-rol><strong>' + r[0] + "</strong><small>" + r[1] + "</small></button></li>";
      }).join("") + '</ul><button class="btn secondary ancho" type="button" data-hacer="form:nuevo-rol">Nuevo rol</button></section>' +
      '<section class="card"><div class="card__cabeza"><div><h2>Permisos del rol Radicador</h2><p>Los cambios se aplican en el próximo ingreso de cada persona.</p></div><button class="btn" type="button" data-hacer="aviso:Permisos guardados">Guardar permisos</button></div>' +
      '<div class="tabla-wrap"><table class="matriz"><caption class="sr-only">Permisos por módulo</caption><thead><tr><th scope="col">Módulo</th>' + permisos.map(function (p) { return '<th scope="col">' + p + "</th>"; }).join("") + "</tr></thead><tbody>" +
      mods.map(function (m) { return '<tr><th scope="row">' + m + "</th>" + permisos.map(function (p) { return '<td data-etiqueta="' + p + '"><input type="checkbox" aria-label="' + p + " en " + m + '"' + (dado(m, p) ? " checked" : "") + "></td>"; }).join("") + "</tr>"; }).join("") +
      "</tbody></table></div></section></div>";
  }

  function vImpronta() {
    return '<section class="card"><div class="card__cabeza"><div><h2>Generar impronta</h2><p>Carga la foto del motor y del chasis. La plataforma lee los números y arma el documento.</p></div></div>' +
      '<form class="formulario" novalidate data-form-simple="Impronta generada para KLM 482"><div class="campo"><label for="imp-placa">Placa</label><input class="control placa-input" id="imp-placa" value="KLM482" maxlength="6"></div>' +
      '<div class="campo"><label for="imp-motor">Número de motor</label><input class="control" id="imp-motor" value="PE30123456"></div><div class="campo"><label for="imp-chasis">Número de chasis</label><input class="control" id="imp-chasis" value="3MVDM2W7XNL1234"></div>' +
      '<div class="campo"><span class="etiqueta">Foto del motor</span><button class="carga" type="button" data-cargar>' + ilus("improntas") + "<strong>Cargar foto</strong><small>JPG o PNG</small></button></div>" +
      '<div class="campo"><span class="etiqueta">Foto del chasis</span><button class="carga" type="button" data-cargar>' + ilus("improntas") + "<strong>Cargar foto</strong><small>JPG o PNG</small></button></div>" +
      '<div class="campo campo--ancho pie-acciones"><span></span><div><button class="btn" type="submit">Generar impronta</button></div></div></form></section>';
  }

  function vNotificaciones() {
    var lista = [["Trámite radicado", "Correo", true], ["Firma pendiente", "Correo y mensaje de texto", true], ["Trámite rechazado", "Correo", true], ["Trámite finalizado", "Correo", true], ["Documento por vencer", "Correo", false]];
    return '<div class="rejilla rejilla--lado"><section class="card"><div class="card__cabeza"><div><h2>Plantillas</h2><p>Enciende o apaga cada envío.</p></div><button class="btn sm" type="button" data-hacer="form:nueva-notificacion">Nueva notificación</button></div><ul class="firmantes">' +
      lista.map(function (n, i) { return "<li><div><strong>" + n[0] + "</strong><small>" + n[1] + '</small></div><button class="interruptor" type="button" role="switch" aria-checked="' + n[2] + '" aria-label="Enviar ' + n[0].toLowerCase() + '" data-switch><span></span></button></li>'; }).join("") +
      '</ul></section><section class="card"><div class="card__cabeza"><div><h2>Vista previa</h2><p>Firma pendiente, por correo.</p></div><button class="btn sm secondary" type="button" data-hacer="aviso:Correo de prueba enviado a laura.gomez@flitsas.com">Enviarme una prueba</button></div>' +
      '<div class="correo"><img class="correo__logo" src="assets/kit/logo-claro.svg" alt="FLIT"><p class="antetitulo">Firma pendiente</p><h3>María Fernanda, falta tu firma</h3><p>El traspaso del vehículo KLM 482 está listo. Firma desde tu celular en menos de dos minutos.</p>' +
      '<div class="correo__datos"><span>Vehículo</span><strong>Mazda CX-30 2022 · KLM 482</strong><span>Vendedor</span><strong>Andrés Rojas</strong></div><span class="btn">Firmar ahora</span><p class="correo__pie">FLIT S.A.S. · Medellín · soporte@flitsas.com</p></div></section></div>';
  }

  function vBanners() {
    var b = [["Mantenimiento programado", "La plataforma no estará disponible el domingo de 2:00 a 4:00 a. m.", "programado", true], ["Nuevo: traspaso unilateral", "Ya puedes radicar traspasos de leasing al locatario.", "publicado", true], ["Cambio en el paz y salvo", "Desde el 1 de diciembre se pide el paz y salvo con menos de 30 días.", "borrador", false]];
    var EB = { programado: ["Programado", "info"], publicado: ["Publicado", "ok"], borrador: ["Borrador", ""] };
    return '<section class="card"><div class="card__cabeza"><div><h2>Banners</h2><p>Así los ve cada usuario arriba de la plataforma.</p></div><button class="btn" type="button" data-hacer="form:nuevo-banner">Nuevo banner</button></div><ul class="banners">' +
      b.map(function (x, i) { return '<li><div class="banner-muestra banner-muestra--' + ["degradado", "azul", "tinta"][i % 3] + '"><strong>' + x[0] + "</strong><span>" + x[1] + '</span></div><div class="banners__pie">' + badge(EB, x[2]) + '<button class="interruptor" type="button" role="switch" aria-checked="' + x[3] + '" aria-label="Mostrar ' + x[0] + '" data-switch><span></span></button></div></li>'; }).join("") + "</ul></section>";
  }

  function vProcesos() {
    var p = [["Sincronizar estados con el RUNT", "Cada 30 minutos", "Hoy, 10:30", "ok", true], ["Recordatorio de firmas pendientes", "Cada día, 8:00 a. m.", "Hoy, 08:00", "ok", true], ["Vencimiento de SOAT y revisión", "Cada día, 6:00 a. m.", "Hoy, 06:00", "ok", true], ["Reintento de envíos a Quipux", "Cada 15 minutos", "Hoy, 10:45", "error", true], ["Limpieza de borradores de más de 90 días", "Cada domingo", "2 nov", "ok", false]];
    return '<section class="card"><div class="card__cabeza"><div><h2>Procesos</h2><p>Cada uno se puede apagar o correr ahora.</p></div></div><ul class="firmantes">' +
      p.map(function (x) { return '<li><div><strong>' + x[0] + "</strong><small>" + x[1] + " · Última vez: " + x[2] + "</small></div>" + badge({ ok: ["Correcto", "ok"], error: ["Falló", "danger"] }, x[3]) +
        '<button class="btn sm secondary" type="button" data-hacer="aviso:' + x[0] + ' en curso">Correr ahora</button><button class="interruptor" type="button" role="switch" aria-checked="' + x[4] + '" aria-label="Activar ' + x[0] + '" data-switch><span></span></button></li>'; }).join("") + "</ul></section>";
  }

  function vReportesIct() {
    var c = [["Centro de soporte", 214, 2], ["Integración Andino", 132, 9], ["Valle pruebas", 18, 0]];
    var max = 214;
    return '<section class="metricas"><a class="metrica" href="#ict-logs"><span class="metrica__etiqueta">Llamadas este mes</span><span class="metrica__cifra">364</span>' + ilus("integraciones", "metrica__ilus") + '<span class="metrica__pie">Ver el log</span></a>' +
      '<a class="metrica metrica--pendiente" href="#ict-logs?vista=error"><span class="metrica__etiqueta">Con error</span><span class="metrica__cifra">11</span>' + ilus("causales", "metrica__ilus") + '<span class="metrica__pie">Revisar errores</span></a>' +
      '<a class="metrica" href="#ict-trazabilidad?vista=materializado"><span class="metrica__etiqueta">Convertidos en trámite</span><span class="metrica__cifra">58</span>' + ilus("trazabilidad", "metrica__ilus") + '<span class="metrica__pie">Ver la trazabilidad</span></a></section>' +
      '<section class="card"><div class="card__cabeza"><div><h2>Llamadas por cliente</h2><p>Este mes. Entre paréntesis, las que fallaron.</p></div><button class="btn sm secondary" type="button" data-hacer="descargar:reportes-ict.xlsx">Descargar</button></div><ul class="barras">' +
      c.map(function (x) { return '<li><span class="barras__et">' + x[0] + '</span><span class="barras__pista"><span style="width:' + Math.round(x[1] / max * 100) + '%"></span></span><span class="barras__val">' + x[1] + " (" + x[2] + ")</span></li>"; }).join("") + "</ul></section>";
  }

  function vConfigTramite(spec) {
    return '<section class="card"><div class="card__cabeza"><div><h2>Configuración de ' + spec.tramite + "</h2><p>Cómo trabaja esta compañía este trámite.</p></div></div>" +
      '<ul class="firmantes"><li><div><strong>Habilitado</strong><small>La compañía puede crear y radicar ' + spec.tramite + '</small></div><button class="interruptor" type="button" role="switch" aria-checked="true" aria-label="Habilitar ' + spec.tramite + '" data-switch><span></span></button></li>' +
      '<li><div><strong>Firma en línea</strong><small>Vendedor y comprador firman desde el celular</small></div><button class="interruptor" type="button" role="switch" aria-checked="true" aria-label="Firma en línea" data-switch><span></span></button></li>' +
      '<li><div><strong>Validación de identidad antes de firmar</strong><small>Con foto del documento y del rostro</small></div><button class="interruptor" type="button" role="switch" aria-checked="true" aria-label="Validación de identidad" data-switch><span></span></button></li>' +
      '<li><div><strong>Radicar sin todos los documentos</strong><small>Solo para clientes con acuerdo firmado</small></div><button class="interruptor" type="button" role="switch" aria-checked="false" aria-label="Radicar sin todos los documentos" data-switch><span></span></button></li></ul>' +
      '<div class="pie-acciones"><span></span><div><button class="btn" type="button" data-hacer="aviso:Configuración guardada">Guardar cambios</button></div></div></section>';
  }
  function vConfigForm(campos, ok) {
    return '<section class="card"><form class="formulario" novalidate data-form-simple="' + ok + '">' + campos.map(function (c) {
      return '<div class="campo"><label for="cf-' + c[0] + '">' + c[1] + "</label>" + (c[3] ? '<select class="control" id="cf-' + c[0] + '">' + c[3].map(function (o) { return "<option>" + o + "</option>"; }).join("") + "</select>" : '<input class="control" id="cf-' + c[0] + '" value="' + esc(c[2]) + '">') + "</div>";
    }).join("") + '<div class="campo campo--ancho pie-acciones"><span></span><div><button class="btn" type="submit">Guardar cambios</button></div></div></form></section>';
  }

  function vAyuda(q) {
    var t = norm(q.q || "");
    var arts = D.MANUAL.filter(function (a) { return !t || norm(a[0] + a[1]).indexOf(t) >= 0; });
    return cabecera({ il: "ayuda", ante: "Manual de usuario", titulo: "Ayuda", texto: "Guías cortas de cada parte de la plataforma.", acciones: [["Escribir a soporte", "aviso:Abrimos un caso. Te respondemos a laura.gomez@flitsas.com"]] }) +
      '<section class="card"><div class="control-icono"><svg class="i"><use href="#i-buscar"/></svg><input class="control" id="filtro-q" type="search" placeholder="Buscar en el manual" aria-label="Buscar en el manual" value="' + esc(q.q || "") + '"></div></section>' +
      '<div class="accesos espacio">' + (arts.length ? arts.map(function (a) { return '<button class="acceso" type="button" data-hacer="aviso:Abriendo la guía ' + a[0] + '">' + ilus(a[2]) + "<span><strong>" + a[0] + "</strong><span>" + a[1] + "</span></span></button>"; }).join("") : '<p class="vacio">No hay guías con ese texto. Prueba con otra palabra o escribe a soporte.</p>') + "</div>";
  }
  function vPerfil() {
    return cabecera({ il: "perfil", ante: "Tu cuenta", titulo: "Mi perfil", texto: "Laura Gómez · Super Admin · FLIT S.A.S.", acciones: [["Cambiar contraseña", "form:contrasena"]] }) +
      vConfigForm([["nombre", "Nombres y apellidos", "Laura Gómez"], ["correo", "Correo", "laura.gomez@flitsas.com"], ["celular", "Celular", "300 555 0101"], ["idioma", "Idioma", "", ["Español"]]], "Perfil actualizado");
  }

  var LOGOS = [["logo-claro", "Logo, fondo claro", "claro"], ["logo-oscuro", "Logo, fondo oscuro", "oscuro"], ["isotipo", "Isotipo sobre el degradado", "claro"], ["favicon", "Favicon", "claro"]];
  var ILUSTRACIONES = ["inicio", "tramites", "traspaso", "matricula", "consulta-placa", "comparendos", "diagnostico", "colaboradores", "empresas", "documentos", "firmas", "biometria", "notificaciones", "reportes", "configuracion", "perfil", "seguridad", "fuentes-oficiales", "trazabilidad", "ayuda", "pendientes", "vencidos", "organismos", "improntas", "integraciones", "auditoria", "procesos", "mandatos", "fur", "banners", "historial-placa", "causales", "preasignacion", "revocatorias", "generacion-documental", "roles", "contrasena", "identidad"];
  var NOMBRES_ILUS = { inicio: "Inicio", tramites: "Trámites", traspaso: "Traspaso", matricula: "Matrícula", "consulta-placa": "Consulta de placa", comparendos: "Comparendos", diagnostico: "Diagnóstico", colaboradores: "Colaboradores", empresas: "Empresas", documentos: "Documentos", firmas: "Firmas", biometria: "Biometría", notificaciones: "Notificaciones", reportes: "Reportes", configuracion: "Configuración", perfil: "Perfil", seguridad: "Seguridad", "fuentes-oficiales": "Fuentes oficiales", trazabilidad: "Trazabilidad", ayuda: "Ayuda", pendientes: "Pendientes", vencidos: "Vencidos", organismos: "Organismos", improntas: "Improntas", integraciones: "Integraciones", auditoria: "Auditoría", procesos: "Procesos", mandatos: "Mandatos", fur: "FUR", banners: "Banners", "historial-placa": "Historial por placa", causales: "Causales", preasignacion: "Preasignación", revocatorias: "Revocatorias", "generacion-documental": "Generación documental", roles: "Roles", contrasena: "Contraseña", identidad: "Identidad" };
  function vMarca() {
    return cabecera({ il: "configuracion", ante: "Marca", titulo: "Juego de marca", texto: "Logo, isotipo, favicon e íconos tomados del repositorio de íconos de FLIT, con los colores del Manual UI Kit 2026." }) +
      '<section class="card"><div class="card__cabeza"><div><h2>Logos</h2><p>La logometría del repositorio de íconos, sin cambios de forma ni de proporción.</p></div></div><div class="marca-rejilla">' +
      LOGOS.map(function (l) { return '<figure class="marca-item"><div class="sobre-' + l[2] + '"><img src="assets/kit/' + l[0] + '.svg" alt="Logo FLIT: ' + l[1] + '"></div><figcaption>' + l[1] + "</figcaption></figure>"; }).join("") +
      '</div></section><section class="card"><div class="card__cabeza"><div><h2>Íconos de módulo</h2><p>' + ILUSTRACIONES.length + ' íconos de línea para cabeceras, menú, buscador y cifras.</p></div></div><div class="ilus-rejilla">' +
      ILUSTRACIONES.map(function (n) { return "<figure>" + ilus(n) + "<figcaption>" + (NOMBRES_ILUS[n] || n.replace(/-/g, " ")) + "</figcaption></figure>"; }).join("") + "</div></section>";
  }
  function vIngreso() {
    var correoGuardado = almacen.getItem("flit-correo") || "";
    var conexion = MODO === "real"
      ? '<p class="conexion conexion--real"><span class="conexion__punto" aria-hidden="true"></span>Conectada a FLIT ' + esc(AMBIENTE) + " · solo lectura</p>"
      : '<p class="conexion"><span class="conexion__punto" aria-hidden="true"></span>Modo de muestra · cualquier correo y contraseña entran</p>';
    return '<div class="ingreso">' +
      '<section class="ingreso__marca" aria-label="FLIT">' +
      '<img class="ingreso__logo" src="assets/kit/logo-oscuro.svg" alt="FLIT">' +
      '<div class="ingreso__texto"><p class="ingreso__ante">Plataforma de trámites de tránsito</p>' +
      '<h1 class="ingreso__titular">Tus trámites vehiculares<span>en un solo lugar</span></h1>' +
      "<p class=\"ingreso__lead\">Traspasos, matrículas, comparendos y el estado de cada vehículo, conectados con las fuentes oficiales y firmados en línea.</p></div>" +
      '<figure class="ingreso__escena" aria-hidden="true"><img src="assets/ilustraciones/heroe-ingreso.webp" alt="" width="1100" height="821">' +
      '<div class="flotante flotante--1"><span class="flotante__icono">' + '<svg class="i"><use href="#i-check"/></svg></span><span><strong>Traspaso firmado</strong><small>KLM 482 · hace 2 min</small></span></div>' +
      '<div class="flotante flotante--2"><span class="flotante__icono flotante__icono--azul"><svg class="i"><use href="#i-candado"/></svg></span><span><strong>Identidad validada</strong><small>Comprador · en línea</small></span></div>' +
      '<div class="flotante flotante--3"><span class="flotante__icono flotante__icono--naranja"><svg class="i"><use href="#i-correo"/></svg></span><span><strong>Radicado en el organismo</strong><small>Medellín · hoy</small></span></div></figure>' +
      '<ul class="ingreso__productos" aria-label="Productos de FLIT">' + [["Trámites", "tramites"], ["Comparendos", "comparendos"], ["Diagnóstico", "diagnostico"]].map(function (p) { return "<li>" + ilus(p[1]) + "<span>" + p[0] + "</span></li>"; }).join("") + "</ul>" +
      '<p class="ingreso__pie">FLIT S.A.S. · Medellín, Colombia · Certificada ISO/IEC 27001:2022</p></section>' +
      '<section class="ingreso__form"><form class="ingreso__tarjeta" id="form-ingreso" novalidate>' +
      '<img class="ingreso__logo-form logo--claro" src="assets/kit/logo-claro.svg" alt="FLIT"><img class="ingreso__logo-form logo--oscuro" src="assets/kit/logo-oscuro.svg" alt="FLIT">' +
      '<div><h2>Ingresa a tu cuenta</h2><p class="nota">Usa el correo y la contraseña con los que entras a FLIT.</p></div>' + conexion +
      '<div class="campo"><label for="ing-correo">Correo</label><div class="control-icono"><svg class="i"><use href="#i-correo"/></svg><input class="control" id="ing-correo" type="email" autocomplete="username" inputmode="email" placeholder="nombre@empresa.co" value="' + esc(correoGuardado) + '" aria-describedby="i-error"></div></div>' +
      '<div class="campo"><label for="i-clave">Contraseña</label><div class="clave control-icono"><svg class="i"><use href="#i-candado"/></svg><input class="control" id="i-clave" type="password" autocomplete="current-password" aria-describedby="i-error"><button type="button" id="ver-clave" aria-pressed="false" aria-label="Mostrar contraseña"><svg class="i"><use href="#i-ojo"/></svg></button></div></div>' +
      '<div class="ingreso__fila"><label class="recordar"><input type="checkbox" id="i-recordar"' + (correoGuardado ? " checked" : "") + '> Recordar mi correo</label><button class="enlace" type="button" id="olvide">Olvidé mi contraseña</button></div>' +
      '<p class="error ingreso__error" id="i-error" role="alert" hidden></p>' +
      '<button class="btn ingreso__boton" type="submit" id="i-entrar">Ingresar</button>' +
      '<p class="ingreso__ayuda">¿Problemas para entrar? Escribe a <a href="mailto:soporte@flitsas.com">soporte@flitsas.com</a></p></form></section></div>';
  }


  // Módulo definido en datos.js: con pestañas, con pantalla propia o lista
  var RENDERS = { rbac: vRbac, impronta: vImpronta, notificaciones: vNotificaciones, banners: vBanners, procesos: vProcesos, "reportes-ict": vReportesIct, "config-tramite": vConfigTramite,
    "config-empresa": function () { return vConfigForm([["razon", "Razón social", "Concesionario Andino"], ["nit", "NIT", "900123456"], ["correo", "Correo de notificaciones", "tramites@andino.co"], ["organismo", "Organismo por defecto", "", ["Secretaría de Movilidad de Medellín", "Tránsito de Envigado"]], ["red", "Pertenece a la red", "", ["Red Movilidad Antioquia", "Ninguna"]]], "Configuración guardada"); },
    "config-organismo": function () { return vConfigForm([["modo", "Modo de radicación", "", ["FLIT", "Quipux"]], ["ventana", "Días para pedir revocatoria", "5"], ["correo", "Correo del organismo", "tramites@medellin.gov.co"], ["horario", "Horario de atención", "Lunes a viernes, 7:00 a. m. a 5:00 p. m."]], "Configuración guardada"); } };
  function vModulo(id, q) {
    var m = D.MODULOS[id];
    if (m.render) return cabecera({ il: m.il, ante: m.ante, titulo: m.titulo, texto: m.texto }) + RENDERS[m.render](m);
    if (m.pestanas) {
      var p = m.pestanas.filter(function (x) { return x[0] === q.tab; })[0] || m.pestanas[0];
      var spec = p[2];
      spec.titulo = p[1];
      var acciones = spec.acciones || [];
      var cuerpo = spec.render ? RENDERS[spec.render](spec) : lista(spec, id, q, id + ":" + p[0]);
      return cabecera({ il: m.il, ante: m.ante, titulo: m.titulo, texto: m.texto, volver: m.volver, acciones: acciones }) + pestanas({ id: id, titulo: m.titulo }, m.pestanas, p[0]) + cuerpo;
    }
    m.subtitulo = m.subtitulo || "Listado";
    return cabecera({ il: m.il, ante: m.ante, titulo: m.titulo, texto: m.texto, acciones: m.acciones }) + lista(m, id, q, id);
  }
  function specDe(clave) {
    var p = clave.split(":"), m = p[0] === "tramites" ? null : D.MODULOS[p[0]];
    if (!m) return null;
    return p[1] ? m.pestanas.filter(function (x) { return x[0] === p[1]; })[0][2] : m;
  }

  // ---------- Enrutador ----------
  function leerRuta() {
    var h = decodeURIComponent(location.hash.replace(/^#/, "")) || "inicio";
    var partes = h.split("?"), q = {};
    (partes[1] || "").split("&").forEach(function (kv) { if (kv) { var p = kv.split("="); q[p[0]] = p[1] || ""; } });
    var id = PROPIAS[partes[0]] || D.MODULOS[partes[0]] ? partes[0] : "inicio";
    return { id: id, q: q };
  }
  var ruta = leerRuta();
  // Placa colombiana: abajo va el municipio donde está matriculado el vehículo. Con datos reales sale del
  // organismo de tránsito del trámite; con los de muestra, del organismo del trámite o de la flota de muestra.
  var MUNICIPIOS = ["MEDELLÍN", "ENVIGADO", "SABANETA", "ITAGÜÍ", "BOGOTÁ D.C.", "RIONEGRO"];
  function municipioDe(placa) {
    var limpia = placa.replace(/\s/g, "").toUpperCase();
    var real = Object.keys(CACHE_TRAMITES).map(function (k) { return CACHE_TRAMITES[k]; }).filter(function (t) { return (t.placa || "").toUpperCase() === limpia; })[0];
    if (real) { var o = real.organismoTransitoNombre || real.organismoNombre || ""; return o ? o.split(" de ").pop().toUpperCase() : ""; }
    if (MODO === "real") return "";
    var m = D.TRAMITES.filter(function (t) { return t.placa === limpia; })[0];
    if (m && m.organismo) return m.organismo.toUpperCase();
    var h = 0; for (var i = 0; i < limpia.length; i++) h = (h * 31 + limpia.charCodeAt(i)) % 997;
    return MUNICIPIOS[h % MUNICIPIOS.length];
  }
  function ciudadesDePlaca(raiz) {
    $$(".placa", raiz).forEach(function (el) {
      if (el.querySelector(".placa__num")) return;
      var txt = el.textContent.trim(); if (!/^[A-Z]{3}\s?\d{2}[0-9A-Z]$/i.test(txt)) return;
      var c = municipioDe(txt);
      el.innerHTML = '<span class="placa__num">' + esc(txt) + "</span>" + (c ? '<span class="placa__ciudad">' + esc(c) + "</span>" : "");
    });
  }
  // Las vistas con datos reales pintan después de la consulta: se completan las placas cuando aparecen.
  new MutationObserver(function () { ciudadesDePlaca(document.body); }).observe(document.body, { childList: true, subtree: true });
  function pintar() {
    ruta = leerRuta();
    // Con la API real, sin sesión solo se ve el ingreso.
    if (MODO === "real" && !SESION && ruta.id !== "ingreso") { history.replaceState(null, "", "#ingreso"); ruta = leerRuta(); }
    var r = ruta, main = $("#contenido"), ingreso = r.id === "ingreso";
    $("#barra").hidden = ingreso; $("#dock").hidden = ingreso;
    main.classList.toggle("main--completo", ingreso);
    var propia = (MODO === "real" ? { inicio: vInicioReal, tramites: vTramitesReal, tramite: vTramiteReal } : {})[r.id] ||
      { inicio: vInicio, tramites: vTramites, traspaso: vTraspaso, tramite: vTramite, reportes: vReportes, marca: vMarca, ingreso: vIngreso, ayuda: vAyuda, perfil: vPerfil }[r.id];
    main.innerHTML = '<div class="vista">' + (propia ? propia(r.q) : vModulo(r.id, r.q)) + "</div>";
    document.title = titulo(r.id) + " · FLIT 2.0";
    var mod = D.MODULOS[r.id];
    $("#producto-actual").textContent = mod && mod.producto ? mod.producto : "Trámites";
    marcarDock(r.id);
    cerrarMenus();
    enlazar();
  }
  function irA(hash, conservarFoco) {
    if (location.hash !== hash) history.pushState(null, "", hash);
    pintar();
    if (!conservarFoco) { $("#contenido").focus({ preventScroll: true }); window.scrollTo(0, 0); }
  }
  function conParams(cambios) {
    var q = Object.assign({}, ruta.q, cambios), partes = [];
    if (!("pag" in cambios)) delete q.pag; // filtrar, buscar o cambiar de estado vuelve a la primera página
    Object.keys(q).forEach(function (k) { if (q[k]) partes.push(k + "=" + encodeURIComponent(q[k])); });
    return "#" + ruta.id + (partes.length ? "?" + partes.join("&") : "");
  }

  // ---------- Menú inferior, productos y usuario ----------
  function pintarDock() {
    var grupo = function (g, i, lado) {
      var id = "menu-" + lado + i;
      if (g.href) return '<a class="dock__item" href="#' + g.href + '" data-grupo="' + g.href + '">' + ilus(g.il) + "<span>" + g.t + "</span></a>";
      // Cada entrada como en el lanzador: ilustración, nombre y para qué sirve
      var entrada = function (it) {
        var m = D.MODULOS[it[0]], d = DESCRIPCION[it[0]] || "";
        return '<li><a class="dock__entrada" href="#' + it[0] + '">' + ilus(m ? m.il : (PROPIAS[it[0]] || [0, g.il])[1]) + '<span><strong>' + it[1] + "</strong>" + (d ? "<small>" + d + "</small>" : "") + "</span></a></li>";
      };
      var cabezaMenu = '<div class="dock__cabeza">' + ilus(g.il) + "<div><strong>" + g.t + "</strong><small>" + (g.items || g.secciones.reduce(function (a, s) { return a.concat(s[1]); }, [])).length + " módulos</small></div></div>";
      var items = cabezaMenu + (g.items ? '<ul>' + g.items.map(entrada).join("") + "</ul>"
        : '<div class="dock__secciones">' + g.secciones.map(function (s) { return '<div class="dock__seccion"><p>' + s[0] + "</p><ul>" + s[1].map(entrada).join("") + "</ul></div>"; }).join("") + "</div>");
      var hijos = (g.items || g.secciones.reduce(function (a, s) { return a.concat(s[1]); }, [])).map(function (it) { return it[0]; }).join(" ");
      return '<div class="dock__grupo"><button class="dock__item" type="button" aria-expanded="false" aria-controls="' + id + '" data-hijos="' + hijos + '">' + ilus(g.il) + "<span>" + g.t + '</span><svg class="i dock__flecha" aria-hidden="true"><use href="#i-arriba"/></svg></button><div class="dock__menu' + (g.secciones ? " dock__menu--ancho" : "") + '" id="' + id + '" hidden>' + items + "</div></div>";
    };
    $("#dock").innerHTML = D.DOCK.izq.map(function (g, i) { return grupo(g, i, "i"); }).join("") +
      '<a class="dock__fab" href="#inicio" data-grupo="inicio" aria-label="Inicio">' + '<img src="assets/kit/isotipo.svg" alt="" aria-hidden="true"></a>' +
      D.DOCK.der.map(function (g, i) { return grupo(g, i, "d"); }).join("");
  }
  // UI Kit 2026, «Menús» (menú contextual): aparece junto a la fila; se cierra al elegir, al pulsar fuera o con Escape.
  var menuAcc = null, botonAcc = null;
  function cerrarAcciones(devolverFoco) {
    if (!menuAcc) return;
    menuAcc.parentNode.removeChild(menuAcc); menuAcc = null; botonAcc.setAttribute("aria-expanded", "false");
    if (devolverFoco && document.contains(botonAcc)) botonAcc.focus();
    botonAcc = null;
  }
  function abrirAcciones(b) {
    var valor = b.getAttribute("data-acciones"), p = valor.split(":"), i = +p.pop(), spec = specDe(p.join(":")), ruta = b.getAttribute("data-abrir-ruta");
    var fila = spec ? spec.filas[i] : null, primero = fila ? (Array.isArray(fila[0]) ? fila[0][0] : fila[0]) : "registro";
    var items = [ruta ? '<a role="menuitem" href="' + ruta + '">Abrir</a>' : '<button role="menuitem" type="button" data-hacer="detalle:' + valor + '">Ver</button>'];
    if (spec && spec.fila) items.push('<button role="menuitem" type="button" data-hacer="aviso:' + esc(spec.fila) + ' listo">' + spec.fila + "</button>");
    else if (!ruta) items.push('<button role="menuitem" type="button" data-hacer="detalle:' + valor + '">Editar</button>');
    items.push('<button role="menuitem" type="button" data-hacer="aviso:Descarga lista: ' + esc(String(primero)) + '.pdf">Descargar</button>');
    menuAcc = document.createElement("div"); menuAcc.className = "menu-flotante menu-acciones"; menuAcc.setAttribute("role", "menu");
    menuAcc.innerHTML = "<ul>" + items.map(function (x) { return "<li>" + x + "</li>"; }).join("") + "</ul>";
    document.body.appendChild(menuAcc); botonAcc = b; b.setAttribute("aria-expanded", "true");
    var r = b.getBoundingClientRect(), w = menuAcc.offsetWidth, h = menuAcc.offsetHeight;
    var x = Math.min(Math.max(8, r.right - w), window.innerWidth - w - 8), y = r.bottom + 6;
    if (y + h > window.innerHeight - 8) y = Math.max(8, r.top - h - 6);
    menuAcc.style.left = x + "px"; menuAcc.style.top = y + "px";
    menuAcc.querySelector("a, button").focus();
  }
  document.addEventListener("click", function (ev) {
    var b = ev.target.closest("[data-acciones]");
    if (b) { var mismo = b === botonAcc; cerrarAcciones(); if (!mismo) abrirAcciones(b); return; }
    if (menuAcc) setTimeout(function () { cerrarAcciones(); }, 0);
  });
  document.addEventListener("keydown", function (ev) {
    if (!menuAcc) return;
    if (ev.key === "Escape") { ev.stopPropagation(); cerrarAcciones(true); return; }
    if (ev.key === "ArrowDown" || ev.key === "ArrowUp") {
      var its = $$("a, button", menuAcc), k = its.indexOf(document.activeElement) + (ev.key === "ArrowDown" ? 1 : -1);
      its[(k + its.length) % its.length].focus(); ev.preventDefault();
    }
  }, true);
  window.addEventListener("scroll", function () { cerrarAcciones(); }, { passive: true });
  window.addEventListener("hashchange", function () { cerrarAcciones(); });
  function marcarDock(id) {
    var actual = id === "traspaso" || id === "tramite" ? "tramites" : id === "compania" ? "companias" : id === "organismo" ? "organismos" : id;
    $$("#dock .dock__item, #dock .dock__fab").forEach(function (el) {
      var activo = el.getAttribute("data-grupo") === actual || (" " + (el.getAttribute("data-hijos") || "") + " ").indexOf(" " + actual + " ") >= 0;
      if (activo) el.setAttribute("aria-current", "page"); else el.removeAttribute("aria-current");
    });
    $$("#dock .dock__menu a").forEach(function (a) { if (a.getAttribute("href") === "#" + actual) a.setAttribute("aria-current", "page"); else a.removeAttribute("aria-current"); });
  }
  function cerrarMenus(excepto) {
    $$("button[aria-expanded='true'][aria-controls]:not(#filtros-boton):not(.acordeon__fila)").forEach(function (b) {
      if (b === excepto) return;
      b.setAttribute("aria-expanded", "false"); var m = document.getElementById(b.getAttribute("aria-controls")); if (m) m.hidden = true;
    });
  }
  // Acordeón de grupo: uno abierto a la vez; se guarda en la dirección sin volver a pintar.
  document.addEventListener("click", function (ev) {
    var f = ev.target.closest(".acordeon__fila"); if (!f) return;
    var abrir = f.getAttribute("aria-expanded") !== "true";
    $$(".acordeon__fila").forEach(function (x) { var on = abrir && x === f; x.setAttribute("aria-expanded", String(on)); document.getElementById(x.getAttribute("aria-controls")).hidden = !on; });
    history.replaceState(null, "", conParams({ grupo: abrir ? f.getAttribute("data-grupo") : "" }));
    ruta.q.grupo = abrir ? f.getAttribute("data-grupo") : "";
  });
  document.addEventListener("click", function (ev) {
    var b = ev.target.closest("button[aria-controls][aria-expanded]:not(#filtros-boton):not(.acordeon__fila)");
    if (b) {
      var abierto = b.getAttribute("aria-expanded") === "true";
      cerrarMenus(b);
      b.setAttribute("aria-expanded", String(!abierto));
      var m = document.getElementById(b.getAttribute("aria-controls")); m.hidden = abierto;
      if (!abierto) { var primero = m.querySelector("a, button"); if (primero) primero.focus(); }
      return;
    }
    if (!ev.target.closest(".dock__menu, .menu-flotante")) cerrarMenus();
  });
  document.addEventListener("keydown", function (ev) {
    if (ev.key !== "Escape") return;
    var abierto = $("button[aria-controls][aria-expanded='true']:not(#filtros-boton):not(.acordeon__fila)");
    if (abierto && !document.querySelector("dialog[open]")) { cerrarMenus(); abierto.focus(); }
  });

  // ---------- Acciones ----------
  function aviso(t) { var a = $("#aviso"); a.textContent = t; a.hidden = false; clearTimeout(aviso.t); aviso.t = setTimeout(function () { a.hidden = true; }, 3600); }
  function cargando(btn, ms, fin) {
    var txt = btn.innerHTML; btn.setAttribute("aria-busy", "true"); btn.disabled = true; btn.textContent = "Procesando";
    setTimeout(function () { btn.removeAttribute("aria-busy"); btn.disabled = false; btn.innerHTML = txt; fin(); }, ms);
  }
  function hacer(accion, btn) {
    var tipo = accion.split(":")[0], valor = accion.slice(tipo.length + 1);
    if (tipo === "form") return abrirForm(valor, btn);
    if (tipo === "abrir") return abrir(valor, btn);
    if (tipo === "aviso") return cargando(btn, 800, function () { aviso(valor + "."); });
    if (tipo === "descargar") return cargando(btn, 900, function () { aviso("Descarga lista: " + valor + "."); });
    if (tipo === "detalle") return abrirDetalle(valor, btn);
    if (tipo === "abrir-flit") return window.open(urlFlit() + "/tramites/" + encodeURIComponent(valor), "_blank", "noopener");
  }
  document.addEventListener("click", function (ev) {
    var a = ev.target.closest("[data-hacer]"); if (a) { ev.preventDefault(); hacer(a.getAttribute("data-hacer"), a); return; }
    var d = ev.target.closest("[data-abrir]"); if (d) { abrir(d.getAttribute("data-abrir"), d); return; }
    var c = ev.target.closest("[data-cerrar]"); if (c) { c.closest("dialog").close(); return; }
    var s = ev.target.closest("[data-switch]"); if (s) { var on = s.getAttribute("aria-checked") !== "true"; s.setAttribute("aria-checked", String(on)); aviso((on ? "Activado: " : "Desactivado: ") + s.getAttribute("aria-label").toLowerCase() + "."); return; }
    var r = ev.target.closest("[data-rol]"); if (r) { $$("[data-rol]").forEach(function (x) { x.setAttribute("aria-pressed", String(x === r)); }); var t = $(".rbac .card:last-child h2"); if (t) t.textContent = "Permisos del rol " + r.querySelector("strong").textContent; return; }
    var u = ev.target.closest("[data-cargar]"); if (u) { u.classList.add("carga--lista"); u.innerHTML = ilus("documentos") + "<strong>Archivo cargado</strong><small>Pulsa para reemplazarlo</small>"; return; }
    var pg = ev.target.closest("[data-pag]"); if (pg) { irA(conParams({ pag: pg.getAttribute("data-pag") === "1" ? "" : pg.getAttribute("data-pag") }), true); var t = $(".tabla-wrap"); if (t) t.closest(".card").scrollIntoView({ block: "start" }); var ac = $('.paginador__num[aria-current="page"]'); if (ac) ac.focus(); return; }
    var l = ev.target.closest("[data-limpiar]"); if (l) { FILTROS_ABIERTOS = false; irA("#" + ruta.id + (ruta.q.tab ? "?tab=" + ruta.q.tab : ""), true); return; }
    var qt = ev.target.closest("[data-quitar]"); if (qt) { var cam = {}; qt.getAttribute("data-quitar").split(",").forEach(function (k) { cam[k] = ""; }); irA(conParams(cam), true); var nb = $("#filtros-boton"); if (nb) nb.focus(); return; }
    if (ev.target.closest("[data-reintentar]")) { pintar(); return; }
    if (ev.target.closest("[data-salir]") && MODO === "real") {
      ev.preventDefault();
      fetch("/local/salir", { method: "POST", credentials: "same-origin" }).finally(function () { SESION = null; CACHE_TRAMITES = {}; irA("#ingreso"); });
    }
  });

  function enlazarFiltros() {
    $$("[data-vista]:not([data-enlazado])").forEach(function (b) {
      b.setAttribute("data-enlazado", "");
      b.addEventListener("click", function () {
        var v = b.getAttribute("data-vista");
        if (v && v === ruta.q.vista) v = ""; // pulsado otra vez quita el filtro
        irA(conParams({ vista: v }), true);
        var otro = $('[data-vista="' + v + '"]'); if (otro) otro.focus();
      });
    });
  }
  function actualizarCampana(n) {
    var c = $('a.icono-btn[href^="#tramites"]'); if (!c) return;
    c.setAttribute("href", "#tramites?vista=subsanacion");
    c.setAttribute("aria-label", n ? n + (n === 1 ? " trámite en subsanación" : " trámites en subsanación") : "Sin trámites en subsanación");
    var p = c.querySelector(".punto"); p.textContent = n; p.hidden = !n;
  }
  function urlFlit() { return AMBIENTE === "QA" ? "https://qa.flitsas.online" : "https://app.flitsas.com"; }
  var ROLES = { SuperAdmin: "Super Admin", AdminCompany: "Admin de compañía", ot_admin: "Admin del organismo" };
  function actualizarUsuario() {
    var u = SESION || {}, nombre = u.nombre || u.correo || "Usuario";
    var ini = nombre.split(/[\s@.]+/).filter(Boolean).slice(0, 2).map(function (x) { return x[0].toUpperCase(); }).join("");
    $(".usuario__avatar").textContent = ini;
    $(".usuario__texto strong").textContent = nombre;
    $(".usuario__texto small").textContent = [ROLES[u.rol] || u.rol, u.compania].filter(Boolean).join(" · ") || u.correo || "";
    $(".usuario").setAttribute("aria-label", "Menú de " + nombre);
    $(".menu-usuario__avatar").textContent = ini; $(".menu-usuario__nombre").textContent = nombre; $(".menu-usuario__rol").textContent = $(".usuario__texto small").textContent;
    var a = $("#ambiente"); a.hidden = false; a.textContent = AMBIENTE + " · solo lectura";
  }

  function enlazar() {
    enlazarFiltros();
    enlazarPanelFiltros();
    var f = $("#filtro-q");
    if (f) f.addEventListener("input", function () {
      // Con datos reales cada letra sería una consulta: se espera a que la persona deje de escribir.
      clearTimeout(enlazar.t);
      enlazar.t = setTimeout(function () {
        var pos = f.selectionStart;
        history.replaceState(null, "", conParams({ q: f.value }));
        pintar(); var n = $("#filtro-q"); n.focus(); n.setSelectionRange(pos, pos);
      }, MODO === "real" ? 400 : 0);
    });
    $$("[data-paso]").forEach(function (b) { b.addEventListener("click", function () { irA("#traspaso?paso=" + b.getAttribute("data-paso"), true); }); });
    $$("[data-form-simple]").forEach(function (fm) { fm.addEventListener("submit", function (ev) { ev.preventDefault(); var b = fm.querySelector("[type=submit]"); cargando(b, 800, function () { aviso(fm.getAttribute("data-form-simple") + "."); }); }); });
    $$("[data-aviso-cambio]").forEach(function (s) { s.addEventListener("change", function () { aviso(s.getAttribute("data-aviso-cambio") + "."); }); });
    var env = $("#enviar");
    if (env) env.addEventListener("click", function () {
      var c = $("#c-correo"), e = $("#c-correo-error");
      if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(c.value)) { e.textContent = "Falta el correo del comprador. Escríbelo para enviarle la validación de identidad."; e.hidden = false; c.setAttribute("aria-invalid", "true"); c.focus(); return; }
      e.hidden = true; c.removeAttribute("aria-invalid"); cargando(env, 1000, function () { aviso("Validación enviada a " + c.value + "."); });
    });
    var fi = $("#form-ingreso");
    if (fi) {
      fi.addEventListener("submit", function (ev) {
        ev.preventDefault(); var c = $("#ing-correo"), k = $("#i-clave"), e = $("#i-error"), b = $("#i-entrar");
        var falla = function (t, campo) { e.textContent = t; e.hidden = false; if (campo) { campo.setAttribute("aria-invalid", "true"); campo.focus(); } };
        c.removeAttribute("aria-invalid"); k.removeAttribute("aria-invalid"); e.hidden = true;
        if (!c.value.trim()) return falla("Escribe tu correo para ingresar.", c);
        if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(c.value.trim())) return falla("Ese correo no es válido. Revisa que tenga @ y un dominio.", c);
        if (!k.value) return falla("Escribe tu contraseña para ingresar.", k);
        if ($("#i-recordar").checked) almacen.setItem("flit-correo", c.value.trim()); else almacen.removeItem("flit-correo");
        if (MODO !== "real") return irA("#inicio");
        b.disabled = true; b.setAttribute("aria-busy", "true"); b.innerHTML = '<span class="girador girador--claro" aria-hidden="true"></span>Ingresando';
        fetch("/local/ingresar", { method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ correo: c.value.trim(), clave: k.value }) })
          .then(function (r) { return r.json().then(function (d) { return { ok: r.ok, d: d }; }); })
          .then(function (x) {
            if (!x.ok) { b.disabled = false; b.removeAttribute("aria-busy"); b.textContent = "Ingresar"; k.value = ""; return falla(x.d.error || "No pudimos ingresar. Intenta de nuevo.", k); }
            SESION = x.d; actualizarUsuario(); irA("#inicio");
          })
          .catch(function () { b.disabled = false; b.removeAttribute("aria-busy"); b.textContent = "Ingresar"; falla("No pudimos hablar con FLIT. Revisa tu conexión y vuelve a intentar.", null); });
      });
      $("#ver-clave").addEventListener("click", function () { var v = this, on = v.getAttribute("aria-pressed") !== "true"; v.setAttribute("aria-pressed", String(on)); v.setAttribute("aria-label", on ? "Ocultar contraseña" : "Mostrar contraseña"); $("#i-clave").type = on ? "text" : "password"; });
      $("#olvide").addEventListener("click", function () {
        if (MODO === "real") window.open(urlFlit() + "/auth/forgot-password", "_blank", "noopener");
        else aviso("En FLIT este botón abre la recuperación de contraseña.");
      });
    }
  }

  // ---------- Diálogos ----------
  var origen = null;
  function abrir(id, desde) {
    var d = document.getElementById(id); if (!d || d.open) return;
    cerrarMenus(); origen = desde || document.activeElement; d.showModal();
    if (id === "buscador") { $("#buscador-q").value = ""; pintarResultados(""); $("#buscador-q").focus(); }
    if (id === "consulta") { $("#consulta-resultado").innerHTML = ""; $("#placa-error").hidden = true; $("#placa").focus(); }
  }
  $$("dialog").forEach(function (d) {
    d.addEventListener("close", function () { if (origen && document.contains(origen)) origen.focus(); origen = null; });
    d.addEventListener("click", function (ev) { if (ev.target === d) d.close(); });
  });
  document.addEventListener("keydown", function (ev) { if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === "k") { ev.preventDefault(); abrir("buscador"); } });

  function campoHtml(c) {
    var id = "f-" + c[0], req = c[4] ? " required aria-required=\"true\"" : "", lab = '<label for="' + id + '">' + c[1] + (c[4] ? "" : ' <small>(opcional)</small>') + "</label>";
    var err = '<p class="error" id="' + id + '-error" role="alert" hidden></p>';
    if (c[2] === "tarjetas") return '<fieldset class="campo campo--ancho"><legend class="etiqueta">' + c[1] + '</legend><div class="tarjetas">' + c[3].map(function (o, i) {
      return '<label class="tarjeta"><input type="radio" name="' + id + '" value="' + o[0] + '"' + (i === 0 ? " checked" : "") + '><span class="tarjeta__marca" aria-hidden="true"><svg class="i"><use href="#i-check"/></svg></span>' + (o[3] ? ilus(o[3], "tarjeta__ilus") : "") + "<span class=\"tarjeta__texto\"><strong>" + o[1] + "</strong><small>" + o[2] + "</small></span></label>"; }).join("") + "</div></fieldset>";
    if (c[2] === "select") return '<div class="campo">' + lab + '<select class="control" id="' + id + '">' + c[3].map(function (o) { return "<option>" + o + "</option>"; }).join("") + "</select></div>";
    if (c[2] === "area") return '<div class="campo campo--ancho">' + lab + '<textarea class="control" id="' + id + '" rows="3" aria-describedby="' + id + '-error"' + req + "></textarea>" + err + "</div>";
    if (c[2] === "archivo") return '<div class="campo campo--ancho"><span class="etiqueta">' + c[1] + '</span><button class="carga" type="button" data-cargar>' + ilus("documentos") + "<strong>Cargar el archivo</strong><small>" + c[3] + "</small></button></div>";
    var tipo = { correo: "email", fecha: "date", clave: "password" }[c[2]] || "text";
    return '<div class="campo">' + lab + '<input class="control" id="' + id + '" type="' + tipo + '" placeholder="' + esc(c[3] || "") + '" aria-describedby="' + id + '-error"' + req + ">" + err + "</div>";
  }
  function abrirForm(fid, btn) {
    var f = D.FORMS[fid], d = $("#formulario");
    d.classList.toggle("modal--ancho", !!f.ancho);
    $("#formulario-titulo").textContent = f.t;
    $("#formulario-ilus").innerHTML = ilus(f.il);
    $("#formulario-cuerpo").innerHTML = (f.texto ? "<p>" + f.texto + "</p>" : "") + '<div class="formulario">' + f.campos.map(campoHtml).join("") + "</div>" +
      '<div class="pie-acciones"><button class="btn ghost" type="button" data-cerrar>Cancelar</button><button class="btn" type="submit">' + f.boton + "</button></div>";
    $("#formulario-cuerpo").dataset.form = fid;
    abrir("formulario", btn);
    var primero = $("#formulario-cuerpo input:not([type=radio]), #formulario-cuerpo input[type=radio]:checked, #formulario-cuerpo select"); if (primero) primero.focus();
  }
  $("#formulario-cuerpo").addEventListener("submit", function (ev) {
    ev.preventDefault();
    var f = D.FORMS[this.dataset.form], primero = null;
    $$("[required]", this).forEach(function (c) {
      var e = document.getElementById(c.id + "-error"), mal = !c.value.trim() || (c.type === "email" && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(c.value));
      e.hidden = !mal; c.toggleAttribute("aria-invalid", mal);
      if (mal) { e.textContent = c.type === "email" && c.value ? "Ese correo no es válido. Revisa que tenga @ y un dominio." : "Este dato es obligatorio."; primero = primero || c; }
    });
    if (primero) { primero.focus(); return; }
    var b = this.querySelector("[type=submit]");
    cargando(b, 800, function () { $("#formulario").close(); if (f.ir) irA(f.ir); else aviso(f.exito + "."); });
  });

  // Detalle de un registro: arriba la identidad (iniciales o ilustración, nombre y dato secundario, estado a la
  // derecha) y debajo los demás datos en recuadros cielo, como en los correos de la casa.
  function abrirDetalle(valor, btn) {
    var p = valor.split(":"), i = +p.pop(), clave = p.join(":"), spec = specDe(clave);
    if (!spec) return;
    var mod = D.MODULOS[p[0]] || {}, fila = spec.filas[i], tipos = spec.columnas.map(function (c) { return c[1]; });
    var iEst = tipos.indexOf("estado"), t0 = tipos[0], v0 = fila[0];
    var nombre = Array.isArray(v0) ? v0[0] : t0 === "placa" ? placaTxt(v0) : String(v0);
    var segundo = Array.isArray(v0) ? v0[1] : "";
    var iniciales = nombre.split(/\s+/).filter(function (x) { return /^[A-ZÁÉÍÓÚÑ]/i.test(x); }).slice(0, 2).map(function (x) { return x.charAt(0).toUpperCase(); }).join("");
    var marca = t0 === "personas" ? '<span class="detalle__avatar" aria-hidden="true">' + esc(iniciales) + "</span>" : t0 === "placa" ? celda("placa", v0, spec) : ilus(spec.il || mod.il || "trazabilidad", "detalle__ilus");
    $("#detalle-ante").textContent = spec.titulo && spec.titulo !== mod.titulo ? mod.titulo + " · " + spec.titulo : (mod.titulo || spec.titulo || "Detalle");
    $("#detalle-titulo").textContent = nombre;
    var fichas = spec.columnas.map(function (c, j) { return j === 0 || j === iEst ? "" : '<div><dt>' + c[0] + "</dt><dd>" + celda(c[1], fila[j], spec) + "</dd></div>"; }).join("");
    $("#detalle-cuerpo").innerHTML = '<div class="detalle__resumen">' + marca + '<div class="detalle__quien">' + (t0 === "placa" ? "<strong>Vehículo</strong>" : "<strong>" + esc(nombre) + "</strong>") + (segundo ? "<small>" + esc(segundo) + "</small>" : "") + "</div>" +
      (iEst >= 0 ? badge(spec.estados, fila[iEst]) : "") + "</div>" +
      (fichas ? '<dl class="dl detalle__fichas">' + fichas + "</dl>" : "") +
      '<div class="pie-acciones"><button class="btn ghost" type="button" data-cerrar>Cerrar</button><button class="btn" type="button" data-hacer="aviso:' + (spec.fila || "Cambios guardados") + ' listo">' + (spec.fila || "Editar") + "</button></div>";
    abrir("detalle", btn);
    var h = $("#detalle-titulo"); h.focus(); // foco inicial en el título: el lector lo anuncia y no se enciende el anillo del botón cerrar
  }


  // Buscador: todas las secciones, acciones y placas
  var ENTRADAS = [];
  D.DOCK.izq.concat(D.DOCK.der).forEach(function (g) {
    var items = g.href ? [[g.href, g.t]] : g.items || g.secciones.reduce(function (a, s) { return a.concat(s[1]); }, []);
    items.forEach(function (it) { var m = D.MODULOS[it[0]]; ENTRADAS.push({ g: "Módulos", t: it[1], d: g.t, il: m ? m.il : g.il, h: "#" + it[0] }); });
  });
  ENTRADAS = [{ g: "Módulos", t: "Inicio", d: "Resumen de la operación", il: "inicio", h: "#inicio" }].concat(ENTRADAS, [
    { g: "Productos", t: "Comparendos", d: "Los comparendos de tu flota", il: "comparendos", h: "#comparendos" },
    { g: "Productos", t: "Diagnóstico", d: "Estado legal y documental", il: "diagnostico", h: "#diagnostico" },
    { g: "Acciones", t: "Nuevo trámite", d: "Traspaso o matrícula inicial", il: "traspaso", hacer: "form:nuevo-tramite" },
    { g: "Acciones", t: "Consultar placa", d: "Estado del vehículo en línea", il: "consulta-placa", abrir: "consulta" },
    { g: "Acciones", t: "Invitar colaborador", d: "Usuarios", il: "colaboradores", hacer: "form:invitar-usuario" },
    { g: "Acciones", t: "Trámites que esperan firma", d: cuantos("firma") + " trámites", il: "firmas", h: "#tramites?vista=firma" },
    { g: "Cuenta", t: "Mi perfil", d: "Datos y contraseña", il: "perfil", h: "#perfil" },
    { g: "Cuenta", t: "Ayuda", d: "Manual de usuario", il: "ayuda", h: "#ayuda" },
    { g: "Cuenta", t: "Juego de marca", d: "Logos e ilustraciones", il: "configuracion", h: "#marca" }
  ], D.TRAMITES.map(function (t) { return { g: "Placas", t: placaTxt(t.placa), d: t.tipo + " · " + ET[t.estado][0], il: "consulta-placa", h: "#tramites?q=" + t.placa, placa: t.placa }; }));
  var activos = [], sel = 0;
  function pintarResultados(q) {
    var n = norm(q);
    activos = ENTRADAS.filter(function (e) { return n ? norm(e.t + e.d + (e.placa || "")).indexOf(n) >= 0 : e.g === "Acciones" || e.g === "Productos" || ["Inicio", "Trámites", "Reportes", "Usuarios", "Compañías"].indexOf(e.t) >= 0; });
    sel = 0;
    var html = "", g = "";
    activos.forEach(function (e, i) {
      if (e.g !== g) { html += (g ? "</div>" : "") + '<div role="group" aria-labelledby="grp-' + i + '"><p class="grupo" id="grp-' + i + '">' + e.g + "</p>"; g = e.g; }
      html += '<div class="resultado" role="option" id="res-' + i + '" data-i="' + i + '" aria-selected="' + (i === 0) + '">' + ilus(e.il) + "<span><strong>" + e.t + "</strong><small>" + e.d + "</small></span></div>";
    });
    if (g) html += "</div>";
    $("#buscador-lista").innerHTML = html || '<p class="vacio">No encontramos nada con eso. Prueba con una placa completa o el nombre de un módulo.</p>';
    $("#buscador-q").setAttribute("aria-activedescendant", activos.length ? "res-0" : "");
  }
  function elegir(i) {
    var e = activos[i]; if (!e) return;
    origen = null; $("#buscador").close();
    if (e.abrir) abrir(e.abrir); else if (e.hacer) hacer(e.hacer, $(".buscar")); else irA(e.h);
  }
  $("#buscador-q").addEventListener("input", function (ev) { pintarResultados(ev.target.value); });
  $("#buscador-q").addEventListener("keydown", function (ev) {
    if (ev.key === "ArrowDown" || ev.key === "ArrowUp") {
      ev.preventDefault(); if (!activos.length) return;
      sel = (sel + (ev.key === "ArrowDown" ? 1 : activos.length - 1)) % activos.length;
      $$(".resultado").forEach(function (b, i) { b.setAttribute("aria-selected", String(i === sel)); });
      document.getElementById("res-" + sel).scrollIntoView({ block: "nearest" }); ev.target.setAttribute("aria-activedescendant", "res-" + sel);
    }
    if (ev.key === "Enter") { ev.preventDefault(); elegir(sel); }
    if (ev.key === "Escape") { ev.preventDefault(); $("#buscador").close(); }
  });
  $("#buscador-lista").addEventListener("click", function (ev) { var b = ev.target.closest(".resultado"); if (b) elegir(+b.getAttribute("data-i")); });

  // Consulta de placa
  $("#consulta-form").addEventListener("submit", function (ev) {
    ev.preventDefault();
    var p = $("#placa"), e = $("#placa-error"), v = p.value.toUpperCase().replace(/\s/g, "");
    if (!/^[A-Z]{3}(\d{3}|\d{2}[A-Z])$/.test(v)) { e.textContent = "Esa placa no tiene el formato correcto. Revisa que sean tres letras y tres números."; e.hidden = false; p.setAttribute("aria-invalid", "true"); p.focus(); return; }
    e.hidden = true; p.removeAttribute("aria-invalid");
    cargando($("#consulta-btn"), 900, function () {
      $("#consulta-resultado").innerHTML = '<div class="card plano"><div class="vehiculo">' + ilus("traspaso") + '<div class="vehiculo__datos"><h3><span class="placa">' + placaTxt(v) + '</span> Mazda CX-30 2022</h3><div class="vehiculo__estado">' + badge({ a: ["SOAT vigente", "ok"] }, "a") + badge({ a: ["Revisión vigente", "ok"] }, "a") + badge({ a: ["Sin multas", "ok"] }, "a") + "</div></div></div>" +
        '<div class="pie-acciones"><a class="btn secondary" href="#historial-placa?q=' + v + '" data-salir>Ver historial</a><a class="btn" href="#traspaso" data-salir>Iniciar traspaso</a></div></div>';
      $$("[data-salir]").forEach(function (a) { a.addEventListener("click", function () { origen = null; $("#consulta").close(); }); });
    });
  });

  // Tema: estado real del botón y cambio con el sistema mientras no haya elección guardada
  var tema = $("#tema");
  function aplicarTema(t) { document.documentElement.dataset.theme = t; tema.setAttribute("aria-pressed", String(t === "dark")); }
  aplicarTema(document.documentElement.dataset.theme);
  tema.addEventListener("click", function () { var t = document.documentElement.dataset.theme === "dark" ? "light" : "dark"; almacen.setItem("flit-tema", t); aplicarTema(t); });
  matchMedia("(prefers-color-scheme: dark)").addEventListener("change", function (m) { if (!almacen.getItem("flit-tema")) aplicarTema(m.matches ? "dark" : "light"); });

  $("#productos-menu").innerHTML = "<ul>" + D.PRODUCTOS.map(function (p) { return '<li><a href="#' + p[0] + '">' + ilus(p[0] === "inicio" ? "tramites" : p[0]) + '<span><strong>' + p[1] + "</strong><small>" + p[2] + "</small></span></a></li>"; }).join("") + "</ul>";
  pintarDock();
  // UI Kit 2026: íconos fijos del encabezado y color del botón según su función (manual, «Botones y acciones»).
  $$("[data-kico]").forEach(function (el) { el.innerHTML = KI[el.getAttribute("data-kico")] || ""; });
  var RE_POSITIVA = /^(finalizar|confirmar|aprobar|aceptar|completar|firmar|validar|activar|restaurar)/i;
  var RE_ALERTA = /^(cancelar|reenviar|reintentar|descartar|revocar|rechazar|anular|eliminar|salir|suspender|desactivar|vencer|quitar)/i;
  function vestirBotones() {
    $$(".btn:not([data-kit])").forEach(function (b) {
      b.setAttribute("data-kit", ""); if (b.classList.contains("ghost")) return;
      var t = b.textContent.trim();
      if (RE_ALERTA.test(t)) b.classList.add("alerta"); else if (RE_POSITIVA.test(t)) b.classList.add("positiva");
    });
  }
  new MutationObserver(vestirBotones).observe(document.body, { childList: true, subtree: true });
  vestirBotones();
  window.addEventListener("hashchange", pintar);
  window.addEventListener("popstate", pintar);
  // Arranque: si la sirve servidor-local.mjs hay API real; con un servidor de archivos, modo de muestra.
  fetch("/local/sesion", { credentials: "same-origin" }).then(function (r) {
    var tipo = r.headers.get("content-type") || "";
    if (tipo.indexOf("application/json") < 0) return;
    return r.json().then(function (d) {
      MODO = "real"; AMBIENTE = d.ambiente || "";
      if (r.ok) { SESION = d; actualizarUsuario(); }
    });
  }).catch(function () {}).then(pintar);
})();
