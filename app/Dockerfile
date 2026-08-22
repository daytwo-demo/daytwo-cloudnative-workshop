# Dockerfile de una sola etapa: compila y corre con la misma imagen (el
# SDK completo). Es la versión "ingenua", a propósito: el lab01 pide
# convertirlo a multi-stage, con el mismo patrón de hello-api/Dockerfile.
# Ver labs/lab01-contenerizar/README.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
COPY TaskFlow.Api/*.csproj .
RUN dotnet restore
COPY TaskFlow.Api/. .
RUN dotnet publish -c Release -o /app/publish --no-restore
WORKDIR /app/publish
EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskFlow.Api.dll"]
