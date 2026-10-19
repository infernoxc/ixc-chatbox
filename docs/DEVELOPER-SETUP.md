# Developer setup: the phone relay and the "Connect account" apps

These are **one-time steps for the person who publishes IXC** (not for users). Until you do them:
- the phone remote shows "not available in this build",
- the **Connect account** buttons say sign-in isn't available.

Everything else works without them, including reading chat on Twitch, Kick, YouTube and Rumble.

The values below go into GitHub **repository variables** (Settings > Secrets and variables > Actions > *Variables*). The release
build writes them into `core/ixc.defaults.json`. They are public IDs, not secrets: the secrets stay inside the relay.

| Variable | What |
|---|---|
| `IXC_RELAY_URL` | Your relay address, e.g. `https://ixc-relay.<you>.workers.dev` |
| `IXC_TWITCH_CLIENT_ID` | Twitch app Client ID |
| `IXC_GOOGLE_CLIENT_ID` | Google OAuth client ID (YouTube) |
| `IXC_KICK_CLIENT_ID` | Kick app Client ID |

## 1. Deploy the relay (Cloudflare, free plan, about 10 minutes)
1. Create a free account at [dash.cloudflare.com](https://dash.cloudflare.com). Workers and Durable Objects (SQLite) are included in the free plan.
2. Install Node.js 20+.
3. In this repository, deploy it. Wrangler opens the browser to log in once:
   ```
   cd relay
   npm install
   npx wrangler deploy
   ```
4. Wrangler prints the address, e.g. `https://ixc-relay.yourname.workers.dev`. Open `/health` in a browser: it should say `IXC relay ok`.
5. Put the address in the repository variable `IXC_RELAY_URL`.

**What the relay does:** it serves the phone page and forwards messages between a phone and *its* PC. It stores only a hash of each
PC's secret. Phone pairing and device keys are checked by the PC, not by the relay.

**Cost:** each streamer's PC keeps one connection open. Keep-alive pings are answered without waking the Durable Object, so normal
use stays well inside the free plan for small to medium numbers of users.

## 2. Twitch app (reply, follower alerts, viewer counts)
1. Go to [dev.twitch.tv/console/apps](https://dev.twitch.tv/console/apps) and click **Register Your Application**.
2. Fill in:
   - **Name:** IXC (or your own),
   - **OAuth Redirect URL:** `http://localhost`. It isn't used: IXC uses Twitch's device sign-in,
   - **Category:** Broadcaster Suite,
   - **Client Type: Public**.
3. Copy the **Client ID** into `IXC_TWITCH_CLIENT_ID`. A public client has no secret, so nothing goes into the relay.

## 3. Google / YouTube app (reply in YouTube chat)
1. In [console.cloud.google.com](https://console.cloud.google.com), create a project and enable the **YouTube Data API v3**.
2. Set up the **OAuth consent screen**:
   - External,
   - app name IXC,
   - scope `https://www.googleapis.com/auth/youtube.force-ssl`.
3. Under **Credentials**, create an **OAuth client ID** of type **Desktop app**.
4. Put the Client ID in `IXC_GOOGLE_CLIENT_ID`. Store the secret in the relay:
   ```
   npx wrangler secret put GOOGLE_CLIENT_ID
   npx wrangler secret put GOOGLE_CLIENT_SECRET
   ```
5. Limits to know about:
   - Until Google verifies the app (needed for this scope), users see an "unverified app" warning and at most 100 users can sign in.
   - YouTube's API has a daily quota (10,000 units). Sending a chat message costs about 50 units, which is enough for normal replies.

## 4. Kick app (reply in Kick chat)
1. Go to [kick.com](https://kick.com) > Settings > **Developer** and create an app.
2. Set the **Redirect URL** to `http://localhost:8767/oauth/kick`.
3. Select the scopes `user:read`, `channel:read`, `chat:write` and `moderation:ban` (what IXC asks for; see the table in the checklist below).
4. Put the Client ID in `IXC_KICK_CLIENT_ID`. Store the ID and secret in the relay:
   ```
   npx wrangler secret put KICK_CLIENT_ID
   npx wrangler secret put KICK_CLIENT_SECRET
   ```

## Maintainer checklist: one-time registration (clients only press "Connect")
IXC is built so that **you register the platform apps once** and every client just presses **Connect** on Platforms & accounts. A client never
creates a developer app and never types a Client ID or secret. (The "Your apps" form stays as an optional override for people who want their own.)

| Platform | Sign-in method | Who holds a secret | Redirect URL to register | Scopes IXC requests |
|---|---|---|---|---|
| Twitch | Device-code sign-in, **public** client | nobody (no secret exists) | `http://localhost` (unused by device flow, but the console requires one) | `chat:read chat:edit moderator:read:followers channel:read:subscriptions moderator:manage:chat_messages moderator:manage:banned_users clips:edit` |
| YouTube (Google) | Browser sign-in, PKCE, loopback | **the relay** (Google Desktop clients still need the secret for the code exchange) | `http://127.0.0.1:8767/oauth/youtube` (Desktop-type clients accept any loopback port) | `https://www.googleapis.com/auth/youtube.force-ssl` |
| Kick | Browser sign-in, PKCE | **the relay** | `http://localhost:8767/oauth/kick` | `user:read channel:read chat:write moderation:ban` |

Checklist (tick each one):
- [ ] Deploy the relay (section 1) and set `IXC_RELAY_URL`. Do **not** turn on Cloudflare Workers Logs / Logpush for it (`relay/wrangler.toml` sets `observability.enabled = false`).
- [ ] Register the Twitch, Google and Kick apps as above; put only the **Client IDs** into the repository variables.
- [ ] Put the Google and Kick client IDs **and secrets** into the relay with `wrangler secret put`. Never in the repository, in `ixc.defaults.json`, or in the installer.
- [ ] Rotate a secret by running `wrangler secret put` again; clients need no update.
- [ ] Publish a **privacy notice** (a web page; Google requires one) saying at least: what IXC stores (the sign-in tokens, encrypted on the client's own PC, never uploaded); that for Google and Kick sign-in the relay forwards the one-time code or refresh token to the platform and keeps nothing; that nothing is logged by the relay; how to revoke (IXC > Platforms & accounts > Disconnect, plus the platform's own "connected apps" page); contact address.
- [ ] Add that notice's address to the Google consent screen and, where the platform asks, to the Twitch and Kick app settings.
- [ ] **Google verification** (needed before the general public can use YouTube replies): the scope `youtube.force-ssl` is *sensitive*. Google requires a verified domain you own, a homepage and the privacy policy on it, a scope justification, and a demo video showing the sign-in and what the scope is used for. Until it is approved the client sees an "unverified app" warning, the app is limited to 100 users, and while the consent screen is in *Testing* the refresh token stops working after 7 days. Do not work around this; apply for verification.
- [ ] **Quota:** the YouTube Data API default is 10,000 units per day for the whole app (about 50 per sent chat message; polling and reading cost more). With many clients this runs out; request a quota extension from Google when you approach it.
- [ ] **Kick / Twitch:** check each platform's current developer terms for app review, user caps and rate limits before a wide release. This repository has not verified those rules; do not assume they match Twitch's or Google's.
- [ ] After registering, build with the variables set, install on a clean profile and press Connect for each platform once. Only then call replies "working".

## 5. Release
Push a tag `vX.Y.Z` that matches `VERSION`. The release workflow then:
1. builds IXC with these variables,
2. tests the installer on a clean Windows,
3. publishes `IXC-Setup-vX.Y.Z.exe`, the ZIP and `SHA256SUMS.txt`.

The in-app updater uses them. See [RELEASING.md](RELEASING.md).

### Optional: code signing
Windows SmartScreen warns about unsigned downloads until a file builds up reputation. A code-signing certificate (or Azure Trusted
Signing) removes the warning. If you get one, sign `ixc-core.exe` and `IXC-Setup.exe` in `build/build.ps1` before the checksums are written.
