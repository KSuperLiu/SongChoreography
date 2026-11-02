using System;
using System.IO;

namespace SongChoreography
{
    public static class ProjectLocations
    {
        public static string BrowseDirectory(string currentFolder,string lastProject,string workspace)
        {
            if(Directory.Exists(currentFolder))return Path.GetFullPath(currentFolder);
            if(File.Exists(lastProject))return Path.GetDirectoryName(Path.GetFullPath(lastProject));
            if(Directory.Exists(workspace))return Path.GetFullPath(workspace);
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
