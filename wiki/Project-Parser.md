# LandmarkEmulator.Parser

**Tool** (console program). A research tool that reads network captures of the game (`.pcap` / `.pcapng`) and
decodes the SOE, gateway and zone layers, so packet layouts can be studied and reference records extracted. It is not
needed to run a server.

Back to [Code Documentation](Code-Documentation).

## Modes

```
dotnet run --project LandmarkEmulator.Parser -- triage <captures>
dotnet run --project LandmarkEmulator.Parser -- parse <capture or folder> [--opcodes 0xED00,0xF900] [--summary-only] [--with-timeline]
dotnet run --project LandmarkEmulator.Parser -- timeline <capture> --seconds 60 --markdown <out.md>
dotnet run --project LandmarkEmulator.Parser -- pack-terrain <parsed bins folder>
dotnet run --project LandmarkEmulator.Parser -- merge-cel2 <cell id list>
```

- **triage**: which captures hold a usable session (login and keys found).
- **parse**: decrypts and decodes a session; dumps chosen opcodes as binary files with a JSON description, a summary and
  an index (`summary.json`, `dumps-index.csv`), optionally a zone-entry timeline.
- **timeline**: the zone-entry sequence with timings, as CSV or markdown.
- **pack-terrain** / **merge-cel2**: build or extend a terrain cell store (CEL2) from captured CellsUpdate packets
  ([Format: CEL2](Format-CEL2)).

## Main files

| Path | What |
|---|---|
| `Parser.cs` | Command line and modes |
| `PcapUdpReader.cs`, `IPHeader.cs`, `UdpHeader.cs` | Reading captures |
| `CaptureSessionParser.cs`, `DataStreamParsing.cs`, `CaptureReading.cs` | Rebuilding the SOE sessions and the game packet stream (using the server's own Shared and Gateway code) |
| `CaptureTriage.cs`, `CaptureEnrichment.cs`, `CaptureModels.cs` | Triage and decoding the login side |
| `ZoneTimelineMarkdown.cs` | Timelines |
| `TerrainCel2Packer.cs` | CEL2 terrain stores |

Captures are not part of the repository.
