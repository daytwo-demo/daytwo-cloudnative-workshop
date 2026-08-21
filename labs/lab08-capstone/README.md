# Lab 08: Capstone

**Día:** viernes · **Branch de referencia:** `lab08-start` → `lab08-solution` (= `solution`)

## Objetivo

Partiendo de un namespace limpio, desplegar TaskFlow completo integrando
las ocho capas trabajadas durante la semana, sin copiar manifiestos de
labs anteriores uno por uno, sino ensamblando conscientemente cada pieza a
partir de lo aprendido. Este lab no introduce conceptos nuevos: es
integración y validación.

## Contexto

Cada equipo (2-3 personas, según arme el instructor) recibe un namespace
nuevo y vacío, sin nada de lo desplegado en labs anteriores. La consigna
es simple de enunciar y exigente de ejecutar: **dejar corriendo, de una,
la versión completa de TaskFlow**: la misma que resultaría de hacer los
labs 01 a 07 en orden, pero armada de memoria y criterio propio, no
copy-pasteada.

## Namespace limpio

```bash
oc new-project taskflow-capstone-<equipo>
```

(o el namespace que indique el instructor, no reutilizar tu
`dev-<usuario>` de los labs anteriores).

## Checklist de integración

Marcar cada ítem solo cuando esté aplicado **y verificado**, no solo
escrito:

### Configuración (lab02)
- [ ] `ConfigMap` con host/puerto/nombre de base + config de OTel.
- [ ] `Secret` con credenciales de Postgres, generado contra el clúster
      (nunca commiteado).
- [ ] Cero valores hardcodeados en `appsettings.json`.

### Imagen (lab01 + lab03)
- [ ] Dockerfile multi-stage, imagen final `aspnet`, non-root
      (`chgrp 0` + `USER 1654`).
- [ ] Imagen construida y publicada a mano con `podman build`/`push`
      (igual que en lab01/lab03, no vía el pipeline) contra el
      `ImageStream` del namespace nuevo (las imágenes no se comparten
      entre namespaces por defecto).

### Despliegue base (lab01 + lab02)
- [ ] `Deployment` de `taskflow-db` con el Secret/ConfigMap correctos.
- [ ] `Deployment` de `taskflow-api` sin sidecar de Postgres.
- [ ] `Service` + `Route` de `taskflow-api`.

### Salud (lab05)
- [ ] `startupProbe`, `livenessProbe`, `readinessProbe` cableados.
- [ ] Verificado en vivo: escalar `taskflow-db` a 0 no reinicia
      `taskflow-api` (solo lo saca de `Endpoints`).

### Observabilidad (lab06)
- [ ] `ServiceMonitor` aplicado y scrapeando (**Observe → Targets**).
- [ ] Logs del pod son JSON válido.

### Seguridad (lab03 + lab07)
- [ ] Pod corre bajo SCC `restricted-v2` (sin `anyuid`/`privileged`).
- [ ] El pod también pasa Pod Security Admission (`enforce=restricted`
      a nivel de namespace), el equivalente portable de la SCC.
- [ ] `resources.requests/limits` definidos.
- [ ] `NetworkPolicy` de segmentación (solo el router llega a la API, solo
      la API llega a la base).
- [ ] `Role`/`RoleBinding` de mínimo privilegio para la ServiceAccount del
      pipeline.

## Validación final por equipo

Cada equipo hace una demo corta (5-10 min) al resto, mostrando:

1. `GET /api/tasks` respondiendo a través de la Route.
2. Un fallo de readiness provocado en vivo (escalar Postgres a 0) sin que
   el pod de la API se reinicie.
3. Un gráfico de métricas real en la consola con tráfico generado en el
   momento.
4. `oc auth can-i delete deployments --as=system:serviceaccount:<ns>:pipeline`
   respondiendo `no`.

## Criterios de "hecho"

- [ ] Los 17 ítems del checklist de integración están tildados y
      verificados (no solo aplicados).
- [ ] La demo de validación se hizo sin usar `oc apply -f` sobre los
      manifiestos ya resueltos de labs anteriores (`labs/lab0*/manifests`):
      el objetivo es reconstruir, no copiar.
- [ ] El equipo puede explicar, de punta a punta, cada decisión del
      checklist si el instructor pregunta "¿por qué esto así?".
