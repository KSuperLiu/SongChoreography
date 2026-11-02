using System;
using UnityEngine;

namespace SongChoreography
{
    // Camera changes never touch the character, animation graph or music clock.
    public sealed class PracticeCamera : MonoBehaviour
    {
        public const int Front=0,Back=1,Free=2,Left=3,Right=4,Upper=5,Feet=6;
        Camera output;
        Transform target;
        Bounds referenceBounds;
        CameraSettings state;
        int preset;
        public int Preset=>preset;
        public Bounds ReferenceBounds=>referenceBounds;
        public Camera Output=>output;
        public void Initialize(Camera camera,Transform character)
        {output=camera;SetTarget(character);}
        public void SetTarget(Transform character)
        {
            target=character;
            var renderers=character.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)throw new InvalidOperationException("角色没有可显示的模型。");
            referenceBounds=renderers[0].bounds;
            foreach(var renderer in renderers)referenceBounds.Encapsulate(renderer.bounds);
            if(referenceBounds.size.y<.5f||referenceBounds.size.y>3)throw new InvalidOperationException("角色尺寸不正确。");
        }
        public void Bind(CameraSettings settings,int view)
        {state=settings??new CameraSettings();preset=view;Refresh();}
        public void Select(int view,bool clearFraming=true)
        {
            preset=view;
            if(clearFraming){state.distanceScale=1;state.panX=state.panY=0;}
            Refresh();
        }
        public void Orbit(Vector2 delta,bool mirror)
        {preset=Free;state.yaw=Mathf.Repeat(state.yaw+delta.x*(mirror?-1:1)*.38f+180,360)-180;state.pitch=Mathf.Clamp(state.pitch+delta.y*.3f,-15,65);Refresh();}
        public void Pan(Vector2 delta,bool mirror)
        {state.panX=Mathf.Clamp(state.panX-delta.x*(mirror?-1:1)*.0014f*state.distanceScale,-1,1);state.panY=Mathf.Clamp(state.panY+delta.y*.0014f*state.distanceScale,-1,1);Refresh();}
        public void Zoom(float delta)
        {state.distanceScale=Mathf.Clamp(state.distanceScale*Mathf.Exp(delta*.08f),.55f,2.4f);Refresh();}
        public void ResetView()
        {state.yaw=25;state.pitch=10;state.distanceScale=1;state.panX=state.panY=0;state.fieldOfView=40;state.dragPans=false;preset=Front;Refresh();}
        public void SetFieldOfView(float value){state.fieldOfView=Mathf.Clamp(value,30,60);Refresh();}
        public void SetDistance(float value){state.distanceScale=Mathf.Clamp(value,.55f,2.4f);Refresh();}
        public void SetOrbit(float yaw,float pitch){state.yaw=Mathf.Clamp(yaw,-180,180);state.pitch=Mathf.Clamp(pitch,-15,65);preset=Free;Refresh();}
        public void SetPan(float x,float y){state.panX=Mathf.Clamp(x,-1,1);state.panY=Mathf.Clamp(y,-1,1);Refresh();}
        void LateUpdate(){Refresh();}
        void Refresh()
        {
            if(!output||!target||state==null)return;
            float height=referenceBounds.size.y;
            Vector3 center=referenceBounds.center;
            float frameHeight=height*1.16f,frameWidth=Mathf.Max(referenceBounds.size.x,height*.95f)*1.10f;
            if(preset==Upper){center.y=referenceBounds.min.y+height*.76f;frameHeight=height*.62f;frameWidth=height*.80f;}
            if(preset==Feet){center.y=referenceBounds.min.y+height*.21f;frameHeight=height*.49f;frameWidth=height*.60f;}
            float yaw=preset==Back?180:preset==Left?90:preset==Right?-90:preset==Free?state.yaw:0;
            float pitch=preset==Free?state.pitch:preset==Feet?10:3;
            Quaternion rotation=Quaternion.Euler(-pitch,yaw,0);
            center+=rotation*new Vector3(state.panX*height,state.panY*height,0);
            output.fieldOfView=state.fieldOfView;
            float halfFov=Mathf.Tan(state.fieldOfView*Mathf.Deg2Rad*.5f);
            float fit=Mathf.Max(frameHeight/(2*halfFov),frameWidth/(2*halfFov*output.aspect));
            float depth=Mathf.Max(referenceBounds.size.z,.25f);
            float distance=(fit+depth*.5f)*state.distanceScale;
            Vector3 position=center+rotation*Vector3.forward*distance;
            position.y=Mathf.Max(.12f,position.y);
            output.transform.position=position;
            output.transform.LookAt(center);
        }
        public bool ContainsPoint(Vector3 point,float margin=0)
        {var v=output.WorldToViewportPoint(point);return v.z>output.nearClipPlane&&v.x>=margin&&v.x<=1-margin&&v.y>=margin&&v.y<=1-margin;}
    }
}
