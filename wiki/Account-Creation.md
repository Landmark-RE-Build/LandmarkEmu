# Account Creation

Every LandmarkEmu server has its own accounts. Players create them themselves, in LaunchPad or on the server's sign-up
website. Passwords never reach the server: both use **SRP-6a**, so the server stores only a salt and a verifier.

## Account names and passwords

- Account name: **3 to 32 letters, digits or `_`**, unique on that server.
- Password: **at least 6 characters** (checked by LaunchPad and the website, since the server never sees it).
- Character names are chosen in game. `STRICT_NAMES=true` in `.env` allows only RPG-style names (a capital first
  letter, letters only, no spaces); off by default.

## In LaunchPad

Account menu (top right) > **Create account**: account name, password twice, then **Create account**. See the
[Client Connection Guide](Client-Connection-Guide#6-create-an-account).

## On the sign-up website

The `web` container serves a sign-up page on `WEBSITE_PORT` (default 8080): `http://<server address>:8080/`. The page
works out the SRP salt and verifier in the browser and sends only those. LaunchPad's **Account website** link opens
it (or the address in `WEBSITE_URL`, if you set one).

How the website is kept apart from everything else:

- it can reach only the auth server's account service, and only its sign-up route; it has no access to the database
  or the worlds, and no passwords;
- it runs read-only, as a non-root user, with strict security headers;
- it has its own spam limits (5 sign-ups a minute per address, 60 a minute in total), a hidden field bots fill in,
  and a 2-second minimum before sending.

For a server on the internet, put HTTPS in front of the website (a reverse proxy such as Caddy, or a tunnel). The
password is never sent even over plain HTTP, but HTTPS stops anyone on the way from changing the page.

## Sign-up settings

In `.env` (they apply to LaunchPad and the website alike):

| Setting | Default | |
|---|---|---|
| `REGISTRATION_OPEN` | `true` | `false` closes sign-ups ("Account creation is closed.") |
| `REGISTRATIONS_PER_ADDRESS_PER_HOUR` | `3` | New accounts per player address per hour |
| `REGISTRATIONS_PER_HOUR` | `60` | New accounts from everyone per hour |

Run `docker compose up -d` after changing them. A private server can open sign-ups, let everyone create their
account, then close them again.

## Admins

Admins can use the game's own admin slash commands (`/goto`, `/target`, `/hideme`, `/run`, `/servertime`, `/prop`,
`/item` ...) and type **`/admin`** in chat to open the admin page: god mode, revive, coins and items, travel, claiming a
site, time of day, creatures, a shutdown countdown, the Player Studio staff queue, the monitor page, and any developer
command, for yourself, everyone or one player. All commands: [Command Documentation](Command-Documentation).

Admins are set in `.env`:

```
ADMIN_ACCOUNT=account:Alice
ADMIN_2=character:AliceTheBuilder
ADMIN_3=
ADMINS=account:Bob,character:Carol
```

| Entry | Who is an admin |
|---|---|
| `account:<name>` | Every character of the account with that account name |
| `character:<name>` | That character, whichever account owns it |
| `<name>` (no prefix, the old form) | The account or the character with that name |

`ADMIN_ACCOUNT`, `ADMIN_2` and `ADMIN_3` take one entry each; `ADMINS` takes any number, comma separated. The prefixes
and names ignore case.

**Always use the prefixed forms.** Account names and character names are both chosen by players, so a bare name could
be taken in the other kind by someone else. When that happens the bare entry makes **nobody** an admin, and the world
logs an error with the prefixed form to use instead.

Create the account first (as above), set the entry, run `docker compose up -d`, and log in again. `start.bat` asks for
your account name on its first run; check that `ADMIN_ACCOUNT` in `.env` starts with `account:`.

## Creating accounts as the server owner

The account service only accepts new accounts over SRP, from LaunchPad or the website, so the usual way is to create
yours like any player. When the auth server runs in a console outside Docker, its console also takes
`account create <name> <password>`.
