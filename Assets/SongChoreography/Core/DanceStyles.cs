using System;

namespace SongChoreography
{
    public static class DanceStyles
    {
        public const string Diagnostic="diagnostic",NewJazz="new-jazz",Charleston="charleston";
        public static bool IsKnown(string id)=>id==Diagnostic||id==NewJazz||id==Charleston;
        public static int SlotBeats(string id)=>8;
        public static string Name(string id)
        {
            switch(id)
            {
                case Diagnostic:return "技术演示";
                case NewJazz:return "New Jazz";
                case Charleston:return "Charleston";
                default:throw new ArgumentException("未知舞种："+id);
            }
        }
    }
}
