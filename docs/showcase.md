# Showcase: store and gallery

The HUD's **Showcase** button opens the `InGameMarketplaceBrowser` window (its title bar reads "Showcase"). The
window is an embedded Chromium browser (CEF, Chrome 33, `SoeBrowser/1.0`) that shows the URL in the client's
`ClientConfig.ini`:

```ini
[Marketplace]
MarketplaceUrl=http://127.0.0.1:19001
MarketplaceAssets=http://127.0.0.1:19001
```

The client adds its own routes to that address:

| Opened by | URL |
|---|---|
| Login preload (`Browser.Create`, view "marketplace") | `MarketplaceUrl#!/gallery` |
| `Browser.OpenMarketplace` | `MarketplaceUrl#!/marketplace` |
| `Browser.OpenGallery` | `MarketplaceUrl#!/gallery` |
| `Browser.OpenMarketplaceWithTags` | `MarketplaceUrl/#!/marketplace/search/limit=50&filterEquals=Purchasable:1&SearchTags=<tags>` |

The official pages lived on Daybreak's store servers (`store*.landmarkthegame.com`), which also delivered what
was bought. The client data has no catalogue (`Resources/Marketing/*.txt` are empty), so the world server now
serves these pages itself (`Game/WebPages.cs`, `Game/Store.cs`, page files in
`LandmarkEmulator.WorldServer/Resources/Web/`).

## Pages

- **Store** (`#!/marketplace`): every item is free for now. Sections:
  - Outfits: equipment slot 1 cosmetics.
  - Decorations: prop items in the builder's prop categories (`PropCategory`, `PropCategoryPropMap`,
    `PropCategoryGroup`; uncategorised props such as game markers and crafting stations are left out).
  - Designs: design items with a client template.

  Tools, weapons, armour and accessories (slot 3 combat trinkets) are not sold: they are crafted. Each item has an
  icon, a name and a description, plus a **Get** button; decorations can be taken 1/5/10/25 at a time.
- **Gallery** (`#!/gallery`): every build site, best rated first, with owner, landscape, votes and an online dot.
  **Visit** travels there (the site's Starting Location, else its centre), as the Landscapes window would.
- Search box: filters both pages; the client's tag search (`SearchTags=`) fills it in.

The page is plain ES5 with `XMLHttpRequest` and flexbox: the client's browser is Chrome 33 (no `fetch`, arrow
functions or CSS grid).

## Help page and help links

- `#!/help` is a help page for this server (Showcase tab "Help").
- The server's ClientSettings packet (0xE9: three URLs, u32, bool) used to carry PlanetSide 2's help and shop
  addresses (a leftover capture). It now points at `#!/help` and `#!/marketplace` on this server.
- The Escape menu's Help (Lua `Ui.OpenSupportUrl`) opens ClientConfig `PetitionUri` in the system's own web browser.
  Set it to `http://<server>:19001/#!/help` to land on that page.

## Who is buying: sign-in

The page only talks to the web server, so the server tells each player's browser who they are:

1. At world entry the server makes a random token for the session (`Store.SignIn`).
2. 10 s later (the browser only exists after the client's login preload) the server runs, in that client,
   `SoeGui.GetWindow('InGameMarketplaceBrowser'):ShowURL('<WebPagesUrl>/?t=<token>#!/marketplace')`. This goes
   through `ClientLua.RunCode` (below).
3. The page stores the token in `sessionStorage` (one per client process, so two clients on one PC stay apart)
   and sends it with every request. When it calls `/api/me` with it, the server marks the page as signed in.
   Opening the Showcase (UI event 7) before that re-sends the sign-in.
4. The client's own later navigations reuse the same browser, so the token stays. It dies with the session.

## Web API (`http://<WebPagesHost>:19001`)

| Request | Reply |
|---|---|
| `GET /` (any path without extension) | the Showcase page |
| `GET /web/showcase.js`, `/web/showcase.css` | page script and style |
| `GET /icons/<image set id>.png` | item icon (see below) |
| `GET /api/me?t=` | `{signedIn, name}` |
| `GET /api/catalogue` | `[{id, name, description, icon, section, group}]` |
| `GET /api/gallery` | `[{id, name, owner, landscape, description, rating, online}]` |
| `POST /api/take` `t, item, count` | `{ok, message}`: item added to the player's bag (max 100, bag space permitting, one take per 0.4 s) |
| `POST /api/visit` `t, site` | `{ok, message}`: travel to the build site |
| `GET /uploads/lm/images/templates[N]/(small\|large)/<24-digit guid>.jpg` | design screenshots (`MarketplaceAssets`) |

Work that changes the game runs on the world thread (`WebPages.Update` drains a queue each tick).

Settings in `WorldServer.json`:
- `WebPagesHost`: listen address (default loopback only).
- `WebPagesUrl`: the address players' browsers use (default `http://127.0.0.1:19001`); it must match their
  `MarketplaceUrl`.

## Icons

Item icons are the client's image sets: `ImageSetMappings.txt` (IMAGE_TYPE 6 = 128 px, 5 = 64 px, 4 = 32 px) →
`Images.txt` file name → a loose `.dds` under `Resources/Images/<folder>/` (a few are in the asset packs). Export
them once into the game data folder:

```bash
python re/tools/export_icons.py --client "<Landmark folder>" --out "<GameDataPath>/icons"
```

## Running Lua in the client: `ClientLua.RunCode`

The server's existing Lua channel is the first-time-event Script packet (FirstTimeEventBase sub 5 → client
FUN_142e3fdf0). It calls one named function with string arguments separated by commas, and:
- A **Lua-defined** table function (`Table.Method`) gets exactly the arguments and no `self`. So
  `InGameMarketplaceBrowser.ShowURL(url)` failed with "attempt to index a string value (local 'self')".
- A **native** table function gets the table as its first argument. `Ui.DoCommand` saw `(table, "Ui")` and ignored
  it, because it reads argument 1.
- A name without a dot is a global function with just the arguments.

`RunCode(session, code)` combines two calls:
1. The global `BindCommandToLua("lmemu", code)` binds the console command `lmemu` to the code. Console commands
   are lower case.
2. `Ui.ProcessChatCommand("/lmemu")` runs it. This native reads the last argument, so the table in front doesn't
   matter. The client also logs "Unrecognized command - lmemu" afterwards.

Code containing commas is sent as `load("...")()` with each comma written as `\44`. The dev command
`luacode <code>` runs code in a player's client. The winner banner fix
([instanced-games.md](instanced-games.md)) uses the same path.

## Tests

`re/test/scenarios/showcase.json` covers:
- token sign-in;
- the page, me and catalogue requests;
- a free outfit for the right player;
- a wrong token and an unknown item refused;
- the gallery;
- Visit.

The `http` step calls the API.

## Portraits and headshots

The official portraits came from a rendering service (Fotomat). The 0.589 client can render its own instead:
its ScreenshotManager captures a portrait and a headshot of the local character. It then:
1. sends the server the CRC-32 of portrait.png (AddClientPortraitCrc 0x75 `{u32}`);
2. posts both images to ClientConfig `[Portrait] UploadUrl` (multipart, boundary `AaBb432101234bBaA`: imageType
   `portrait`, characterId, thumbnailFile `headshot.png`, imageFile `portrait.png`).

The server side is `Game/Portraits.cs`:
- **Uploads** are taken at `POST /portrait/upload`, so set `UploadUrl=http://<server>:19001/portrait/upload`.
  An upload is only kept if its CRC is the one that character announced over its own game session, so nobody can
  overwrite someone else's portrait. Images are stored as `Portraits/<character>_portrait.png` and `_headshot.png`
  next to the server.
- **Requests:** clients ask with FotomatBase 0x98 sub 2 PortraitDataRequest `{u64 character, string "Headshot" |
  "Portrait"}` (sent for party members and other players). The server answers with sub 3 PlayerImageData
  `{u64 character, string kind, bool, appearance block (empty), u32 length + PNG}` when it has an image.

Limits found in 0.589:
- No window of this client shows those images: the Flash helper `BindPlayerPortrait` (`imgps://<guid>`) is never
  called, and the Party window has no faces.
- What makes the client capture and upload its own portrait is not traced yet (the capture and post functions are
  only reached through function pointers).

Tests:
- `re/test/portrait_upload.py` posts like the client.
- Dev commands: `portraitcrc <crc>` and `portraittest <character> <kind> <icon id>`.
