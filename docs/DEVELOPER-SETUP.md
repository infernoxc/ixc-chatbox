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
3. Select the scopes `user:read`, `channel:read` and `chat:write`.
4. Put the Client ID in `IXC_KICK_CLIENT_ID`. Store the ID and secret in the relay:
   ```
   npx wrangler secret put KICK_CLIENT_ID
   npx wrangler secret put KICK_CLIENT_SECRET
   ```

## 5. Release
Push a tag `vX.Y.Z` that matches `VERSION`. The release workflow then:
1. builds IXC with these variables,
2. tests the installer on a clean Windows,
3. publishes `IXC-Setup-vX.Y.Z.exe`, the ZIP and `SHA256SUMS.txt`.

The in-app updater uses them. See [RELEASING.md](RELEASING.md).

### Optional: code signing
Windows SmartScreen warns about unsigned downloads until a file builds up reputation. A code-signing certificate (or Azure Trusted
Signing) removes the warning. If you get one, sign `ixc-core.exe` and `IXC-Setup.exe` in `build/build.ps1` before the checksums are written.
