// IXC Core - event messages: a chat message when someone follows, subscribes, gifts, raids, sends a Super Chat or becomes a member
// ("Thanks for the follow, @name!"), posted on the platform where it happened. Replaces Streamer.bot event actions.
// A burst (follow bots, a gift bomb) posts once per event type and platform per events.cooldownSec, never a flood.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Linq;

namespace IXC {
  public static class Events {
    public static readonly string[] Kinds = { "follow", "sub", "gift", "raid", "superchat", "member" };
    public static readonly Dictionary<string, string> Defaults = new Dictionary<string, string> {
      { "follow", "Thanks for the follow, {user}!" }, { "sub", "Thank you for subscribing, {user}!" }, { "gift", "Thank you for the gift, {user}!" },
      { "raid", "Welcome raiders! Thank you for the raid, {user}!" }, { "superchat", "Thank you for the Super Chat, {user}! ({amount})" }, { "member", "Welcome to the membership, {user}!" } };
    static readonly Dictionary<string, DateTime> last = new Dictionary<string, DateTime>();
    public static int Sent, Skipped;

    public static void OnAlert(ChatMsg m) {
      if (m == null || m.Old || !Kinds.Contains(m.Kind) || !Settings.Bool("events." + m.Kind + ".on")) return;
      var tpl = Settings.Str("events." + m.Kind + ".message").Trim(); if (tpl.Length == 0) return;
      var key = m.Platform + "|" + m.Kind; var cd = Settings.Int("events.cooldownSec");
      lock (last) { DateTime t; if (cd > 0 && last.TryGetValue(key, out t) && (DateTime.Now - t).TotalSeconds < cd) { Skipped++; Log.Debug("chat", "event message for " + m.Kind + " skipped (cooldown)"); return; } last[key] = DateTime.Now; }
      var text = Commands.Fill(tpl, m, "", "event:" + m.Kind); if (text.Length == 0) return;
      Sent++; Log.Info("chat", m.Platform + " " + m.Kind + " -> event message"); Chat.Reply(m, text); }
  }
}
