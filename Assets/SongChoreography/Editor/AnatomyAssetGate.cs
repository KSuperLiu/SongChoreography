using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SongChoreography.Editor
{
    public static class AnatomyAssetGate
    {
        [Serializable]class Case {public string id,character,worstMuscle;public int samples;public float maximumNormalized,minimumBendDegrees;public bool passed;}
        [Serializable]class Report {public int schemaVersion=1;public float normalizedTolerance=JointRangeProbe.Tolerance;public List<Case> cases=new List<Case>();}
        static readonly Report measured=new Report();
        public static void Validate(MotionDefinition definition)
        {
            foreach(string sex in new[]{"female","male"})
            {
                var character=DancerCharacters.Create(sex);var animator=character.GetComponentInChildren<Animator>();
                var all=animator.GetComponentsInChildren<Transform>();var bindP=all.Select(t=>t.localPosition).ToArray();var bindQ=all.Select(t=>t.localRotation).ToArray();
                var clip=Resources.Load<AnimationClip>(definition.resource);PlayableGraph graph=default(PlayableGraph);
                try
                {
                    var data=definition.localRigMotion?Resources.Load<LocalRigMotionAsset>(definition.resource+"-rig"+(sex=="male"?"-male":"")):null;
                    Transform[] bones=null;AnimationClipPlayable player=default(AnimationClipPlayable);
                    if(definition.localRigMotion)
                    {
                        if(!data||data.schemaVersion!=2||data.characterId!=sex)throw new InvalidDataException("角色专用轨道缺失："+definition.id+" / "+sex);
                        bones=data.paths.Select(p=>animator.transform.Find(p)).ToArray();animator.enabled=false;
                    }
                    else
                    {graph=PlayableGraph.Create("Joint range validation");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);player=AnimationClipPlayable.Create(graph,clip);player.SetSpeed(0);player.SetApplyFootIK(definition.footIK);player.SetApplyPlayableIK(false);var output=AnimationPlayableOutput.Create(graph,"pose",animator);output.SetSourcePlayable(player);graph.Play();}
                    using(var probe=new JointRangeProbe(animator))
                    {
                        int frames=Mathf.CeilToInt(clip.length*120);
                        for(int frame=0;frame<=frames;frame++)
                        {
                            float time=Mathf.Min(clip.length-.00001f,frame/120f);
                            if(data)
                            {
                                for(int i=0;i<all.Length;i++){all[i].localPosition=bindP[i];all[i].localRotation=bindQ[i];}
                                float at=Mathf.Clamp(time*data.fps,0,data.frameCount-1);int a=Mathf.FloorToInt(at),b=Math.Min(a+1,data.frameCount-1);float mix=at-a;
                                for(int j=0;j<52;j++)
                                {int k=a*208+j*4,n=b*208+j*4;bones[j].localRotation=Quaternion.Slerp(new Quaternion(data.rotations[k],data.rotations[k+1],data.rotations[k+2],data.rotations[k+3]),new Quaternion(data.rotations[n],data.rotations[n+1],data.rotations[n+2],data.rotations[n+3]),mix);}
                                bones[0].localPosition=Vector3.Lerp(data.hips[a],data.hips[b],mix);
                            }
                            else {player.SetTime(time);graph.Evaluate(0);}
                            if(!probe.Sample())throw new InvalidDataException("关节范围验收失败："+definition.id+" / "+sex+" / "+time+"s / "+probe.FailureReason);
                        }
                        Debug.Log("JOINT_RANGE_PASS "+definition.id+" / "+sex+" / samples="+probe.Samples+" / maximum="+probe.Maximum+" / minimumBend="+probe.MinimumBendDegrees);
                        measured.cases.RemoveAll(c=>c.id==definition.id&&c.character==sex);
                        measured.cases.Add(new Case{id=definition.id,character=sex,worstMuscle=probe.WorstMuscle,samples=probe.Samples,maximumNormalized=probe.Maximum,minimumBendDegrees=probe.MinimumBendDegrees,passed=probe.Passed});
                        Directory.CreateDirectory("Builds/Windows/JointRange");File.WriteAllText("Builds/Windows/JointRange/results.json",JsonUtility.ToJson(measured,true));
                    }
                }
                finally{if(graph.IsValid())graph.Destroy();UnityEngine.Object.DestroyImmediate(character);}
            }
        }
    }
}
