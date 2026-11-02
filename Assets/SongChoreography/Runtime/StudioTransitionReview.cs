using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SongChoreography
{
    public sealed partial class StudioApp
    {
        [Serializable]class PhaseEdge {public string from="",to="";public bool candidate;}
        [Serializable]class PhaseEdges {public List<PhaseEdge> connections=new List<PhaseEdge>();}
        [Serializable]class TransitionCase
        {
            public string from,to,character,screenshot;
            public int samples;
            public float lowestMesh=100,maxFramePosition,maxFrameAngle,cutPositionGap;
            public float maxNormalizedJoint;
            public string worstJoint="";
            public bool jointRangesPassed=true;
            public bool passed;
        }
        [Serializable]class TransitionReport
        {
            public string version=Application.version;
            public int schemaVersion=1,expectedCases;
            public bool allPassed,visualReviewRequired=true;
            public string error="";
            public List<TransitionCase> cases=new List<TransitionCase>();
        }
        List<Texture2D> reviewFrames;
        readonly List<string> reviewFrameLabels=new List<string>();
        string reviewBoardTitle="",reviewBoardSubtitle="";
        TransitionReport transitionReport;
        string transitionFolder;

        IEnumerator TransitionReview()
        {
            transitionReport=new TransitionReport();transitionFolder=Path.Combine(Path.GetDirectoryName(Application.dataPath),"TransitionReview");Directory.CreateDirectory(transitionFolder);
            var iterator=TransitionScenarios();
            while(true)
            {
                bool more=false;object current=null;
                try{more=iterator.MoveNext();if(more)current=iterator.Current;}
                catch(Exception ex){transitionReport.error=ex.ToString();Debug.LogException(ex);break;}
                if(!more)break;yield return current;
            }
            (iterator as IDisposable)?.Dispose();ClearReviewBoard();
            transitionReport.allPassed=string.IsNullOrEmpty(transitionReport.error)&&transitionReport.expectedCases>0&&transitionReport.cases.Count==transitionReport.expectedCases&&transitionReport.cases.All(c=>c.passed);
            File.WriteAllText(Path.Combine(transitionFolder,"results.json"),JsonUtility.ToJson(transitionReport,true));
            var html=new StringBuilder("<!doctype html><meta charset='utf-8'><title>动作连接检查</title><style>body{background:#0d1722;color:#deedf3;font:16px system-ui;margin:32px}img{width:100%;max-width:1400px}article{margin:32px 0}a{color:#50dfc3}</style><h1>逐连接检查</h1><p>120 Hz 音乐时间采样，实际蒙皮与骨骼测量。通过技术检查后，仍需逐图复查舞蹈自然度；不会自动批准动作。</p>");
            html.Append("<p>").Append(transitionReport.allPassed?"全部技术检查通过":"存在未通过项").Append(" · ").Append(transitionReport.cases.Count).Append(" / ").Append(transitionReport.expectedCases).Append(" 项</p>");
            if(!string.IsNullOrEmpty(transitionReport.error))html.Append("<pre>").Append(System.Net.WebUtility.HtmlEncode(transitionReport.error)).Append("</pre>");
            foreach(var item in transitionReport.cases)
            {
                html.Append("<article><h2>").Append(System.Net.WebUtility.HtmlEncode(item.character+" / "+item.from+" → "+item.to)).Append("</h2><p>").Append(item.passed?"技术检查通过":"需修正").Append(" · 每帧最大位移 ").Append((item.maxFramePosition*1000).ToString("0.0")).Append(" mm · 每帧最大旋转 ").Append(item.maxFrameAngle.ToString("0.0")).Append("° · 切换瞬间位移 ").Append((item.cutPositionGap*1000).ToString("0.00")).Append(" mm</p><img src='").Append(item.screenshot).Append("'></article>");
            }
            File.WriteAllText(Path.Combine(transitionFolder,"report.html"),html.ToString());
            yield return new WaitForSecondsRealtime(.5f);Application.Quit(transitionReport.allPassed?0:3);
        }

        IEnumerator TransitionScenarios()
        {
            yield return null;Guard(()=>UseBuiltin(0));while(loading)yield return null;
            if(!Ready)throw new InvalidOperationException("Transition review music failed: "+error);
            playback.Pause();inspectorTab=2;ResetCamera();
            var edges=JsonUtility.FromJson<PhaseEdges>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"MotionCredits","phase-review.json"))).connections.Where(e=>e.candidate).ToList();
            bool personalReview=catalog.personalUseOnly&&!Environment.GetCommandLineArgs().Contains("-review-charleston");
            if(personalReview)
            {
                var candidates=catalog.motions.Where(m=>m.personalUseOnly).ToArray();
                bool approvedOnly=candidates.All(m=>m.approved)&&!Environment.GetCommandLineArgs().Contains("-review-all-candidates");
                edges=candidates.SelectMany(a=>candidates.Where(b=>a.id!=b.id&&(!approvedOnly||a.safeNext.Contains(b.id))).Select(b=>new PhaseEdge{from=a.id,to=b.id,candidate=true})).ToList();
            }
            else
            {
                var approved=catalog.motions.Where(m=>m.approved&&m.style==DanceStyles.Charleston).ToArray();
                if(approved.Length>=8&&!Environment.GetCommandLineArgs().Contains("-review-all-candidates"))edges=approved.SelectMany(a=>a.safeNext.Select(id=>new PhaseEdge{from=a.id,to=id,candidate=true})).ToList();
            }
            transitionReport.expectedCases=edges.Count*2;
            var sequence=project.segments;string oldTitle=project.title,oldCharacter=selectedCharacter;
            var oldLinks=catalog.motions.ToDictionary(m=>m.id,m=>new List<string>(m.safeNext));
            foreach(var motion in catalog.motions.Where(m=>personalReview?m.personalUseOnly:(m.style=="charleston-review"||m.style==DanceStyles.Charleston)))motion.safeNext=edges.Where(e=>e.from==motion.id).Select(e=>e.to).ToList();
            var mesh=new Mesh();
            try
            {
                foreach(string sex in new[]{"female","male"})
                {
                    SwitchCharacter(sex);yield return null;ResetCamera();
                    var body=characterObject.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();
                    var watched=Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>().Where(b=>b!=HumanBodyBones.LastBone).Select(b=>diagnosticAnimator.GetBoneTransform(b)).Where(b=>b).ToArray();
                    foreach(var edge in edges)
                    {
                        double period=60/project.grid.bpm,cut=period*8;
                        project.segments=new List<DanceSegment>{new DanceSegment{motionId=edge.from,startSeconds=0,endSeconds=cut,beatCount=8},new DanceSegment{motionId=edge.to,startSeconds=cut,endSeconds=cut*2,beatCount=8}};
                        var result=new TransitionCase{from=edge.from,to=edge.to,character=sex,screenshot=sex+"-"+edge.from+"--"+edge.to+".png"};
                        playback.Seek(cut-.00001);var beforeCut=watched.Select(t=>t.position).ToArray();playback.Seek(cut);
                        result.cutPositionGap=watched.Select((t,i)=>Vector3.Distance(t.position,beforeCut[i])).Max();
                        Vector3[] previous=null;Quaternion[] rotations=null;
                        using(var probe=new JointRangeProbe(diagnosticAnimator))
                        {
                            for(double time=cut-.15;time<=cut+.65;time+=1.0/120)
                            {
                                playback.Seek(time);probe.Sample();body.BakeMesh(mesh);result.lowestMesh=Math.Min(result.lowestMesh,mesh.vertices.Min(v=>body.transform.TransformPoint(v).y));result.samples++;
                                if(previous!=null)
                                {
                                    result.maxFramePosition=Math.Max(result.maxFramePosition,watched.Select((t,i)=>Vector3.Distance(t.position,previous[i])).Max());
                                    result.maxFrameAngle=Math.Max(result.maxFrameAngle,watched.Select((t,i)=>Quaternion.Angle(t.rotation,rotations[i])).Max());
                                }
                                previous=watched.Select(t=>t.position).ToArray();rotations=watched.Select(t=>t.rotation).ToArray();
                            }
                            result.jointRangesPassed=probe.Passed;result.maxNormalizedJoint=probe.Maximum;result.worstJoint=probe.WorstMuscle;
                        }
                        result.passed=result.jointRangesPassed&&result.lowestMesh>=-.005f&&result.cutPositionGap<.003f&&result.maxFramePosition<.05f&&result.maxFrameAngle<12;
                        transitionReport.cases.Add(result);
                        ClearReviewBoard();reviewFrames=new List<Texture2D>();reviewBoardTitle=sex+" · "+edge.from+" → "+edge.to;
                        reviewBoardSubtitle=(result.passed?"技术检查通过":"需修正")+$"  |  切换位移 {result.cutPositionGap*1000:0.00} mm  |  每帧最大旋转 {result.maxFrameAngle:0.0}°  |  仍需视觉复查";
                        foreach(float offset in new[]{-.12f,0,.06f,.15f,.30f,.48f})
                        {
                            playback.Seek(cut+offset);yield return null;stageCamera.Render();
                            var previousTarget=RenderTexture.active;RenderTexture.active=preview;
                            var texture=new Texture2D(preview.width,preview.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,preview.width,preview.height),0,0);texture.Apply();RenderTexture.active=previousTarget;
                            reviewFrames.Add(texture);reviewFrameLabels.Add(offset==0?"连接点":$"连接前后 {offset:+0.00;-0.00} 秒");
                        }
                        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(transitionFolder,result.screenshot));yield return null;
                        Debug.Log((result.passed?"TRANSITION_PASS ":"TRANSITION_FAIL ")+sex+" "+edge.from+" -> "+edge.to+" position="+result.maxFramePosition+", angle="+result.maxFrameAngle);
                    }
                }
            }
            finally
            {
                ClearReviewBoard();Destroy(mesh);project.segments=sequence;project.title=oldTitle;
                foreach(var motion in catalog.motions)motion.safeNext=oldLinks[motion.id];
                SwitchCharacter(oldCharacter);ResetCamera();playback.Seek(0);dirty=false;
            }
        }
        void ClearReviewBoard()
        {if(reviewFrames!=null)foreach(var texture in reviewFrames)if(texture)Destroy(texture);reviewFrames=null;reviewFrameLabels.Clear();}
        void DrawReviewBoard()
        {
            Text(24,19,1550,32,reviewBoardTitle,heading);Text(24,62,1550,30,reviewBoardSubtitle,small);
            for(int i=0;i<reviewFrames.Count;i++)
            {
                float x=24+i%3*520,y=120+i/3*355;
                GUI.DrawTexture(new Rect(x,y,506,284),reviewFrames[i],ScaleMode.ScaleToFit);
                Text(x,y+292,506,27,reviewFrameLabels[i],small);
            }
        }
    }
}
