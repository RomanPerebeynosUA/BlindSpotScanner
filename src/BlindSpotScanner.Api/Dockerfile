# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/BlindSpotScanner.Domain/BlindSpotScanner.Domain.csproj src/BlindSpotScanner.Domain/
COPY src/BlindSpotScanner.Application/BlindSpotScanner.Application.csproj src/BlindSpotScanner.Application/
COPY src/BlindSpotScanner.Infrastructure/BlindSpotScanner.Infrastructure.csproj src/BlindSpotScanner.Infrastructure/
COPY src/BlindSpotScanner.Api/BlindSpotScanner.Api.csproj src/BlindSpotScanner.Api/
RUN dotnet restore src/BlindSpotScanner.Api/BlindSpotScanner.Api.csproj
# Prompts and the scoring rubric are embedded into Infrastructure at build time (D20).
COPY BlindSpotScanner-Blueprint/prompts/ BlindSpotScanner-Blueprint/prompts/
COPY BlindSpotScanner-Blueprint/docs/06-scoring-rubric.md BlindSpotScanner-Blueprint/docs/
# The profile template is embedded into Application (D22).
COPY BlindSpotScanner-Blueprint/profiles/_template.md BlindSpotScanner-Blueprint/profiles/
COPY src/ src/
RUN dotnet publish src/BlindSpotScanner.Api/BlindSpotScanner.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "BlindSpotScanner.Api.dll"]
