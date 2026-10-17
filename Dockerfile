# syntax=docker/dockerfile:1

# ============================================================ build / publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (leveraging layer cache). Only the .csproj files are needed here.
COPY Bank.slnx ./
COPY Bank.API/Bank.API.csproj           Bank.API/
COPY Bank.DAL/Bank.DAL.csproj           Bank.DAL/
COPY Bank.Repo/Bank.Repo.csproj         Bank.Repo/
COPY Bank.Services/Bank.Services.csproj Bank.Services/
COPY Bank.Shared/Bank.Shared.csproj     Bank.Shared/
RUN dotnet restore Bank.API/Bank.API.csproj

# Now copy the full source and publish.
COPY . .
RUN dotnet publish Bank.API/Bank.API.csproj \
      -c Release \
      -o /app/publish \
      /p:UseAppHost=false

# ================================================== EF Core migrations bundle
# Produces a self-contained executable that applies the DB migrations.
FROM build AS migrator-build
RUN dotnet tool install --global dotnet-ef --version 10.0.10
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
      --project Bank.DAL/Bank.DAL.csproj \
      --startup-project Bank.API/Bank.API.csproj \
      --context AppDbContext \
      --self-contained \
      -r linux-x64 \
      -o /app/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrator
WORKDIR /app
COPY --from=migrator-build /app/efbundle /app/efbundle
# Run with:  /app/efbundle --connection "Host=db;..."
ENTRYPOINT ["/app/efbundle"]

# ============================================================ runtime (API)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Native deps the app needs on Linux:
#   - libtesseract / libleptonica : OCR (the Tesseract NuGet only ships Windows natives)
#   - libfontconfig / libfreetype  : SkiaSharp (PDFtoImage)
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      libfontconfig1 libfreetype6 libharfbuzz0b libglib2.0-0 \
      tesseract-ocr tesseract-ocr-ara \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bank.API.dll"]
