using System;
using System.IO;

namespace SongChoreography
{
    public static class AnalyzerRuntime
    {
        public static string BundledPath(string applicationDirectory)=>Path.Combine(applicationDirectory,"Runtime","Python","python.exe");
        public static string Resolve(string applicationDirectory,string preference,string environmentOverride)
        {
            if(!string.IsNullOrWhiteSpace(environmentOverride))return environmentOverride.Trim();
            if(!string.IsNullOrWhiteSpace(preference)&&preference.Trim()!="python")return preference.Trim();
            string bundled=BundledPath(applicationDirectory);
            return File.Exists(bundled)?bundled:"python";
        }
    }
}
