FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Build context is the repository root. Copy project files first so package
# restore remains cached while only source files change.
COPY global.json ./
COPY src/CrmAnalytics.Api/CrmAnalytics.Api.csproj src/CrmAnalytics.Api/
COPY src/CrmAnalytics.Application/CrmAnalytics.Application.csproj src/CrmAnalytics.Application/
COPY src/CrmAnalytics.Contracts/CrmAnalytics.Contracts.csproj src/CrmAnalytics.Contracts/
COPY src/CrmAnalytics.Domain/CrmAnalytics.Domain.csproj src/CrmAnalytics.Domain/
COPY src/CrmAnalytics.Infrastructure/CrmAnalytics.Infrastructure.csproj src/CrmAnalytics.Infrastructure/
COPY src/Crm.Analytics.Sql/Crm.Analytics.Sql.csproj src/Crm.Analytics.Sql/
RUN dotnet restore src/CrmAnalytics.Api/CrmAnalytics.Api.csproj

COPY src/CrmAnalytics.Api/ src/CrmAnalytics.Api/
COPY src/CrmAnalytics.Application/ src/CrmAnalytics.Application/
COPY src/CrmAnalytics.Contracts/ src/CrmAnalytics.Contracts/
COPY src/CrmAnalytics.Domain/ src/CrmAnalytics.Domain/
COPY src/CrmAnalytics.Infrastructure/ src/CrmAnalytics.Infrastructure/
COPY src/Crm.Analytics.Sql/ src/Crm.Analytics.Sql/
RUN dotnet publish src/CrmAnalytics.Api/CrmAnalytics.Api.csproj \
    --configuration Release --no-restore --output /app/publish \
    /p:UseAppHost=false
RUN dotnet tool install --tool-path /tools dotnet-ef --version 10.0.10 && \
    /tools/dotnet-ef migrations bundle \
    --project src/CrmAnalytics.Infrastructure/CrmAnalytics.Infrastructure.csproj \
    --startup-project src/CrmAnalytics.Api/CrmAnalytics.Api.csproj \
    --context CrmAnalyticsDbContext \
    --configuration Release \
    --no-build \
    --output /app/migrations/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /app/publish .
COPY --from=build /app/migrations/efbundle .
USER $APP_UID
ENTRYPOINT ["dotnet", "CrmAnalytics.Api.dll"]
