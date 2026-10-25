# Посредник сетевой игры Last Edict (docs/network.md §6). Сборка: docker build -t le-relay tools/Relay
# Запуск: docker run -d --name le-relay --restart unless-stopped -p 127.0.0.1:8790:8790 -v le-relay-data:/data le-relay
# Секретов нет; за обратным прокси — RELAY_PATH_BASE (путь) и RELAY_TRUST_PROXY=1 (адрес из X-Real-IP).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Relay.csproj .
RUN dotnet restore
COPY *.cs ./
RUN dotnet publish -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:8790 RELAY_DATA=/data DOTNET_gcServer=0 DOTNET_GCHeapHardLimit=0x10000000
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data
USER $APP_UID
EXPOSE 8790
HEALTHCHECK --interval=30s --timeout=3s CMD ["dotnet", "Relay.dll", "--health"]
ENTRYPOINT ["dotnet", "Relay.dll"]
