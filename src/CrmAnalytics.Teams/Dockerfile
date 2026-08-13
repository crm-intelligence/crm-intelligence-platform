FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Build context is the repository root. Copy project files first so package
# restore remains cached while only source files change.
COPY global.json ./
COPY src/CrmAnalytics.Contracts/CrmAnalytics.Contracts.csproj src/CrmAnalytics.Contracts/
COPY src/CrmAnalytics.Teams/CrmAnalytics.Teams.csproj src/CrmAnalytics.Teams/
RUN dotnet restore src/CrmAnalytics.Teams/CrmAnalytics.Teams.csproj

COPY src/CrmAnalytics.Contracts/ src/CrmAnalytics.Contracts/
COPY src/CrmAnalytics.Teams/ src/CrmAnalytics.Teams/
RUN dotnet publish src/CrmAnalytics.Teams/CrmAnalytics.Teams.csproj \
    --configuration Release --no-restore --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CrmAnalytics.Teams.dll"]
