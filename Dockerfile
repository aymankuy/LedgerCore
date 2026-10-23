# Multi-stage build: the SDK image compiles, only the much smaller ASP.NET runtime image ships.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LedgerCore.Domain/LedgerCore.Domain.csproj src/LedgerCore.Domain/
COPY src/LedgerCore.Application/LedgerCore.Application.csproj src/LedgerCore.Application/
COPY src/LedgerCore.Infrastructure/LedgerCore.Infrastructure.csproj src/LedgerCore.Infrastructure/
COPY src/LedgerCore.Api/LedgerCore.Api.csproj src/LedgerCore.Api/
RUN dotnet restore src/LedgerCore.Api/LedgerCore.Api.csproj

COPY src/ src/
RUN dotnet publish src/LedgerCore.Api/LedgerCore.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The base image ships a non-root user; the API needs no privileges.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LedgerCore.Api.dll"]
