FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Meridian.Domain/Meridian.Domain.csproj src/Meridian.Domain/
COPY src/Meridian.Application/Meridian.Application.csproj src/Meridian.Application/
COPY src/Meridian.Infrastructure/Meridian.Infrastructure.csproj src/Meridian.Infrastructure/
COPY src/Meridian.Api/Meridian.Api.csproj src/Meridian.Api/
RUN dotnet restore src/Meridian.Api/Meridian.Api.csproj

COPY src/ src/
RUN dotnet publish src/Meridian.Api/Meridian.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Meridian.Api.dll"]
