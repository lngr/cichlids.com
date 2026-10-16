FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /source
COPY . .
RUN printf '{}\n' > src/Cichlids.Etl/appsettings.json \
    && dotnet publish src/Cichlids.Api -c Release -o /out/api /p:UseAppHost=false \
    && dotnet publish src/Cichlids.Etl -c Release -o /out/migration /p:UseAppHost=false \
    && rm -f /out/api/appsettings.Development.json /out/migration/appsettings.json

FROM mwader/static-ffmpeg:7.1.1@sha256:11a44711684c0b9f754c047dcd64235b8b52deab251bd0e0a86f22faa160749c AS ffmpeg

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS api
WORKDIR /app
COPY --from=build /out/api .
COPY --from=ffmpeg /ffmpeg /ffprobe /usr/local/bin/
ENV ASPNETCORE_HTTP_PORTS=8080 DOTNET_EnableDiagnostics=0
USER 1654:1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cichlids.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:939555726cae4c916d212323ac3c0792996dcac2614f00e82cc40fcf059e1af3 AS migration
WORKDIR /app
COPY --from=build /out/migration .
ENV DOTNET_EnableDiagnostics=0
USER 1654:1654
ENTRYPOINT ["dotnet", "Cichlids.Etl.dll"]
CMD ["migrate"]
