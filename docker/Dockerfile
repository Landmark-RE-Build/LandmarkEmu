# LandmarkEmu servers in containers. One build stage compiles the solution; two small runtime images run the auth
# server and the world server. Built by docker-compose.yml (context: the solution folder, one level up).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first, so package restore is cached until a project's references change.
COPY LandmarkEmulator.sln ./
COPY LandmarkEmulator.AuthServer/LandmarkEmulator.AuthServer.csproj LandmarkEmulator.AuthServer/
COPY LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj LandmarkEmulator.WorldServer/
COPY LandmarkEmulator.Database/LandmarkEmulator.Database.csproj LandmarkEmulator.Database/
COPY LandmarkEmulator.Database.Auth/LandmarkEmulator.Database.Auth.csproj LandmarkEmulator.Database.Auth/
COPY LandmarkEmulator.Database.Character/LandmarkEmulator.Database.Character.csproj LandmarkEmulator.Database.Character/
COPY LandmarkEmulator.Gateway/LandmarkEmulator.Gateway.csproj LandmarkEmulator.Gateway/
COPY LandmarkEmulator.Shared/LandmarkEmulator.Shared.csproj LandmarkEmulator.Shared/
COPY LandmarkEmulator.WebAPI/LandmarkEmulator.WebAPI.csproj LandmarkEmulator.WebAPI/
RUN dotnet restore LandmarkEmulator.AuthServer/LandmarkEmulator.AuthServer.csproj \
 && dotnet restore LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj
COPY . .
RUN dotnet publish LandmarkEmulator.AuthServer/LandmarkEmulator.AuthServer.csproj -c Release -o /out/auth --no-restore \
 && dotnet publish LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj -c Release -o /out/world --no-restore \
 && cp /out/auth/AuthServer.example.json /out/auth/AuthServer.json \
 && cp /out/world/WorldServer.example.json /out/world/WorldServer.json

# Auth server: login (UDP 20042) and the account web API the launcher uses (TCP 5000).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS auth
WORKDIR /app
COPY --from=build /out/auth ./
EXPOSE 20042/udp 5000/tcp
ENTRYPOINT ["dotnet", "LandmarkEmulator.AuthServer.dll"]

# World server: the game (UDP 19000) and its in-game web pages (TCP 19001). Its saved state (terrain edits, felled
# trees, screenshots...) is written next to the program, so docker-entrypoint.sh links those paths into /data.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS world
WORKDIR /app
COPY --from=build /out/world ./
COPY docker/world-entrypoint.sh /usr/local/bin/world-entrypoint.sh
RUN sed -i 's/\r$//' /usr/local/bin/world-entrypoint.sh && chmod +x /usr/local/bin/world-entrypoint.sh
EXPOSE 19000/udp 19001/tcp
ENTRYPOINT ["/usr/local/bin/world-entrypoint.sh"]
