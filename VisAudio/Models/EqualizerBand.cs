namespace VisAudio.Models
{
    public class EqualizerBand
    {
        public string Label { get; set; } = string.Empty;
        public string ShortLabel { get; set; } = string.Empty;
        public double CenterFrequency { get; set; }
        public double Bandwidth { get; set; }
        public double GainDb { get; set; }
        public double MinGain { get; set; } = -12;
        public double MaxGain { get; set; } = 12;

        public EqualizerBand(string label, string shortLabel, double centerFreq, double bandwidth = 1.5)
        {
            Label = label;
            ShortLabel = shortLabel;
            CenterFrequency = centerFreq;
            Bandwidth = bandwidth;
        }
    }
}
