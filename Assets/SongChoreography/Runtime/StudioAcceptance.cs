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
        [Serializable]class AcceptanceCase {public string name,detail;public bool passed;}
        [Serializable]class AcceptanceReport {public string version=Application.version;public bool allPassed;public List<AcceptanceCase> checks=new List<AcceptanceCase>();}
        AcceptanceReport acceptance;
        bool acceptanceFailed;
        string acceptanceFolder;
        void Verify(string name,bool pass,string detail="")
        {
            acceptance.checks.Add(new AcceptanceCase{name=name,passed=pass,detail=detail});
            if(!pass)acceptanceFailed=true;
            Debug.Log((pass?"ACCEPT_PASS ":"ACCEPT_FAIL ")+name+" "+detail);
        }
        IEnumerator RunAcceptanceChecks()
        {
            acceptance=new AcceptanceReport();acceptanceFailed=false;
            acceptanceFolder=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Acceptance");Directory.CreateDirectory(acceptanceFolder);
            var iterator=AcceptanceScenarios();
            while(true)
            {
                bool more=false;object next=null;
                try{more=iterator.MoveNext();if(more)next=iterator.Current;}
                catch(Exception ex){Verify("Unhandled acceptance exception",false,ex.Message);break;}
                if(!more)break;
                yield return next;
            }
            acceptance.allPassed=!acceptanceFailed;
            File.WriteAllText(Path.Combine(acceptanceFolder,"results.json"),JsonUtility.ToJson(acceptance,true));
            var html=new StringBuilder("<!doctype html><meta charset='utf-8'><title>Choreo 角色镜头验证</title><style>body{font:16px system-ui;background:#101923;color:#e5edf3;margin:32px}li{margin:10px}img{width:48%;margin:.5%}a{color:#48dfbf}.fail{color:#ff9b8c}</style><h1>角色与镜头验收</h1><p>应用版本 "+Application.version+"</p>");
            html.Append("<p>").Append(acceptance.allPassed?"全部通过":"存在未通过项").Append(" / ").Append(acceptance.checks.Count).Append(" 项</p><ol>");
            foreach(var check in acceptance.checks)html.Append("<li class='").Append(check.passed?"pass":"fail").Append("'>").Append(System.Net.WebUtility.HtmlEncode(check.name+" — "+(check.passed?"PASS":"FAIL")+" "+check.detail)).Append("</li>");
            html.Append("</ol><h2>逐镜头实际画面</h2>");
            foreach(var file in Directory.GetFiles(acceptanceFolder,"*.png"))html.Append("<img alt='").Append(Path.GetFileName(file)).Append("' src='").Append(Path.GetFileName(file)).Append("'>");
            File.WriteAllText(Path.Combine(acceptanceFolder,"report.html"),html.ToString());
        }
        IEnumerator AcceptanceScenarios()
        {
            playback.Pause();playback.Seek(9.25);ResetCamera();project.mirror=false;
            int[] views={0,1,3,4,5,6,2};string[] names={"front","back","left","right","upper","feet","free"};
            foreach(string sex in new[]{"female","male"})
            {
                SwitchCharacter(sex);yield return null;
                var skins=characterObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                var body=skins.OrderByDescending(r=>r.sharedMesh.vertexCount).First();
                Verify(sex+" complete skinned character",skins.Length>=3&&body.sharedMesh.vertexCount>5000,"vertices="+body.sharedMesh.vertexCount+", skins="+skins.Length);
                Verify(sex+" material and texture",skins.All(r=>r.sharedMaterial&&r.sharedMaterial.shader.isSupported&&r.sharedMaterial.mainTexture));
                // Mesh density is very different on the face/fingers and torso, so
                // count the actual weighted body regions rather than a global vertex ratio.
                var clothedBones=new HashSet<Transform>(new[]{HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg}.Select(b=>diagnosticAnimator.GetBoneTransform(b)).Where(b=>b));
                var skinRoots=new[]{HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot}.Select(b=>diagnosticAnimator.GetBoneTransform(b)).ToArray();
                var weights=body.sharedMesh.boneWeights;var colors=body.sharedMesh.colors;int clothed=0,uncovered=0,skin=0,paintedSkin=0;
                for(int v=0;v<weights.Length;v++)
                {
                    var w=weights[v];int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] values={w.weight0,w.weight1,w.weight2,w.weight3};float coreWeight=0,skinWeight=0;
                    for(int b=0;b<4;b++)
                    {
                        var bone=body.bones[ids[b]];if(clothedBones.Contains(bone))coreWeight+=values[b];
                        if(skinRoots.Any(root=>bone==root||bone.IsChildOf(root)))skinWeight+=values[b];
                    }
                    if(coreWeight>.8f){clothed++;if(colors[v].a<.79f)uncovered++;}
                    if(skinWeight>.8f){skin++;if(colors[v].a>.21f)paintedSkin++;}
                }
                Verify(sex+" training outfit covers torso and legs",clothed>500&&uncovered==0,"body vertices="+clothed+", uncovered="+uncovered);
                Verify(sex+" face fingers and feet retain skin material",skin>500&&paintedSkin==0,"skin vertices="+skin+", painted="+paintedSkin);
                var head=diagnosticAnimator.GetBoneTransform(HumanBodyBones.Head);
                var eyes=skins.First(r=>r.name.Equals("Eyes",StringComparison.OrdinalIgnoreCase));
                var hair=characterObject.GetComponentsInChildren<Renderer>().First(r=>r.name.IndexOf("Hair",StringComparison.OrdinalIgnoreCase)>=0);
                Verify(sex+" hair follows head",Vector3.Distance(hair.bounds.center,head.position)<.45f,"distance="+Vector3.Distance(hair.bounds.center,head.position));
                var posedBody=new Mesh();body.BakeMesh(posedBody);
                float skullTop=posedBody.vertices.Max(v=>body.transform.TransformPoint(v).y);
                float hairAboveSkull=hair.bounds.max.y-skullTop;Destroy(posedBody);
                Verify(sex+" hair fits skull height",hairAboveSkull>.005f&&hairAboveSkull<.10f,"above skull="+hairAboveSkull);
                playback.Seek(10);yield return null;
                var upperArm=diagnosticAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);Quaternion pose=upperArm.rotation;
                playback.Seek(11.2);yield return null;
                Verify(sex+" animation changes actual bones",Quaternion.Angle(pose,upperArm.rotation)>.1f);
                playback.Seek(9.25);yield return null;
                Vector3 foot=diagnosticAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                float lowest=Math.Min(foot.y,diagnosticAnimator.GetBoneTransform(HumanBodyBones.RightFoot).position.y);
                Verify(sex+" feet at floor",lowest>=-.03f&&lowest<.22f,"ankle height="+lowest);
                for(int index=0;index<views.Length;index++)
                {
                    ResetCamera();ChangeView(views[index]);yield return null;
                    var points=views[index]==5?new[]{HumanBodyBones.Head,HumanBodyBones.Neck,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm}:
                        views[index]==6?new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg}:
                        new[]{HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot};
                    bool framed=points.All(b=>cameraRig.ContainsPoint(diagnosticAnimator.GetBoneTransform(b).position,.01f));
                    Verify(sex+" "+names[index]+" framing",framed);
                    Verify(sex+" "+names[index]+" preserves paused pose",Math.Abs(playback.Position-9.25)<.0001&&Vector3.Distance(foot,diagnosticAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position)<.0001);
                    if(views[index]==0)
                    {
                        Vector3 face=Vector3.ProjectOnPlane(eyes.bounds.center-head.position,Vector3.up).normalized;
                        Vector3 toCamera=Vector3.ProjectOnPlane(stageCamera.transform.position-head.position,Vector3.up).normalized;
                        Verify(sex+" front actually faces viewer",Vector3.Dot(face,toCamera)>.5f,"dot="+Vector3.Dot(face,toCamera));
                    }
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(acceptanceFolder,sex+"_"+names[index]+".png"));yield return null;
                }
            }
            ResetCamera();ChangeView(2);cameraRig.Orbit(new Vector2(140,-30),false);cameraRig.Pan(new Vector2(35,-20),false);cameraRig.Zoom(-1);cameraRig.SetFieldOfView(48);
            Vector3 cameraPosition=stageCamera.transform.position;float expectedYaw=project.camera.yaw;
            project.character=selectedCharacter;Save();OpenProject(Path.Combine(projectFolder,"project.danceproj"));while(loading)yield return null;
            Verify("camera and character survive real project reload",selectedCharacter=="male"&&project.view==2&&Math.Abs(project.camera.yaw-expectedYaw)<.001&&Vector3.Distance(cameraPosition,stageCamera.transform.position)<.005);
            var center=diagnosticAnimator.GetBoneTransform(HumanBodyBones.Hips).position;
            ResetCamera();Vector3 before=stageCamera.transform.position;project.mirror=true;yield return null;
            Verify("mirror does not modify bones or camera",Vector3.Distance(before,stageCamera.transform.position)<.0001&&Vector3.Distance(center,diagnosticAnimator.GetBoneTransform(HumanBodyBones.Hips).position)<.0001);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(acceptanceFolder,"male_mirror.png"));yield return null;project.mirror=false;
            foreach(float fov in new[]{30f,60f})
            {
                cameraRig.SetFieldOfView(fov);yield return null;
                Verify("full body at FOV "+fov,cameraRig.ContainsPoint(diagnosticAnimator.GetBoneTransform(HumanBodyBones.Head).position)&&cameraRig.ContainsPoint(diagnosticAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position));
            }
            ChangeView(2);cameraRig.Orbit(new Vector2(9000,9000),false);cameraRig.Zoom(200);yield return null;
            Verify("orbit and zoom limits stay above floor",stageCamera.transform.position.y>=.12f&&project.camera.pitch<=65&&project.camera.distanceScale<=2.4f);
            cameraRig.Orbit(new Vector2(-9000,-9000),false);cameraRig.Zoom(-400);yield return null;
            Verify("near camera stays above floor",stageCamera.transform.position.y>=.12f&&project.camera.pitch>=-15&&project.camera.distanceScale>=.55f);
            ResetCamera();yield return null;
            Verify("reset restores a usable full-body view",CameraView==0&&project.camera.panX==0&&project.camera.panY==0&&cameraRig.ContainsPoint(diagnosticAnimator.GetBoneTransform(HumanBodyBones.Head).position)&&cameraRig.ContainsPoint(diagnosticAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position));
            playback.Seek(5);playback.Play();yield return new WaitForSecondsRealtime(.2f);double at=playback.Position;
            double dspAnchor=playback.DspAnchor,songAnchor=playback.SongAnchor;
            float switchStarted=Time.realtimeSinceStartup;SwitchCharacter("female");ChangeView(4);float switchMs=(Time.realtimeSinceStartup-switchStarted)*1000;
            yield return new WaitForSecondsRealtime(.2f);
            double expected=songAnchor+Math.Max(0,AudioSettings.dspTime-dspAnchor)*project.speed;
            Verify("live character and camera switch preserve music",playback.IsPlaying&&playback.DspAnchor==dspAnchor&&playback.SongAnchor==songAnchor&&playback.Position>at&&Math.Abs(playback.Position-expected)<.05,"playing="+playback.IsPlaying+", before="+at+", after="+playback.Position+", DSP expected="+expected+", switch ms="+switchMs);
            playback.Pause();ResetCamera();playback.Seek(9.25);yield return null;
            project.segments[0].locked=true;string locked=project.segments[0].motionId;Generate(project.mode,true);
            Verify("regeneration preserves locked slot",project.segments[0].locked&&project.segments[0].motionId==locked);
            project.segments[0].locked=false;double end=project.segments[0].endSeconds;
            var replacement=Choreographer.Replacements(project,catalog,0).Last();Choreographer.Replace(project,catalog,0,replacement.id);
            Verify("replacement preserves segment length",project.segments[0].endSeconds==end);
            Verify("all camera screenshots written",Directory.GetFiles(acceptanceFolder,"*.png").Length>=15);
        }
    }
}
