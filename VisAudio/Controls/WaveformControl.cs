using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisAudio.Controls;

public class WaveformControl : Control
{
    static WaveformControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(WaveformControl),
            new FrameworkPropertyMetadata(typeof(WaveformControl)));
        HeightProperty.OverrideMetadata(typeof(WaveformControl),
            new FrameworkPropertyMetadata(120.0));
        BackgroundProperty.OverrideMetadata(typeof(WaveformControl),
            new FrameworkPropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"))));
    }

    public static readonly DependencyProperty WaveformDataProperty =
        DependencyProperty.Register(nameof(WaveformData), typeof(float[]), typeof(WaveformControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayPositionProperty =
        DependencyProperty.Register(nameof(PlayPosition), typeof(double), typeof(WaveformControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LoopStartPositionProperty =
        DependencyProperty.Register(nameof(LoopStartPosition), typeof(double?), typeof(WaveformControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LoopEndPositionProperty =
        DependencyProperty.Register(nameof(LoopEndPosition), typeof(double?), typeof(WaveformControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public float[] WaveformData
    {
        get => (float[])GetValue(WaveformDataProperty);
        set => SetValue(WaveformDataProperty, value);
    }

    public double PlayPosition
    {
        get => (double)GetValue(PlayPositionProperty);
        set => SetValue(PlayPositionProperty, value);
    }

    public double? LoopStartPosition
    {
        get => (double?)GetValue(LoopStartPositionProperty);
        set => SetValue(LoopStartPositionProperty, value);
    }

    public double? LoopEndPosition
    {
        get => (double?)GetValue(LoopEndPositionProperty);
        set => SetValue(LoopEndPositionProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var renderSize = RenderSize;
        var width = renderSize.Width;
        var height = renderSize.Height;

        var bgBrush = Background;
        if (bgBrush != null)
            dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

        var centerY = height / 2.0;
        var centerPen = new Pen(new SolidColorBrush(Colors.Gray), 0.5);
        dc.DrawLine(centerPen, new Point(0, centerY), new Point(width, centerY));

        var data = WaveformData;
        if (data is { Length: > 0 })
        {
            var cyan = (Color)ColorConverter.ConvertFromString("#00E5FF");
            var magenta = (Color)ColorConverter.ConvertFromString("#FF00FF");
            var halfHeight = height / 2.0;

            for (int i = 0; i < data.Length; i++)
            {
                var x = (double)i / data.Length * width;
                var amplitude = Math.Min(Math.Abs(data[i]), 1.0);
                var barHeight = amplitude * halfHeight;

                var color = Color.FromRgb(
                    (byte)(cyan.R + (magenta.R - cyan.R) * amplitude),
                    (byte)(cyan.G + (magenta.G - cyan.G) * amplitude),
                    (byte)(cyan.B + (magenta.B - cyan.B) * amplitude));

                var pen = new Pen(new SolidColorBrush(color), Math.Max(1.0, width / data.Length));
                dc.DrawLine(pen, new Point(x, centerY - barHeight), new Point(x, centerY + barHeight));
            }
        }

        if (LoopStartPosition.HasValue && LoopEndPosition.HasValue)
        {
            var loopStartX = LoopStartPosition.Value * width;
            var loopEndX = LoopEndPosition.Value * width;
            var loopRect = new Rect(loopStartX, 0, loopEndX - loopStartX, height);
            var loopFill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0));
            dc.DrawRectangle(loopFill, null, loopRect);
        }

        if (LoopStartPosition.HasValue)
        {
            var loopStartX = LoopStartPosition.Value * width;
            var loopPen = new Pen(new SolidColorBrush(Colors.Green), 1.0) { DashStyle = DashStyles.Dash };
            dc.DrawLine(loopPen, new Point(loopStartX, 0), new Point(loopStartX, height));
        }

        if (LoopEndPosition.HasValue)
        {
            var loopEndX = LoopEndPosition.Value * width;
            var loopPen = new Pen(new SolidColorBrush(Colors.Green), 1.0) { DashStyle = DashStyles.Dash };
            dc.DrawLine(loopPen, new Point(loopEndX, 0), new Point(loopEndX, height));
        }

        if (PlayPosition >= 0.0 && PlayPosition <= 1.0)
        {
            var playX = PlayPosition * width;
            var playPen = new Pen(Brushes.Red, 1.5);
            dc.DrawLine(playPen, new Point(playX, 0), new Point(playX, height));
        }
    }
}
