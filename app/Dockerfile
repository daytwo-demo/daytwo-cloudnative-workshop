# SOLUCIÓN (lab03): Dockerfile productivo, multi-stage + non-root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY TaskFlow.Api/*.csproj .
RUN dotnet restore
COPY TaskFlow.Api/. .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# Soporte para UID arbitrario asignado por la SCC restricted-v2 de
# OpenShift: el grupo 0 es predecible aunque el UID no lo sea.
RUN chgrp -R 0 /app && chmod -R g=u /app

# Correr explícitamente como un UID non-root fijo (1654, el mismo que
# trae predefinido la imagen base con el usuario "app" desde .NET 8):
# válido en Kubernetes fuera de OpenShift, y no rompe nada cuando
# OpenShift lo sobreescribe con su propio UID arbitrario.
USER 1654

EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskFlow.Api.dll"]
