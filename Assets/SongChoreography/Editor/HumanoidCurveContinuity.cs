using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class HumanoidCurveContinuity
    {
        static int Axis(string name)
        {
            if(!name.Contains("Twist")&&!name.Contains("Turn")&&!name.Contains("Left-Right"))return -1;
            int index=Array.IndexOf(HumanTrait.MuscleName,name);
            return index>=0&&HumanTrait.GetMuscleDefaultMin(index)<0&&HumanTrait.GetMuscleDefaultMax(index)>0?index:-1;
        }
        static float Degrees(int index,float value)=>value*(value>=0?HumanTrait.GetMuscleDefaultMax(index):-HumanTrait.GetMuscleDefaultMin(index));
        static float Normalized(int index,float degrees)=>degrees/(degrees>=0?HumanTrait.GetMuscleDefaultMax(index):-HumanTrait.GetMuscleDefaultMin(index));
        public static void Unwrap(string name,float[] values,float? referenceDegrees=null)
        {
            int axis=Axis(name);if(axis<0||values.Length==0)return;
            float previous=referenceDegrees??Degrees(axis,values[0]);
            for(int i=0;i<values.Length;i++)
            {
                float angle=Degrees(axis,values[i]);angle-=360*Mathf.Round((angle-previous)/360);
                values[i]=Normalized(axis,angle);previous=angle;
            }
        }
        public static Dictionary<string,float> Reference(AnimationClip clip)
        {
            var result=new Dictionary<string,float>();
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            {
                int axis=Axis(binding.propertyName);if(axis>=0)result[binding.propertyName]=Degrees(axis,AnimationUtility.GetEditorCurve(clip,binding).Evaluate(0));
            }
            return result;
        }
        public static void AlignBranches(AnimationClip clip,Dictionary<string,float> reference)
        {
            int frames=Mathf.RoundToInt(clip.length*30);
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if(!reference.TryGetValue(binding.propertyName,out float start))continue;
                var curve=AnimationUtility.GetEditorCurve(clip,binding);var values=new float[frames+1];
                for(int i=0;i<=frames;i++)values[i]=curve.Evaluate(i/30f);
                Unwrap(binding.propertyName,values,start);AnimationUtility.SetEditorCurve(clip,binding,Curve(values));
            }
            EditorUtility.SetDirty(clip);
        }
        public static AnimationCurve Curve(float[] values,int fps=30)
        {
            int end=values.Length-1;float cycle=values[end]-values[0];var keys=new Keyframe[values.Length];
            for(int i=0;i<=end;i++)
            {
                float before=i==0?values[end-1]-cycle:values[i-1];
                float after=i==end?values[1]+cycle:values[i+1];
                float slope=(after-before)*fps*.5f;keys[i]=new Keyframe(i/(float)fps,values[i],slope,slope);
            }
            return new AnimationCurve(keys);
        }
        public static void SmoothCycle(float[] values,float sigma)
        {
            int count=values.Length-1,radius=Mathf.CeilToInt(sigma*3);
            var original=(float[])values.Clone();float cycle=original[count]-original[0];
            for(int i=0;i<count;i++)
            {
                float sum=0,weight=0;
                for(int offset=-radius;offset<=radius;offset++)
                {
                    int frame=i+offset,turn=Mathf.FloorToInt(frame/(float)count),wrapped=frame-turn*count;
                    float w=Mathf.Exp(-offset*offset/(2*sigma*sigma));sum+=(original[wrapped]+turn*cycle)*w;weight+=w;
                }
                values[i]=sum/weight;
            }
            values[count]=values[0]+cycle;
        }
        public static void Validate()
        {
            const string name="Left Upper Leg Twist In-Out";int axis=Axis(name);
            var values=new[]{Normalized(axis,179),Normalized(axis,-179)};Unwrap(name,values);
            if(Math.Abs(Degrees(axis,values[1])-Degrees(axis,values[0])-2)>.01f)throw new InvalidOperationException("Axial wrap failed to take the short rotation.");
            if(Quaternion.Angle(Quaternion.AngleAxis(Degrees(axis,values[1]),Vector3.up),Quaternion.AngleAxis(-179,Vector3.up))>.01f)throw new InvalidOperationException("Axial wrap changed the physical pose.");
            var branch=new[]{Normalized(axis,-179),Normalized(axis,-178)};Unwrap(name,branch,179);
            float midpoint=(Degrees(axis,values[0])+Degrees(axis,branch[0]))*.5f;
            if(Quaternion.Angle(Quaternion.AngleAxis(midpoint,Vector3.up),Quaternion.AngleAxis(180,Vector3.up))>.01f)throw new InvalidOperationException("Cross-clip angle branches would blend through zero.");
            var winding=Curve(new[]{350f,440f,530f,620f,710f},4);
            if(Math.Abs(winding.keys[0].outTangent-360)>.01f||Math.Abs(winding.keys[4].inTangent-360)>.01f)throw new InvalidOperationException("A winding loop has a discontinuous boundary velocity.");
            var ramp=new[]{0f,90f,180f,270f,360f};SmoothCycle(ramp,1.5f);
            if(Math.Abs(ramp[0])>.01f||Math.Abs(ramp[2]-180)>.01f||Math.Abs(ramp[4]-360)>.01f)throw new InvalidOperationException("Periodic smoothing changed angular winding.");
            Debug.Log("ROTATION_CURVE_TESTS_PASSED 5");
        }
    }
}
