FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY OrderService.csproj ./
RUN dotnet restore

COPY . .

RUN dotnet publish \
    -c Release \
    -o /app/publish \
    --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && adduser --disabled-password --gecos "" appuser

COPY --from=build /app/publish .

RUN chown -R appuser:appuser /app

USER appuser

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "OrderService.dll"]