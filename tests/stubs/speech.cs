// Linux test stub for System.Speech (Windows-only). Behaves like "no offline voices installed".
using System; using System.Collections.ObjectModel; using System.Globalization; using System.IO;
namespace System.Speech.Synthesis {
  public enum VoiceGender { NotSet, Male, Female, Neutral }
  public class VoiceInfo { public string Name { get { return ""; } } public CultureInfo Culture { get { return CultureInfo.InvariantCulture; } } public VoiceGender Gender { get { return VoiceGender.NotSet; } } }
  public class InstalledVoice { public bool Enabled { get { return true; } } public VoiceInfo VoiceInfo { get { return new VoiceInfo(); } } }
  public class SpeechSynthesizer : IDisposable {
    public int Rate { get; set; } public int Volume { get; set; }
    public ReadOnlyCollection<InstalledVoice> GetInstalledVoices() { return new ReadOnlyCollection<InstalledVoice>(new InstalledVoice[0]); }
    public void SelectVoice(string n) { throw new InvalidOperationException("no voices (stub)"); }
    public void SetOutputToAudioStream(Stream s, System.Speech.AudioFormat.SpeechAudioFormatInfo f) { } public void SetOutputToNull() { }
    public void Speak(string t) { throw new InvalidOperationException("no voices (stub)"); } public void Dispose() { } } }
namespace System.Speech.AudioFormat {
  public enum AudioBitsPerSample { Eight = 8, Sixteen = 16 } public enum AudioChannel { Mono = 1, Stereo = 2 }
  public class SpeechAudioFormatInfo { public SpeechAudioFormatInfo(int r, AudioBitsPerSample b, AudioChannel c) { } } }
