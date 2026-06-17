using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisAudio.Controls;

public class SpectrumControl : Control
{
    static SpectrumControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SpectrumControl),
            new FrameworkPropertyMetadata(typeof(SpectrumControl)));
        HeightProperty.OverrideMetadata(typeof(SpectrumControl),
            new FrameworkPropertyMetadata(150.0));
        BackgroundProperty.OverrideMetadata(typeof(SpectrumControl),
            new FrameworkPropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"))));
    }

    public static readonly DependencyProperty FftDataProperty =
        DependencyProperty.Register(nameof(FftData), typeof(float[]), typeof(SpectrumControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarCountProperty =
        DependencyProperty.Register(nameof(BarCount), typeof(int), typeof(SpectrumControl),
            new FrameworkPropertyMetadata(64, FrameworkPropertyMetadataOptions.AffectsRender));

    public float[] FftData
    {
        get => (float[])GetValue(FftDataProperty);
        set => SetValue(FftDataProperty, value);
    }

    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var renderSize = RenderSize;
        var width = renderSize.Width;
        var height = renderSize.Height;

        var bgBrush = Background;
        if (bgBrush != null)
            dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

        var fftData = FftData;
        if (fftData == null || fftData.Length == 0)
            return;

        var barCount = Math.Max(1, BarCount);
        var gap = 1.0;
        var totalGaps = (barCount - 1) * gap;
        var barWidth = Math.Max(1, (width - totalGaps) / barCount);

        var green = Colors.Green;
        var red = Colors.Red;

        for (int i = 0; i < barCount; i++)
        {
            var logStart = Math.Log(i + 1, barCount + 1);
            var logEnd = Math.Log(i + 2, barCount + 1);
            var binStart = (int)(logStart * fftData.Length);
            var binEnd = (int)(logEnd * fftData.Length);
            binStart = Math.Clamp(binStart, 0, fftData.Length - 1);
            binEnd = Math.Clamp(binEnd, binStart + 1, fftData.Length);

            float magnitude = 0f;
            for (int b = binStart; b < binEnd; b++)
                magnitude = Math.Max(magnitude, fftData[b]);

            magnitude = Math.Min(magnitude, 1.0f);

            var barHeight = magnitude * height;
            var x = i * (barWidth + gap);
            var y = height - barHeight;

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0),
                GradientStops =
                {
                    new GradientStop(green, 0.0),
                    new GradientStop(Color.FromRgb(
                        (byte)(green.R + (red.R - green.R) * 0.5),
                        (byte)(green.G + (red.G - green.G) * 0.5),
                        (byte)(green.B + (red.B - green.G) * 0.5)), 0.5),
                    new GradientStop(red, 1.0)
                }
            };

            var radius = Math.Min(barWidth / 2.0, 3.0);
            var rect = new Rect(x, y, barWidth, barHeight);
            dc.DrawRoundedRectangle(brush, null, rect, radius, radius);
        }
    }
}
