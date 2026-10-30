# Chat voice: Azure AI Speech with Windows voices as backup

IXC reads chat aloud through the **IXC TTS** browser source in OBS. This page explains how IXC picks a voice, how to set up natural Indian English and Hindi voices, how the Azure key is protected, and how to check it all by ear.

## How IXC picks the engine

Settings > **Chat voice** > **Speech engine**:

| Choice | What happens |
|---|---|
| **Automatic** (default) | Azure AI Speech when you've added a key. If Azure fails, a Windows voice **in the same language** reads the message (if "Offline backup voice" is on). Without a key, IXC tries Microsoft Edge's free read-aloud voices first. Microsoft currently refuses those (HTTP 403), so the Windows voice is used. |
| **Azure AI Speech only** | Only Azure. If Azure fails, the message is marked failed with the reason, and no Windows voice is used. |
| **Windows voices only** | Only installed Windows voices. Nothing is sent to Azure. |

The fallback rules are fixed and predictable:

1. Azure is used when it is set up and not paused.
2. If an Azure request fails, the message is read by a Windows voice of the same language: `en-IN` first for Indian English, then any English, and only Hindi for Hindi voices.
3. If no Windows voice exists for that language, nothing is read. The message shows a reason, for example *"No Windows voice for Hindi (hi-IN) is installed. Install the Hindi speech pack… or pick another voice"*. IXC never switches to another language on its own.
4. Text written in Devanagari (for example `नमस्ते`) needs a Hindi Windows voice, because English Windows voices skip that script.
5. Network errors and timeouts are retried **Retries** times (default 1). A refused key (401/403) and a bad request (400) are not retried.
6. After 2 failed messages in a row, Azure is **paused** for the cooldown (default 60 s) and makes no calls. After that, one trial request decides. Success switches Azure back on by itself. The message that is playing is never interrupted.
7. A refused key stops all Azure calls until you press **Save** or **Test key**.
8. **Skip** and **Clear waiting** abort a voice request that is still running, and that is not counted as an Azure failure.

Timeouts, retries and the cooldown are on the Azure card (3 to 30 s, 0 to 2 retries, 10 to 600 s).

## Voices

The Azure voice list comes from Azure itself (`/cognitiveservices/voices/list` in your region), not from a list built into IXC. It is checked when you press Save or Test key, kept in `azure-voices.json` in the IXC data folder (it holds no secrets), and refreshed once a day. IXC offers:

- **Native** voices: every `en-IN` (English, India) and `hi-IN` (Hindi, India) voice your region lists, male and female.
- **Multilingual** voices: only those whose `SecondaryLocaleList` includes `hi-IN` or `en-IN`, labelled as multilingual.
- **Soft whisper**: `en-US-JennyNeural` with the `whispering` style (slower, lower), and only if Azure lists that style. Indian voices have no whisper style.
- Short fixed names for chat commands and settings: `az-in-female`, `az-in-male`, `az-hi-female`, `az-hi-male`, `az-multi-female`, `az-multi-male`, `az-whisper`. Each points to a listed voice (Neerja, Prabhat, Swara, Madhur, Ava, Andrew when available).

The **Language** filter on the Chat voice page shows Hindi, English (India), Multilingual or Other voices.

**Roman Hindi / Hinglish** (for example *"kya haal hai bhai"*) usually sounds best with the Indian English voices. Hindi voices read Devanagari best. IXC does not transliterate. Use **Pronunciation fixes** (`word=how to say it`, one per line, whole words, any script) for words that come out wrong, for example `gg=good game`. Changing pitch or speed does not make a voice more natural. Voice choice matters most.

Windows voices (offline): install **Hindi** and **English (India)** in Windows Settings > Time & language > Speech > Add voices. The Chat voice page shows whether each is installed. Windows voices ignore the Pitch setting.

## Setting up Azure

1. In the Azure portal, create a **Speech** resource (the free F0 tier gives about 500,000 characters a month). A region in India (for example `centralindia`) keeps latency low.
2. Copy **Key 1** and the **Region** from *Keys and Endpoint*.
3. In IXC, open Chat voice > **Natural voices (Azure Speech)**, paste the region and key, and press **Save**. IXC loads the voice list straight away, which also checks the key.
4. Pick a voice, type a sentence in **Test sentence** and press **🎧 Preview here**. The preview plays only in the dashboard. It is not queued, not played in OBS, and does not lower the music. **▶ Test on stream** plays through OBS.

## Security

- The key is stored in `secrets.dat`, encrypted with Windows DPAPI for your Windows user (the same protection Windows Credential Manager uses). It is never written to `config.json`, logs, diagnostics, error messages, chat, or any API answer. The field is a password box and is cleared after Save.
- Limits: a key on your PC can be read by any program that runs as your Windows user. It is also sent to Azure with each request (over HTTPS, to your region's Microsoft endpoint).
- Stronger protection would need a separate **token server**. It would keep the key and give IXC short-lived Azure tokens (`/sts/v1.0/issueToken`, valid 10 minutes). IXC does not include one, and nothing in IXC assumes one exists. Adding one means a new optional setting for the token URL, and IXC would send `Authorization: Bearer` instead of the key.
- If a key may have leaked, regenerate it in the Azure portal, then paste the new one into IXC.

## Tests

`tests/integration/32-azure-tts.test.mjs` and `33-voice-engine.test.mjs` use a **mocked** Azure server (`fake-azure.mjs`) and stand-in Windows voices (`IXC_SAPI_FAKE`). They prove IXC's choices, the request contents (SSML, language, escaping, Unicode), timing, cancelling, fallback, persistence and key privacy. They do **not** prove real audio quality or pronunciation.

## Manual check with a real key (by ear)

1. Set up Azure as above. Press **Test key**. Expected: *"Azure works - Ready - N voices from Azure"*.
2. Preview each: `az-in-female`, `az-in-male`, `az-hi-female`, `az-hi-male`, `az-whisper`, with these sentences:
   - `Namaste dosto, kya haal hai? Aaj ka match full masti wala hai.` (Roman Hindi)
   - `नमस्ते दोस्तों, आज हम PUBG खेलेंगे। Score 25-13 hai!` (Devanagari + English + numbers)
   - `Thanks for the follow, Rahul! GG everyone.`
   Note which voice sounds most natural for each line.
3. Set **Speech engine** to Automatic and voice `az-hi-female`. Disconnect the internet and press Preview. Expected: a Windows Hindi voice if the Hindi pack is installed, otherwise the "No Windows voice for Hindi" message. Reconnect, wait for the cooldown, preview again. Expected: Azure.
4. Turn on chat voice, send a test message in chat, and check that it plays once, in OBS, on the expected audio tracks, with the music lowered while it speaks.
5. Check the IXC logs folder and `config.json`: the key must not appear.
