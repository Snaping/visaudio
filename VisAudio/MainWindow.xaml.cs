﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using VisAudio.Models;
using VisAudio.ViewModels;

namespace VisAudio;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private bool _isSeeking;
    private Point _dragStartPoint;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is MainViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsPlaying):
                    UpdatePlayPauseButton();
                    break;
                case nameof(MainViewModel.IsMuted):
                    UpdateMuteButton();
                    break;
            }
        });
    }

    private void UpdatePlayPauseButton()
    {
        var btn = FindPlayPauseButton();
        if (btn != null)
            btn.Content = ViewModel.IsPlaying ? "⏸" : "▶";
    }

    private void UpdateMuteButton()
    {
        foreach (var child in FindVisualChildren<Button>(this))
        {
            if (child.ToolTip is string tip && tip == "Mute/Unmute")
            {
                child.Content = ViewModel.IsMuted ? "🔇" : "🔊";
                break;
            }
        }
    }

    private Button? FindPlayPauseButton()
    {
        foreach (var child in FindVisualChildren<Button>(this))
        {
            if (child.ToolTip is string tip && tip == "Play/Pause")
                return child;
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
                yield return t;
            foreach (var grandChild in FindVisualChildren<T>(child))
                yield return grandChild;
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsPlaying)
            ViewModel.Pause();
        else
            ViewModel.Resume();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsMuted = !ViewModel.IsMuted;
    }

    private void Rewind_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentTime = ViewModel.CurrentTime.Subtract(TimeSpan.FromSeconds(5));
        if (ViewModel.CurrentTime < TimeSpan.Zero)
            ViewModel.CurrentTime = TimeSpan.Zero;
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentTime = ViewModel.CurrentTime.Add(TimeSpan.FromSeconds(5));
        if (ViewModel.CurrentTime > ViewModel.TotalDuration)
            ViewModel.CurrentTime = ViewModel.TotalDuration;
    }

    private void ProgressSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = true;
    }

    private void ProgressSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            var position = TimeSpan.FromTicks((long)(ViewModel.TotalDuration.Ticks * slider.Value));
            ViewModel.CurrentTime = position;
        }
        _isSeeking = false;
    }

    private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSeeking && sender is Slider slider)
        {
            var position = TimeSpan.FromTicks((long)(ViewModel.TotalDuration.Ticks * slider.Value));
            ViewModel.CurrentTime = position;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ViewModel != null)
            ViewModel.Volume = (float)e.NewValue;
    }

    private void EqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.DataContext is EqualizerBand band)
        {
            var bands = ViewModel.EqualizerBands;
            int index = bands.IndexOf(band);
            if (index >= 0)
                ViewModel.UpdateBandGainCommand.Execute((index, e.NewValue));
        }
    }

    private void WaveformDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Controls.WaveformControl wf && ViewModel.TotalDuration > TimeSpan.Zero)
        {
            var pos = e.GetPosition(wf);
            var ratio = pos.X / wf.RenderSize.Width;
            ratio = Math.Clamp(ratio, 0, 1);
            ViewModel.CurrentTime = TimeSpan.FromTicks((long)(ViewModel.TotalDuration.Ticks * ratio));
        }
    }

    private void Playlist_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void Playlist_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
        {
            var pos = e.GetPosition(null);
            var diff = _dragStartPoint - pos;
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = true;
                if (sender is ListBox listBox && listBox.SelectedItem != null)
                {
                    var item = (PlaylistItem)listBox.SelectedItem;
                    var data = new DataObject(DataFormats.FileDrop, new[] { item.FilePath });
                    DragDrop.DoDragDrop(listBox, data, DragDropEffects.Copy);
                    _isDragging = false;
                }
            }
        }
    }

    private void Playlist_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null)
            {
                foreach (var file in files)
                {
                    var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".mp3" || ext == ".wav" || ext == ".flac")
                        ViewModel.PlaylistItems.Add(new PlaylistItem(file));
                }
            }
        }

        if (e.Data.GetDataPresent(typeof(int)))
        {
            var sourceIndex = (int)e.Data.GetData(typeof(int));
            var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (targetItem != null && sender is ListBox listBox)
            {
                var targetIndex = listBox.ItemContainerGenerator.IndexFromContainer(targetItem);
                if (targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    var item = ViewModel.PlaylistItems[sourceIndex];
                    ViewModel.PlaylistItems.RemoveAt(sourceIndex);
                    var insertIndex = targetIndex > sourceIndex ? targetIndex - 1 : targetIndex;
                    ViewModel.PlaylistItems.Insert(insertIndex, item);
                    ViewModel.SelectedPlaylistIndex = insertIndex;
                }
            }
        }
    }

    private void Playlist_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Playlist_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.PlaySelectedCommand.Execute(null);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null)
            {
                foreach (var file in files)
                {
                    var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".mp3" || ext == ".wav" || ext == ".flac")
                        ViewModel.PlaylistItems.Add(new PlaylistItem(file));
                }
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T t) return t;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ViewModel.Stop();
    }
}
