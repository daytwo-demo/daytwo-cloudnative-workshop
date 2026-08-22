# Lab 05: Health checks

**Día:** miércoles · **Branch de referencia:** `lab05-start` → `lab05-solution`

## Objetivo

Cablear los tres probes de Kubernetes (`liveness`, `readiness`,
`startup`) del Deployment `taskflow-api` a los endpoints de salud que la
app ya expone desde el día 1 (`/healthz`, `/readyz`, `/startupz`), y
provocar fallos a propósito para observar cómo reacciona OpenShift en cada
caso.

## Contexto

TaskFlow ya distingue los tres endpoints en el código
(`app/TaskFlow.Api/Program.cs`):

- `/healthz`: solo verifica que el proceso .NET responde. **No** depende
  de Postgres a propósito.
- `/readyz`: sí depende de Postgres (`AddNpgSql`, tag `ready`).
- `/startupz`: igual que `/healthz`, pensado para darle tiempo al proceso
  a inicializar antes de que la liveness probe empiece a contar fallos.

Hasta este lab, ningún manifiesto de Kubernetes usaba estos endpoints:
un pod colgado o sin base de datos no se comportaba distinto de uno sano
a nivel de OpenShift.

## Pasos

> **Antes de empezar:** confirma que ya hiciste `oc login` como tu usuario
> (Paso 0 de `labs/README.md`) en esta terminal, `oc whoami` tiene que
> devolver tu usuario, no un `system:serviceaccount:...`. La sesión no
> persiste entre reinicios del workspace.

1. **Completar `manifests/deployment.yaml`** (tiene `# TODO`) agregando
   `startupProbe`, `livenessProbe` y `readinessProbe`, cada uno apuntando
   a su endpoint correspondiente en el puerto 8080. El campo `image`
   también tiene un `# TODO`: este lab no cambia código de la app, así
   que no hace falta reconstruir nada, referencia la misma imagen
   `taskflow-api:lab04` que ya publicó el pipeline el miércoles.

2. **Aplicar y confirmar que el pod pasa por `Startup` → `Ready`:**
   ```bash
   oc apply -f labs/lab05-health/manifests/
   oc get pods -w
   ```

3. **Provocar un fallo de *readiness* sin afectar *liveness*.** Escalar
   Postgres a 0 réplicas:
   ```bash
   oc scale deployment/taskflow-db --replicas=0
   ```
   Observar con `oc get pods` y `oc get endpoints taskflow-api`: el pod de
   la API **no se reinicia**, pero desaparece de los `Endpoints` del
   Service, deja de recibir tráfico sin que Kubernetes lo mate, porque el
   problema es de una dependencia externa, no del proceso en sí.

4. **Revertir:** `oc scale deployment/taskflow-db --replicas=1` y
   confirmar que el pod de la API vuelve a los `Endpoints` sin haberse
   reiniciado nunca.

5. **Provocar una terminación real del proceso.** Entrar al contenedor y
   matar el proceso principal:
   ```bash
   oc exec deploy/taskflow-api -- kill 1
   ```
   `oc get pods` va a mostrar un `RESTARTS` incrementado, pero la razón es
   distinta a la del paso anterior: `kill` sin señal explícita manda
   `SIGTERM`, que el Generic Host de ASP.NET Core intercepta para hacer un
   *shutdown* ordenado (drena conexiones, sale con código 0). El proceso ya
   no existe cuando terminaría de correr el próximo probe: no es la
   liveness probe la que "detecta" esto, es el `restartPolicy: Always` del
   Deployment reaccionando a que el contenedor terminó de verdad. A
   diferencia del paso 3, acá sí hubo una terminación real del proceso, no
   una condición transitoria detectada por una probe.

## Criterios de "hecho"

- [ ] Los tres probes están definidos y usan los endpoints correctos.
- [ ] Se observó (no solo se leyó) que un fallo de readiness saca el pod
      de `Endpoints` sin reiniciarlo.
- [ ] Se observó que una terminación real del proceso sí reinicia el
      contenedor, a diferencia de un fallo de readiness.
- [ ] El equipo puede explicar por qué `/healthz` deliberadamente no
      depende de la base de datos.

## Pistas

- Si el pod nunca llega a `Ready`, revisar primero el `startupProbe`: un
  `failureThreshold`/`periodSeconds` muy ajustado puede matar al pod antes
  de que Kestrel termine de levantar.
- `oc describe pod <pod>` muestra en `Events` exactamente qué probe
  falló y con qué respuesta.
