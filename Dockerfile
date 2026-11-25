# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ShipmentReport/ShipmentReport.csproj ShipmentReport/
RUN dotnet restore ShipmentReport/ShipmentReport.csproj
COPY ShipmentReport/ ShipmentReport/
RUN dotnet publish ShipmentReport/ShipmentReport.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Hosts like Render pass the port in $PORT; 8080 when run locally.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet ShipmentReport.dll"]
