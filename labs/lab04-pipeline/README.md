# Lab 04: Pipeline (OpenShift Pipelines / Tekton)

**Día:** miércoles · **Branch de referencia:** `lab04-start` → `lab04-solution`

## Objetivo

A diferencia de los labs anteriores, **este pipeline ya viene armado**:
la tarea de hoy es dispararlo, leerlo y entender cada paso, no construirlo
desde cero. El objetivo es que el equipo entienda qué automatiza un
pipeline de CI/CD sobre lo que ya hicieron a mano en los labs 01 y 03
(build + push + deploy).

## Contexto

`manifests/pipeline.yaml` define un `Pipeline` de tres pasos:

1. **`fetch-repo`**: clona este repositorio en un workspace compartido
   (tarea `git-clone`).
2. **`build-and-push`**: construye la imagen con `buildah` a partir de
   `app/Dockerfile` (el mismo Dockerfile productivo del
   lab03) y la publica en el `ImageStream` interno.
3. **`deploy`**: actualiza el Deployment `taskflow-api` para que use la
   imagen recién construida y espera a que el rollout termine (tarea
   `openshift-client`).

Las tres `Task` (`git-clone`, `buildah`, `openshift-client`) no viven en
este repo: se resuelven en tiempo de ejecución vía `resolver: cluster`
contra los `Task` que el propio OpenShift Pipelines Operator instala en
el namespace `openshift-pipelines`. Esas tareas están afinadas para el
modelo de seguridad de OpenShift (corren con la SCC restringida del
pipeline, sin `privileged`); el catálogo genérico de Tekton Hub trae su
propia versión de `buildah` que exige `privileged: true` y por eso falla
con `PodAdmissionFailed` en este clúster.

Este pipeline construye desde el código de referencia (mismo resultado
funcional que tu propio lab03), no desde tus cambios locales: el
objetivo es entender la automatización, no reconstruir tu código
personal. Al terminar, tu `Deployment` va a correr la imagen que armó
el pipeline en vez de la que subiste a mano con `podman push`: es
exactamente "lo mismo, pero automatizado", como se vio en la teoría de
hoy.

## Pasos

> **Antes de empezar:** confirma que ya hiciste `oc login` como tu usuario
> (Paso 0 de `labs/README.md`) en esta terminal, `oc whoami` tiene que
> devolver tu usuario, no un `system:serviceaccount:...`. La sesión no
> persiste entre reinicios del workspace.

1. **Leer `manifests/pipeline.yaml` completo** antes de correr nada.
   Identificar: los tres `Task`, el `Workspace` compartido entre ellos, y
   los `params` que recibe el `Pipeline` (`git-url`, `git-revision`,
   `image`).

2. **Confirmar que existe la ServiceAccount `pipeline`** en el namespace
   (la crea automáticamente el OpenShift Pipelines Operator) y darle
   permiso para pushear al registry interno, que no trae por defecto:
   ```bash
   oc get serviceaccount pipeline
   oc apply -f labs/lab04-pipeline/manifests/pipeline-image-builder-rolebinding.yaml
   ```

3. **Instalar `tkn`.** El workspace de Dev Spaces no lo trae preinstalado
   (a diferencia de `oc`/`git`/`podman`): cada clúster de OpenShift
   Pipelines sirve su propio binario, ya logueado con `oc`:
   ```bash
   curl -sL "$(oc get consoleclidownloads tkn -o jsonpath='{.spec.links[*].href}' \
     | tr ' ' '\n' | grep linux-amd64)" | tar -xz -C /usr/local/bin ./tkn
   ```

4. **Registrar el `Pipeline` y dispararlo:**
   ```bash
   oc apply -f labs/lab04-pipeline/manifests/pipeline.yaml
   oc create -f labs/lab04-pipeline/manifests/pipelinerun.yaml
   tkn pipelinerun logs -f -l tekton.dev/pipeline=taskflow-build-deploy
   ```
   o, de forma equivalente, desde la consola web: **Pipelines → Pipelines
   → taskflow-build-deploy → Start**.

5. **Seguir la ejecución en la consola** (pestaña *Pipelines* del
   namespace): observar cómo cada `Task` pasa de `Running` a `Succeeded`,
   y abrir los logs de `build-and-push` para ver el build de `buildah` en
   vivo.

6. **Validar el resultado** igual que en labs anteriores: `oc rollout
   status deployment/taskflow-api` y `curl` contra la Route.

## Criterios de "hecho"

- [ ] El equipo puede explicar, en sus palabras, qué hace cada uno de los
      tres `Task` del pipeline sin mirar la chuleta de este README.
- [ ] El `PipelineRun` termina en `Succeeded`.
- [ ] La imagen desplegada corresponde al commit que se clonó (verificar
      con `oc describe deployment taskflow-api | grep Image`).
- [ ] Se identificó dónde se resuelven `git-clone`/`buildah`/
      `openshift-client` (namespace `openshift-pipelines`, vía
      `resolver: cluster`).

## Pistas

- Si `fetch-repo` falla por permisos, el problema casi siempre es la
  ServiceAccount del `PipelineRun`: confirmar que es `pipeline` y no
  `default`.
- Si `build-and-push` falla en el push con `authentication required`
  (después de haber construido la imagen sin problema), falta aplicar
  `pipeline-image-builder-rolebinding.yaml` del paso 2: la ServiceAccount
  `pipeline` no puede pushear al registry interno hasta tener esa
  ClusterRole.
- `tkn pipelinerun describe <nombre>` da un resumen más legible que
  `oc describe` para pipelines con varios pasos.
- Este pipeline no reemplaza lo aprendido en el lab01/lab03: automatiza
  exactamente esos mismos pasos manuales.
- El paso `build-and-push` tarda varios minutos (el `dotnet restore`/
  `publish` real dentro de `buildah` es más pesado que un build liviano),
  no es que esté colgado, solo es más lento que el `podman build` local
  del lab01/lab03.
