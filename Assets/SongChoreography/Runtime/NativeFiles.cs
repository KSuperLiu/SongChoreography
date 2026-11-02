using System;
using System.Runtime.InteropServices;

namespace SongChoreography
{
    public static class NativeFiles
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        class OpenFileName
        {
            public int structSize=Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr owner=IntPtr.Zero,instance=IntPtr.Zero;
            public string filter;
            public string customFilter=null;
            public int maxCustomFilter=0,filterIndex=1;
            [MarshalAs(UnmanagedType.LPWStr)] public string file=new string('\0',32768);
            public int maxFile=32768;
            public string fileTitle=null;
            public int maxFileTitle=0;
            public string initialDir=null,title;
            public int flags=0x00080000|0x00001000|0x00000008; // Explorer, existing file, don't change cwd
            public short fileOffset,extensionOffset;
            public string defaultExt=null;
            public IntPtr customData=IntPtr.Zero,hook=IntPtr.Zero;
            public string templateName=null;
            public IntPtr reserved=IntPtr.Zero;
            public int reservedWord,flagsEx;
        }
        [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetOpenFileName([In,Out]OpenFileName file);
        public static string Open(bool project)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var f=new OpenFileName{filter=project?"编舞项目\0*.danceproj\0\0":"音乐 WAV / MP3\0*.wav;*.mp3\0\0",title=project?"打开编舞项目":"选择本地音乐"};
            return GetOpenFileName(f)?f.file.Split('\0')[0]:null;
#else
            return null;
#endif
        }
    }
}
