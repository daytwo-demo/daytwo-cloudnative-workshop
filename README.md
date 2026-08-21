# Workshop Desarrollo Cloud Native sobre Kubernetes/OpenShift

Workshop remoto de 5 días (lunes a viernes, 4h/día) dictado por **DayTwo**
(Red Hat Premier Partner) para equipos de desarrollo. Hasta 15
participantes.

Hilo conductor: una API .NET (**TaskFlow**, en `app/`) que arranca en un
estado deliberadamente "no cloud-native" y se transforma laboratorio a
laboratorio hasta llegar a un despliegue productivo completo: contenedor,
config externalizada, imagen segura, pipeline, health checks,
observabilidad y seguridad.

## Estructura del repositorio

```
daytwo-cloudnative-workshop/
├── app/                         # TaskFlow: API .NET de referencia
└── labs/                        # 8 laboratorios, uno por sesión
```

El clúster OpenShift y el acceso individual por participante los levanta
y opera DayTwo con herramienta propia, separada de este repo: este repo
es exclusivamente el contenido del workshop (app de referencia + labs)
que recibe el participante.

## Versiones de referencia

- **OpenShift Container Platform 4.22** (release par con EUS, soporte
  activo hasta dic-2027, frente a 4.21 que por ser impar solo tiene
  soporte estándar y ya está cerca del fin de su fase de full support).
  Verificar la versión vigente en el [Red Hat OpenShift Container Platform Life Cycle](https://access.redhat.com/support/policy/updates/openshift)
  antes de instalar: `create-cluster.sh` usa el binario `openshift-install`
  que el instructor descargue, no una versión fijada en el repo.
- **.NET 10 (LTS)**, soporte hasta noviembre 2028. Se eligió sobre .NET 8
  (LTS previa, EOL noviembre 2026) porque un workshop pensado para
  reutilizarse necesita margen de vigencia mayor al que le queda a .NET 8.

## Portabilidad: qué es de Kubernetes y qué es de OpenShift

El workshop enseña conceptos portables de Kubernetes como base, y
presenta las capacidades propias de OpenShift como conveniencias
aditivas, identificadas explícitamente en el lab donde aparecen:

| Concepto portable (Kubernetes) | Conveniencia de OpenShift | Lab |
|---|---|---|
| `Deployment`, `Service` | - | lab01 |
| Exponer un `Service` hacia afuera (`Ingress`) | `Route` | lab01 |
| Probes estándar (liveness/readiness/startup) | - | lab05 |
| Pod Security Admission (`restricted`) | SCC (`restricted-v2`) | lab03, lab07 |
| Tekton (`Pipeline`, `Task`, `PipelineRun`) | OpenShift Pipelines (operador + `Task` propias en `openshift-pipelines`) | lab04 |

Ningún lab depende de una capacidad de OpenShift sin dejarlo explícito.

## Orden de uso de punta a punta

1. **Acceso al clúster**: el instructor entrega la URL de la consola y
   credenciales individuales antes de empezar; el clúster es de un solo
   AZ y desechable, no requiere alta disponibilidad, solo sobrevivir la
   semana del workshop.
2. **Dictar los labs**: seguir `labs/README.md`, un lab por sesión según
   la agenda de la semana (lab01–lab02 lunes/martes, lab03–lab05
   miércoles, lab06–lab07 jueves, lab08 viernes).

## Convenciones de este repo

- Scripts en Bash con `set -euo pipefail`, validación de prerequisitos y
  mensajes claros.
- Comentarios en español, terminología técnica en inglés.
- `# TODO` marca dónde trabaja el alumno; `# SOLUCIÓN` marca lo ya resuelto
  como referencia del instructor (ver `labs/README.md` para el esquema
  completo de branches/tags de Git).
- Nada de secretos commiteados, ver `.gitignore`.
