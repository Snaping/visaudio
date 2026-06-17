using System;

namespace VisAudio.Models
{
    public class PlaylistItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string Title { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string DisplayDuration => Duration.ToString(@"mm\:ss");

        public PlaylistItem(string filePath)
        {
            FilePath = filePath;
            Title = System.IO.Path.GetFileNameWithoutExtension(filePath);
        }

        public override string ToString() => Title;
    }
}
