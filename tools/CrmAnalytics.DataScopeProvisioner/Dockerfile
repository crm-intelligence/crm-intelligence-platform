FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY tools/CrmAnalytics.DataScopeProvisioner/CrmAnalytics.DataScopeProvisioner.csproj tools/CrmAnalytics.DataScopeProvisioner/
RUN dotnet restore tools/CrmAnalytics.DataScopeProvisioner/CrmAnalytics.DataScopeProvisioner.csproj \
    --runtime linux-x64

COPY tools/CrmAnalytics.DataScopeProvisioner/ tools/CrmAnalytics.DataScopeProvisioner/
RUN dotnet publish tools/CrmAnalytics.DataScopeProvisioner/CrmAnalytics.DataScopeProvisioner.csproj \
    --configuration Release --no-restore --output /app/publish \
    --runtime linux-x64 --self-contained false /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CrmAnalytics.DataScopeProvisioner.dll"]
