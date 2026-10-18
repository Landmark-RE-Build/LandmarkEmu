# LandmarkEmulator.WorldChannel

**Program** (Docker: `docker/worldchannel`, optional, one per world; needs the world's gateway in its own container).
The back end of channel 1 ("World") of a world's gateway.

Back to [Code Documentation](Code-Documentation).

## What it does

It dials the world's gateway and registers as the back end of channel 1, and dials the world's zone process (its
internal port 20310). Every channel-1 packet goes on to the zone unchanged, which answers the client through the
gateway, except the ones this service answers itself:

| Part | File | Setting |
|---|---|---|
| Messages nothing needs to answer (locale, Steam info, client telemetry, leaderboard requests with no client reply) | `Sinks.cs` | always |
| Logout to character select (a relog session from the login service) | `Logout.cs` | always |
| Trade invitations (the zone runs the exchange itself) | `TradeBroker.cs` | `WC_TRADE_BROKER` |
| Duel challenges and the encounter lifecycle (the zone keeps the combat rules) | `EncounterBroker.cs` | `WC_ENCOUNTER_BROKER` |
| Build-site likes and the can-vote query, from its own database | `RatingBroker.cs`, `VoteStore.cs` | `WC_CLAIM_RATINGS` |

While the zone link is down, channel-1 packets are dropped (the client repeats its world-channel requests).

## Main files

| Path | What |
|---|---|
| `WorldChannelServer.cs`, `HostedService.cs` | Start-up |
| `WorldChannelConfiguration.cs` | `WorldChannel.json` ([Settings Reference](Settings-Reference#worldchanneljson)) |
| `ChannelRelay.cs` | The two links and the hand-on |
| `VoteStore.cs` | `lmemu_wc_<WORLD_KEY>` ([Database: World-Channel](Database-World-Channel)); on its first start it copies the likes counted before from the character database |
