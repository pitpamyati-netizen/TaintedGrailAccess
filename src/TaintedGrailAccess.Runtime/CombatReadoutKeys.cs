using System;

namespace TaintedGrailAccess.Runtime
{
    public enum CombatReadout { Health, Energy, Barrier, ClassResource, Effects, Intents, ShortIntents, Hand }

    /// <summary>Passive, exact info chords. A held press stays consumed without repeating speech.</summary>
    public static class CombatReadoutKeys
    {
        public static bool TryGet(KeyChord chord, out CombatReadout readout)
        {
            readout = default(CombatReadout);
            if (chord.Modifiers == Modifiers.Control)
            {
                switch (chord.Key)
                {
                    case UiKey.H: readout = CombatReadout.Health; return true;
                    case UiKey.Y: readout = CombatReadout.Energy; return true;
                    case UiKey.B: readout = CombatReadout.Barrier; return true;
                    case UiKey.R: readout = CombatReadout.ClassResource; return true;
                    case UiKey.P: readout = CombatReadout.Effects; return true;
                    case UiKey.I: readout = CombatReadout.Intents; return true;
                }
            }
            if (chord.Modifiers == Modifiers.Alt)
            {
                if (chord.Key == UiKey.I) { readout = CombatReadout.ShortIntents; return true; }
                if (chord.Key == UiKey.H) { readout = CombatReadout.Hand; return true; }
            }
            return false;
        }

        public static bool Handle(KeyChord chord, Func<CombatReadout, string> read, ISpeechOutput speech, string unavailable)
        {
            CombatReadout readout;
            if (!TryGet(chord, out readout)) return false;
            if (!chord.IsRepeat)
            {
                var value = read(readout);
                speech?.Speak(string.IsNullOrWhiteSpace(value) ? unavailable : value, true);
            }
            return true;
        }
    }
}
