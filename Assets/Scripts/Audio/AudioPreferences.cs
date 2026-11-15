using System;

namespace KLTN.Audio
{
    /// <summary>Independent audio channels stored as integer percentages.</summary>
    [Serializable]
    public sealed class AudioPreferences
    {
        public int music = 75;
        public int ui = 75;
        public int combat = 75;
        public bool musicMuted;
        public bool uiMuted;

        public AudioPreferences Copy() => (AudioPreferences)MemberwiseClone();
        public bool SameAs(AudioPreferences other) => other != null
            && music == other.music && ui == other.ui && combat == other.combat
            && musicMuted == other.musicMuted && uiMuted == other.uiMuted;
        public bool IsValid() => ValidLevel(music) && ValidLevel(ui)
            && ValidLevel(combat) && (music != 0 || musicMuted)
            && (ui != 0 || uiMuted);
        private static bool ValidLevel(int value) => value == 75
            || (value >= 0 && value <= 100 && value % 10 == 0);
    }
}
