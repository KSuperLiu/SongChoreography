using System.Collections.Generic;
using UnityEngine;

namespace SongChoreography
{
    // An original, deliberately simple diagnostic mannequin. Not a New Jazz asset.
    public static class DiagnosticAvatar
    {
        public static GameObject Create()
        {
            var root=new GameObject("Diagnostic Mannequin");
            var bones=new Dictionary<string,Transform>();
            Transform hips=Bone(root.transform,"Hips",new Vector3(0,1.02f,0),bones);
            Transform spine=Bone(hips,"Spine",new Vector3(0,.19f,0),bones);
            Transform chest=Bone(spine,"Chest",new Vector3(0,.2f,0),bones);
            Transform neck=Bone(chest,"Neck",new Vector3(0,.19f,0),bones);
            Transform head=Bone(neck,"Head",new Vector3(0,.13f,0),bones);
            for(int s=-1;s<=1;s+=2)
            {
                string side=s>0?"Left":"Right";
                Transform upper=Bone(hips,side+"UpperLeg",new Vector3(s*.115f,-.08f,0),bones);
                Transform lower=Bone(upper,side+"LowerLeg",new Vector3(0,-.43f,0),bones);
                Transform foot=Bone(lower,side+"Foot",new Vector3(0,-.43f,0),bones);
                Bone(foot,side+"Toes",new Vector3(0,-.035f,.15f),bones);
                Transform shoulder=Bone(chest,side+"Shoulder",new Vector3(s*.1f,.105f,0),bones);
                Transform arm=Bone(shoulder,side+"UpperArm",new Vector3(s*.13f,0,0),bones);
                Transform forearm=Bone(arm,side+"LowerArm",new Vector3(s*.28f,0,0),bones);
                Bone(forearm,side+"Hand",new Vector3(s*.25f,0,0),bones);
            }
            var human=new List<HumanBone>();
            foreach(var b in bones) human.Add(new HumanBone{boneName=b.Key,humanName=b.Key,limit=new HumanLimit{useDefaultValues=true}});
            var skeleton=new List<SkeletonBone>();
            foreach(var t in root.GetComponentsInChildren<Transform>()) skeleton.Add(new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale});
            var desc=new HumanDescription{human=human.ToArray(),skeleton=skeleton.ToArray(),upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f,feetSpacing=0,hasTranslationDoF=false};
            var animator=root.AddComponent<Animator>();
            animator.avatar=AvatarBuilder.BuildHumanAvatar(root,desc);
            animator.avatar.name="Original diagnostic humanoid";
            animator.applyRootMotion=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var body=Material(new Color(.14f,.79f,.76f));
            var dark=Material(new Color(.075f,.15f,.22f));
            var light=Material(new Color(.87f,.92f,.94f));
            Link(hips,spine,.18f,dark);Link(spine,chest,.185f,body);Link(chest,neck,.2f,body);
            Ball(head,new Vector3(0,.065f,0),new Vector3(.23f,.29f,.23f),light);
            // Visor makes the front legible in orbit/mirror view.
            Ball(head,new Vector3(0,.09f,.105f),new Vector3(.17f,.065f,.045f),dark);
            foreach(string side in new[]{"Left","Right"})
            {
                Link(bones[side+"UpperLeg"],bones[side+"LowerLeg"],.105f,dark);
                Link(bones[side+"LowerLeg"],bones[side+"Foot"],.085f,body);
                Ball(bones[side+"Foot"],new Vector3(0,-.015f,.055f),new Vector3(.14f,.09f,.25f),dark);
                Link(bones[side+"UpperArm"],bones[side+"LowerArm"],.075f,body);
                Link(bones[side+"LowerArm"],bones[side+"Hand"],.065f,light);
                Ball(bones[side+"Hand"],Vector3.zero,new Vector3(.08f,.1f,.09f),light);
                Ball(bones[side+"LowerLeg"],Vector3.zero,Vector3.one*.105f,light);
            }
            return root;
        }
        static Transform Bone(Transform parent,string name,Vector3 position,Dictionary<string,Transform> bones)
        { var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;bones.Add(name,t);return t; }
        static Material Material(Color color)
        { var m=new Material(Shader.Find("Standard"));m.color=color;m.SetFloat("_Glossiness",.38f);return m; }
        static void Ball(Transform parent,Vector3 position,Vector3 scale,Material material)
        { var g=GameObject.CreatePrimitive(PrimitiveType.Sphere);g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;Object.Destroy(g.GetComponent<Collider>()); }
        static void Link(Transform a,Transform b,float radius,Material material)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Capsule);g.transform.SetParent(a,false);
            Vector3 delta=a.InverseTransformPoint(b.position);g.transform.localPosition=delta*.5f;
            g.transform.localRotation=Quaternion.FromToRotation(Vector3.up,delta.normalized);
            g.transform.localScale=new Vector3(radius*2,delta.magnitude*.5f,radius*2);
            g.GetComponent<Renderer>().sharedMaterial=material;Object.Destroy(g.GetComponent<Collider>());
        }
    }
}
