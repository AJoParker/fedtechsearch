FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/FedTechSearch.Ingest/FedTechSearch.Ingest.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
COPY tests/fixtures/ /fixtures/
ENTRYPOINT ["dotnet", "FedTechSearch.Ingest.dll"]
