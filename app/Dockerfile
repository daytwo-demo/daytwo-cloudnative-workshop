# SOLUCIÓN (lab01): Dockerfile básico de dos etapas.
# La versión productiva (non-root, rootfs de solo lectura) se aplica en
# labs/lab03-imagen.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY TaskFlow.Api/*.csproj .
RUN dotnet restore
COPY TaskFlow.Api/. .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskFlow.Api.dll"]
