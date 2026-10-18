# LandmarkEmu servers in containers. One build stage compiles the solution; small runtime images run the auth server,
# the social service (opt-in) and the world server. Built by docker-compose.yml (context: the solution folder, one level
# up).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first, so package restore is cached until a project's references change.
COPY LandmarkEmulator.sln ./
COPY LandmarkEmulator.AuthServer/LandmarkEmulator.AuthServer.csproj LandmarkEmulator.AuthServer/
COPY LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj LandmarkEmulator.WorldServer/
COPY LandmarkEmulator.Database/LandmarkEmulator.Database.csproj LandmarkEmulator.Database/
COPY LandmarkEmulator.Database.Auth/LandmarkEmulator.Database.Auth.csproj LandmarkEmulator.Database.Auth/
COPY LandmarkEmulator.Database.Character/LandmarkEmulator.Database.Character.csproj LandmarkEmulator.Database.Character/
COPY LandmarkEmulator.EnvServer.Core/LandmarkEmulator.EnvServer.Core.csproj LandmarkEmulator.EnvServer.Core/
COPY LandmarkEmulator.Gateway/LandmarkEmulator.Gateway.csproj LandmarkEmulator.Gateway/
COPY LandmarkEmulator.Internal/LandmarkEmulator.Internal.csproj LandmarkEmulator.Internal/
COPY LandmarkEmulator.Database.Social/LandmarkEmulator.Database.Social.csproj LandmarkEmulator.Database.Social/
COPY LandmarkEmulator.SocialServer/LandmarkEmulator.SocialServer.csproj LandmarkEmulator.SocialServer/
COPY LandmarkEmulator.GatewayServer/LandmarkEmulator.GatewayServer.csproj LandmarkEmulator.GatewayServer/
COPY LandmarkEmulator.WorldChannel/LandmarkEmulator.WorldChannel.csproj LandmarkEmulator.WorldChannel/
COPY LandmarkEmulator.CommerceServer/LandmarkEmulator.CommerceServer.csproj LandmarkEmulator.CommerceServer/
COPY LandmarkEmulator.EnvServer/LandmarkEmulator.EnvServer.csproj LandmarkEmulator.EnvServer/
COPY LandmarkEmulator.Shared/LandmarkEmulator.Shared.csproj LandmarkEmulator.Shared/
COPY LandmarkEmulator.WebAPI/LandmarkEmulator.WebAPI.csproj LandmarkEmulator.WebAPI/
COPY LandmarkEmulator.PatchPacks/LandmarkEmulator.PatchPacks.csproj LandmarkEmulator.PatchPacks/
RUN dotnet restore LandmarkEmulator.AuthServer/LandmarkEmulator.AuthServer.csproj \
 && dotnet restore LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj \
 && dotnet restore LandmarkEmulator.SocialServer/LandmarkEmulator.SocialServer.csproj  && dotnet restore LandmarkEmulator.GatewayServer/LandmarkEmulator.GatewayServer.csproj  && dotnet restore LandmarkEmulator.WorldChannel/LandmarkEmulator.WorldChannel.csproj \
 && dotnet restore LandmarkEmulator.CommerceServer/LandmarkEmulator.CommerceServer.csproj  && dotnet restore LandmarkEmulator.EnvServer/LandmarkEmulator.EnvServer.csproj
COPY . .
RUN dotnet publish LandmarkEmulator.AuthServer/LandmarkEmulator.AuthServer.csproj -c Release -o /out/auth --no-restore \
 && dotnet publish LandmarkEmulator.WorldServer/LandmarkEmulator.WorldServer.csproj -c Release -o /out/world --no-restore \
 && dotnet publish LandmarkEmulator.SocialServer/LandmarkEmulator.SocialServer.csproj -c Release -o /out/social --no-restore  && dotnet publish LandmarkEmulator.GatewayServer/LandmarkEmulator.GatewayServer.csproj -c Release -o /out/gateway --no-restore  && dotnet publish LandmarkEmulator.WorldChannel/LandmarkEmulator.WorldChannel.csproj -c Release -o /out/worldchannel --no-restore \
 && dotnet publish LandmarkEmulator.CommerceServer/LandmarkEmulator.CommerceServer.csproj -c Release -o /out/commerce --no-restore  && dotnet publish LandmarkEmulator.EnvServer/LandmarkEmulator.EnvServer.csproj -c Release -o /out/envserver --no-restore \
 && cp /out/auth/AuthServer.example.json /out/auth/AuthServer.json \
 && cp /out/world/WorldServer.example.json /out/world/WorldServer.json \
 && cp /out/social/SocialServer.example.json /out/social/SocialServer.json  && cp /out/gateway/GatewayServer.example.json /out/gateway/GatewayServer.json  && cp /out/worldchannel/WorldChannel.example.json /out/worldchannel/WorldChannel.json \
 && cp /out/commerce/CommerceServer.example.json /out/commerce/CommerceServer.json  && cp /out/envserver/EnvServer.example.json /out/envserver/EnvServer.json

# Auth server: login (UDP 20042) and the account web API the launcher uses (TCP 5000).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS auth
WORKDIR /app
COPY --from=build /out/auth ./
EXPOSE 20042/udp 5000/tcp
ENTRYPOINT ["dotnet", "LandmarkEmulator.AuthServer.dll"]

# Social service (opt-in, docker/social): presence, friends and ignore lists shared by every world. Internal TCP 20200,
# reached by the worlds over the Docker network only (never published). Its log goes to the container's output.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS social
WORKDIR /app
COPY --from=build /out/social ./
EXPOSE 20200/tcp
ENTRYPOINT ["dotnet", "LandmarkEmulator.SocialServer.dll"]

# Gateway (opt-in, docker/gateway): a world's client-facing UDP port in its own process; the world's zone process dials
# its internal TCP port 20300 (never published). Without it the world runs the gateway itself.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS gateway
WORKDIR /app
COPY --from=build /out/gateway ./
EXPOSE 19000/udp 20300/tcp
ENTRYPOINT ["dotnet", "LandmarkEmulator.GatewayServer.dll"]

# World channel (opt-in, docker/worldchannel; needs the gateway container): the back end of a world's channel 1.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worldchannel
WORKDIR /app
COPY --from=build /out/worldchannel ./
ENTRYPOINT ["dotnet", "LandmarkEmulator.WorldChannel.dll"]

# Commerce (opt-in, docker/commerce): Player Studio / Marketplace, contests and portraits shared by every world. Internal
# TCP 20400, reached by the worlds over the Docker network only (never published); its files (contest screenshots,
# portraits) in the volume at /data.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS commerce
WORKDIR /app
COPY --from=build /out/commerce ./
EXPOSE 20400/tcp
ENTRYPOINT ["dotnet", "LandmarkEmulator.CommerceServer.dll"]

# Environment Server (opt-in, docker/envserver): one world's terrain / voxel store (edits, heals, veins, ownership, LOD,
# stamps). Internal TCP 20500, reached by the world's zone over the Docker network only (never published); its store in
# the volume at /data, the game and the server's game data read-only. Same garbage-collector setting as the world (the
# store makes the same large, short-lived allocations).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS envserver
WORKDIR /app
ENV DOTNET_GCConserveMemory=7
COPY --from=build /out/envserver ./
EXPOSE 20500/tcp
ENTRYPOINT ["dotnet", "LandmarkEmulator.EnvServer.dll"]

# World server: the game (UDP 19000) and its in-game web pages (TCP 19001). Its saved state (terrain edits, felled
# trees, screenshots...) is written next to the program, so docker-entrypoint.sh links those paths into /data.
# (The world stays the last stage, the default of a plain "docker build".)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS world
WORKDIR /app
# Garbage collector: compact more and give memory back to the OS sooner (0-9; the world's terrain cells and decoded
# templates make large, short-lived allocations).
ENV DOTNET_GCConserveMemory=7
COPY --from=build /out/world ./
COPY docker/world-entrypoint.sh /usr/local/bin/world-entrypoint.sh
RUN sed -i 's/\r$//' /usr/local/bin/world-entrypoint.sh && chmod +x /usr/local/bin/world-entrypoint.sh
EXPOSE 19000/udp 19001/tcp
ENTRYPOINT ["/usr/local/bin/world-entrypoint.sh"]
