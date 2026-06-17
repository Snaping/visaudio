using System;

namespace VisAudio.Models
{
    public class LrcLine : IComparable<LrcLine>
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; } = string.Empty;

        public int CompareTo(LrcLine? other)
        {
            if (other == null) return 1;
            return Timestamp.CompareTo(other.Timestamp);
        }

        public override string ToString() => $"[{Timestamp:mm\\:ss\\.ff}] {Text}";
    }
}
