using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SongChoreography
{
    [Serializable] public class BuiltinMusicTrack
    {
        public string id="",title="",artist="",genre="",file="",sha256="",sourceUrl="",licenseName="",licenseUrl="";
        public double durationSeconds;
    }
    [Serializable] public class BuiltinMusicCatalog
    {
        public int schemaVersion=1;
        public string purpose="";
        public List<BuiltinMusicTrack> tracks=new List<BuiltinMusicTrack>();
    }
    public static class MusicLibrary
    {
        public static string ResolveTrack(string root,BuiltinMusicTrack track)
        {
            if(track==null||string.IsNullOrWhiteSpace(track.id)||string.IsNullOrWhiteSpace(track.title)||
               !Choreographer.Finite(track.durationSeconds)||track.durationSeconds<30)
                throw new InvalidDataException("内置音乐信息无效。");
            string path=Choreographer.ResolveLocal(root,track.file);
            if(!string.Equals(Path.GetExtension(path),".mp3",StringComparison.OrdinalIgnoreCase)&&
               !string.Equals(Path.GetExtension(path),".wav",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("内置音乐须为 WAV 或 MP3。");
            if(!File.Exists(path))throw new FileNotFoundException("内置音乐文件缺失："+track.title,path);
            return path;
        }
        public static void Validate(BuiltinMusicCatalog catalog,string root)
        {
            if(catalog==null||catalog.schemaVersion!=1||catalog.tracks==null||catalog.tracks.Any(t=>t==null)||
               catalog.tracks.Select(t=>t.id).Distinct().Count()!=catalog.tracks.Count)
                throw new InvalidDataException("内置音乐清单无效。");
            foreach(var track in catalog.tracks)ResolveTrack(root,track);
        }
    }
}
