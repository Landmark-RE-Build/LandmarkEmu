# Server Building on Windows

## 1. Install the tools

- The **.NET 10 SDK** (x64) from Microsoft's .NET download page. Check it in a new terminal:

  ```
  dotnet --list-sdks
  ```

  A `10.0.x` line must be there.
- **git for Windows**, or download the code as a ZIP ([Server Cloning](Server-Cloning)).
- Optional: Visual Studio or Rider, with .NET 10 support.

## 2. Build

In a terminal in the project folder (the one with `LandmarkEmulator.sln`):

```
dotnet build LandmarkEmulator.sln
```

This builds every project, LaunchPad included. The output of each is in its own `bin\Debug\net10.0\` folder
(`bin\Debug\net10.0-windows\` for LaunchPad). A release build: `dotnet build LandmarkEmulator.sln -c Release`.

## 3. Run the tests

```
dotnet test LandmarkEmulator.Tests
```

It must end with `0 failed, 0 skipped` (one skip is expected in a plain clone, see
[Server Building](Server-Building#tests)).

## 4. Build LaunchPad as one exe

```
dotnet publish LandmarkEmulator.LaunchPad -c Release -o publish/LaunchPad
```

This makes one self-contained `publish\LaunchPad\LaunchPad.exe` (about 65 MB; the player needs no .NET install, only
the WebView2 runtime Windows already has). Copy it into a Landmark folder as described in the
[Client Connection Guide](Client-Connection-Guide).

## 5. Try your build in Docker

With Docker Desktop installed ([Server Installation on Windows](Server-Installation-Windows)), rebuild the server
images from your working folder and restart:

```
docker compose up -d --build
```

or `start.bat -Update`.
