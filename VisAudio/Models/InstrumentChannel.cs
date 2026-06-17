using System.Collections.Generic;
using NAudio.Dsp;

namespace VisAudio.Models
{
    public class InstrumentChannel
    {
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public double Volume { get; set; } = 1.0;
        public double LowFreq { get; set; }
        public double HighFreq { get; set; }
        public string Icon { get; set; } = "🎵";
        public List<BiQuadFilter> Filters { get; set; } = new();

        public InstrumentChannel(string name, double lowFreq, double highFreq, string icon = "🎵")
        {
            Name = name;
            LowFreq = lowFreq;
            HighFreq = highFreq;
            Icon = icon;
        }
    }
}
