using System;
using System.Linq;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class AnatomicalPoseCleanup
    {
        public static bool IsFinger(string name)=>new[]{"Thumb","Index","Middle","Ring","Little"}.Any(name.Contains);
        public static void Apply(Animator animator,Transform[] bones,float[][] tracks,int count)
        {
            var all=animator.GetComponentsInChildren<Transform>();var bindP=all.Select(t=>t.localPosition).ToArray();var bindQ=all.Select(t=>t.localRotation).ToArray();
            var channels=HumanTrait.MuscleName.Select((name,i)=>new{name,i}).ToArray();
            var values=channels.Select(_=>new float[count+1]).ToArray();
            Action<int> sample=frame=>
            {
                for(int i=0;i<all.Length;i++){all[i].localPosition=bindP[i];all[i].localRotation=bindQ[i];}
                for(int j=0;j<52;j++)bones[j].localRotation=new Quaternion(tracks[j*4][frame],tracks[j*4+1][frame],tracks[j*4+2][frame],tracks[j*4+3][frame]);
                bones[0].localPosition=new Vector3(tracks[208][frame],tracks[209][frame],tracks[210][frame]);
            };
            using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
            {
                var pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
                for(int frame=0;frame<=count;frame++)
                {
                    sample(frame);handler.GetHumanPose(ref pose);
                    for(int c=0;c<channels.Length;c++)
                    {
                        string name=channels[c].name;float limit=name.Contains("Foot")||name.Contains("Toes")?.9f:.97f;
                        float minimum=name.Contains("Spread")?-.3f:IsFinger(name)?-.35f:-limit,maximum=name.Contains("Spread")?.3f:IsFinger(name)?.60f:limit;
                        if(name.Contains("Forearm Stretch")||name.Contains("Lower Leg Stretch"))maximum=Mathf.Min(maximum,.94f);
                        float wristDegrees=name.Contains("Hand Down-Up")?60:name.Contains("Hand In-Out")?20:0;
                        if(wristDegrees>0){minimum=Mathf.Max(minimum,-wristDegrees/Mathf.Abs(HumanTrait.GetMuscleDefaultMin(channels[c].i)));maximum=Mathf.Min(maximum,wristDegrees/HumanTrait.GetMuscleDefaultMax(channels[c].i));}
                        values[c][frame]=Mathf.Clamp(pose.muscles[channels[c].i],minimum,maximum);
                    }
                }
                for(int frame=0;frame<=count;frame++)
                {
                    sample(frame);handler.GetHumanPose(ref pose);
                    // Correct coordinate handedness and anatomical calibration
                    // keep the body away from the old decomposition poles.
                    // Export solved local quaternions, never unbounded muscles.
                    for(int c=0;c<channels.Length;c++)
                    {
                        float smooth=0,total=0;
                        for(int offset=-2;offset<=2;offset++){float weight=3-Mathf.Abs(offset);smooth+=values[c][Mathf.Clamp(frame+offset,0,count)]*weight;total+=weight;}
                        float influence=Mathf.SmoothStep(0,1,Mathf.Min(frame,count-frame)/3f);
                        pose.muscles[channels[c].i]=Mathf.Lerp(values[c][frame],smooth/total,influence);
                    }
                    handler.SetHumanPose(ref pose);
                    for(int j=0;j<52;j++)
                    {
                        var q=bones[j].localRotation;if(frame>0&&q.x*tracks[j*4][frame-1]+q.y*tracks[j*4+1][frame-1]+q.z*tracks[j*4+2][frame-1]+q.w*tracks[j*4+3][frame-1]<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                        tracks[j*4][frame]=q.x;tracks[j*4+1][frame]=q.y;tracks[j*4+2][frame]=q.z;tracks[j*4+3][frame]=q.w;
                    }
                    tracks[208][frame]=bones[0].localPosition.x;tracks[209][frame]=bones[0].localPosition.y;tracks[210][frame]=bones[0].localPosition.z;
                }
            }
        }
    }
}
