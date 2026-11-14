# Multi-stage build: the SDK image compiles, only small runtime images ship.
# Two deployables from one build: `--target worker` for the Worker; the API is the default (last) stage.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LedgerCore.Domain/LedgerCore.Domain.csproj src/LedgerCore.Domain/
COPY src/LedgerCore.Application/LedgerCore.Application.csproj src/LedgerCore.Application/
COPY src/LedgerCore.Infrastructure/LedgerCore.Infrastructure.csproj src/LedgerCore.Infrastructure/
COPY src/LedgerCore.Api/LedgerCore.Api.csproj src/LedgerCore.Api/
COPY src/LedgerCore.Worker/LedgerCore.Worker.csproj src/LedgerCore.Worker/
RUN dotnet restore src/LedgerCore.Api/LedgerCore.Api.csproj \
 && dotnet restore src/LedgerCore.Worker/LedgerCore.Worker.csproj

COPY src/ src/
RUN dotnet publish src/LedgerCore.Api/LedgerCore.Api.csproj -c Release -o /app/api --no-restore \
 && dotnet publish src/LedgerCore.Worker/LedgerCore.Worker.csproj -c Release -o /app/worker --no-restore

# The Worker serves no HTTP, so it runs on the plain .NET runtime image without ASP.NET Core.
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS worker
WORKDIR /app
COPY --from=build /app/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "LedgerCore.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS api
WORKDIR /app
COPY --from=build /app/api .

# The base image ships a non-root user; the API needs no privileges.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LedgerCore.Api.dll"]
