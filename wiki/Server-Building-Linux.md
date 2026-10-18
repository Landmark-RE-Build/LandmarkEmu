# Server Building on Linux

The servers and the tests build and run on Linux. **LaunchPad doesn't**: it is a Windows program (WPF).

## 1. Install the tools

- The **.NET 10 SDK**: from your distribution's packages where it has .NET 10, or with Microsoft's install script /
  packages (see Microsoft's ".NET on Linux" install page). Check it:

  ```
  dotnet --list-sdks
  ```

  A `10.0.x` line must be there.
- **git**.

## 2. Build the servers

Building the whole `LandmarkEmulator.sln` on Linux stops at LaunchPad, because it targets Windows. Build the projects
you need instead; their references are built with them:

```
dotnet build LandmarkEmulator.AuthServer
dotnet build LandmarkEmulator.WorldServer
dotnet build LandmarkEmulator.SocialServer
dotnet build LandmarkEmulator.CommerceServer
dotnet build LandmarkEmulator.EnvServer
dotnet build LandmarkEmulator.GatewayServer
dotnet build LandmarkEmulator.WorldChannel
```

(The Docker image is built the same way: it publishes these projects one by one.)

If you want the whole solution to build, .NET can compile Windows-targeting projects on Linux when asked to:
`dotnet build LandmarkEmulator.sln -p:EnableWindowsTargeting=true`. The LaunchPad it makes still only runs on Windows.

## 3. Run the tests

```
dotnet test LandmarkEmulator.Tests
```

The test project doesn't reference LaunchPad, so it builds as it is. It must end with `0 failed, 0 skipped` (one skip
is expected in a plain clone, see [Server Building](Server-Building#tests)).

## 4. Try your build in Docker

With Docker Engine and the compose plugin installed ([Server Installation on Linux](Server-Installation-Linux)),
rebuild the images from your working folder and restart:

```
docker compose up -d --build
```
