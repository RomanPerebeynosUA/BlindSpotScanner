# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/BlindSpotScanner.Domain/BlindSpotScanner.Domain.csproj src/BlindSpotScanner.Domain/
COPY src/BlindSpotScanner.Application/BlindSpotScanner.Application.csproj src/BlindSpotScanner.Application/
COPY src/BlindSpotScanner.Infrastructure/BlindSpotScanner.Infrastructure.csproj src/BlindSpotScanner.Infrastructure/
COPY src/BlindSpotScanner.Worker/BlindSpotScanner.Worker.csproj src/BlindSpotScanner.Worker/
RUN dotnet restore src/BlindSpotScanner.Worker/BlindSpotScanner.Worker.csproj
COPY src/ src/
RUN dotnet publish src/BlindSpotScanner.Worker/BlindSpotScanner.Worker.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "BlindSpotScanner.Worker.dll"]
