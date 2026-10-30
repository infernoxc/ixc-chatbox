// MOCKED Azure Speech for tests: answers voices/list and synthesis like Azure does, records every request. Never a real Azure call,
// so tests using it prove request contents and IXC's behaviour, not real audio quality.
// Part of IXC - (c) 2026 Ishan (InFerNoxC) - MIT License
import { httpServer, sleep } from './harness.mjs';

export const CATALOG = [
  { ShortName: 'en-IN-NeerjaNeural', DisplayName: 'Neerja', Locale: 'en-IN', Gender: 'Female', VoiceType: 'Neural', Status: 'GA', StyleList: ['newscast', 'cheerful'] },
  { ShortName: 'en-IN-PrabhatNeural', DisplayName: 'Prabhat', Locale: 'en-IN', Gender: 'Male', VoiceType: 'Neural', Status: 'GA' },
  { ShortName: 'hi-IN-SwaraNeural', DisplayName: 'Swara', Locale: 'hi-IN', Gender: 'Female', VoiceType: 'Neural', Status: 'GA' },
  { ShortName: 'hi-IN-MadhurNeural', DisplayName: 'Madhur', Locale: 'hi-IN', Gender: 'Male', VoiceType: 'Neural', Status: 'GA' },
  { ShortName: 'en-US-AvaMultilingualNeural', DisplayName: 'Ava Multilingual', Locale: 'en-US', Gender: 'Female', VoiceType: 'Neural', Status: 'GA', SecondaryLocaleList: ['hi-IN', 'fr-FR'] },
  { ShortName: 'en-US-AndrewMultilingualNeural', DisplayName: 'Andrew Multilingual', Locale: 'en-US', Gender: 'Male', VoiceType: 'Neural', Status: 'GA', SecondaryLocaleList: ['hi-IN'] },
  { ShortName: 'en-US-JennyNeural', DisplayName: 'Jenny', Locale: 'en-US', Gender: 'Female', VoiceType: 'Neural', Status: 'GA', StyleList: ['whispering', 'cheerful'] },
  { ShortName: 'fr-FR-DeniseNeural', DisplayName: 'Denise', Locale: 'fr-FR', Gender: 'Female', VoiceType: 'Neural', Status: 'GA' },
  { ShortName: 'en-IN-OldNeural', DisplayName: 'Old', Locale: 'en-IN', Gender: 'Male', VoiceType: 'Neural', Status: 'Deprecated' },
];

// mode: 'ok' | 'reject' (401) | 'slow' (waits az.delay ms) | 'down' (500) ; az.speaks = synthesis requests, az.lists = voice-list requests
export async function fakeAzure() {
  const az = { mode: 'ok', delay: 6000, speaks: [], lists: 0, aborted: 0 };
  const srv = await httpServer(async (req, res, body) => {
    if (az.mode === 'reject') { res.writeHead(401); res.end('denied'); return; }
    if (req.method === 'GET' && req.url.includes('voices/list')) { az.lists++; res.writeHead(200, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(CATALOG)); return; }
    az.speaks.push({ key: req.headers['ocp-apim-subscription-key'], type: req.headers['content-type'], fmt: req.headers['x-microsoft-outputformat'], body });
    if (az.mode === 'down') { res.writeHead(500); res.end('oops'); return; }
    if (az.mode === 'slow') { let gone = false; res.on('close', () => { if (!res.writableEnded) { gone = true; az.aborted++; } }); await sleep(az.delay); if (gone) return; }
    res.writeHead(200, { 'Content-Type': 'audio/mpeg' }); res.end(Buffer.alloc(3000, 7)); });
  az.url = srv.url; az.close = srv.close; az.env = { IXC_TTS_FAKE: '0', IXC_EP_AZURE_TTS: srv.url + '/cognitiveservices/v1', IXC_EP_AZURE_VOICES: srv.url + '/cognitiveservices/voices/list' };
  return az; }
export const KEY = 'Abcdefghij0123456789ABCDEFGHIJ01';
