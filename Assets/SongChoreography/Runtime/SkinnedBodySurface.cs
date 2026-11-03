using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SongChoreography
{
    // A refittable triangle hierarchy over the character's actual body skin.
    // Hands/arms are excluded by skin weights so a hand cannot hit itself.
    public sealed class SkinnedBodySurface : IDisposable
    {
        public struct Contact {public Vector3 point,normal;public float distance;public bool inside,found;}
        sealed class Skin
        {
            public SkinnedMeshRenderer renderer;public int offset;public int[] used;
            public Vector3[] sourceVertices,sourceNormals;public BoneWeight[] weights;public Matrix4x4[] bind,matrices;public bool bake;
        }
        sealed class Node {public Vector3 min,max;public int left=-1,right,start,count;}
        readonly List<Skin> skins=new List<Skin>();readonly List<Node> nodes=new List<Node>();
        readonly List<Vector3> localVertices=new List<Vector3>(),localNormals=new List<Vector3>();
        readonly List<float> hits=new List<float>();readonly Mesh baked=new Mesh();
        readonly Vector3[] vertices,normals;readonly int[] triangles,order;
        public int TriangleCount=>order.Length;
        public bool Valid=>nodes.Count>0;
        static readonly HumanBodyBones[] BodyBones={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.Neck,HumanBodyBones.Head,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes};
        public SkinnedBodySurface(GameObject actor,Animator animator)
        {
            var body=new HashSet<Transform>(BodyBones.Select(animator.GetBoneTransform).Where(t=>t));var selected=new List<int>();int count=0;
            foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var source=renderer.sharedMesh;if(!source)continue;
                var weights=source.boneWeights;var indices=new HashSet<int>(Enumerable.Range(0,renderer.bones.Length).Where(i=>body.Contains(renderer.bones[i])));var original=source.triangles;var used=new HashSet<int>();
                for(int i=0;i<original.Length;i+=3)
                {
                    int a=original[i],b=original[i+1],c=original[i+2];
                    if(weights.Length!=source.vertexCount||Weight(weights[a],indices)<.65f||Weight(weights[b],indices)<.65f||Weight(weights[c],indices)<.65f)continue;
                    selected.Add(a+count);selected.Add(b+count);selected.Add(c+count);used.Add(a);used.Add(b);used.Add(c);
                }
                var originalNormals=source.normals;
                skins.Add(new Skin{renderer=renderer,offset=count,used=used.ToArray(),sourceVertices=source.vertices,sourceNormals=originalNormals,weights=weights,bind=source.bindposes,matrices=new Matrix4x4[renderer.bones.Length],bake=source.blendShapeCount>0||originalNormals.Length!=source.vertexCount});count+=source.vertexCount;
            }
            vertices=new Vector3[count];normals=new Vector3[count];triangles=selected.ToArray();order=Enumerable.Range(0,triangles.Length/3).ToArray();
            Update();if(order.Length>0){Build(0,order.Length);Refit();}baked.MarkDynamic();
        }
        static float Weight(BoneWeight w,HashSet<int> b)=>(b.Contains(w.boneIndex0)?w.weight0:0)+(b.Contains(w.boneIndex1)?w.weight1:0)+(b.Contains(w.boneIndex2)?w.weight2:0)+(b.Contains(w.boneIndex3)?w.weight3:0);
        Vector3 Center(int triangle)=>(vertices[triangles[triangle*3]]+vertices[triangles[triangle*3+1]]+vertices[triangles[triangle*3+2]])/3;
        int Build(int start,int count)
        {
            int at=nodes.Count;var node=new Node{start=start,count=count};nodes.Add(node);
            if(count<=12)return at;
            var bounds=new Bounds(Center(order[start]),Vector3.zero);for(int i=start+1;i<start+count;i++)bounds.Encapsulate(Center(order[i]));var size=bounds.size;int axis=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
            Array.Sort(order,start,count,Comparer<int>.Create((a,b)=>Center(a)[axis].CompareTo(Center(b)[axis])));int half=count/2;
            node.left=Build(start,half);node.right=Build(start+half,count-half);return at;
        }
        public void Update()
        {
            foreach(var skin in skins)
            {
                if(skin.bake)
                {
                    skin.renderer.BakeMesh(baked,true);baked.GetVertices(localVertices);baked.GetNormals(localNormals);var matrix=skin.renderer.transform.localToWorldMatrix;
                    foreach(int index in skin.used){vertices[skin.offset+index]=matrix.MultiplyPoint3x4(localVertices[index]);normals[skin.offset+index]=matrix.MultiplyVector(localNormals[index]).normalized;}
                    continue;
                }
                // Exact four-weight CPU skinning of body vertices only. The
                // Tripo mesh has no blend shapes; skip baking unrelated arms.
                for(int i=0;i<skin.matrices.Length;i++)skin.matrices[i]=skin.renderer.bones[i].localToWorldMatrix*skin.bind[i];
                foreach(int index in skin.used)
                {
                    var w=skin.weights[index];var v=skin.sourceVertices[index];var n=skin.sourceNormals[index];var a=skin.matrices[w.boneIndex0];var b=skin.matrices[w.boneIndex1];var c=skin.matrices[w.boneIndex2];var d=skin.matrices[w.boneIndex3];
                    vertices[skin.offset+index]=a.MultiplyPoint3x4(v)*w.weight0+b.MultiplyPoint3x4(v)*w.weight1+c.MultiplyPoint3x4(v)*w.weight2+d.MultiplyPoint3x4(v)*w.weight3;
                    normals[skin.offset+index]=(a.MultiplyVector(n)*w.weight0+b.MultiplyVector(n)*w.weight1+c.MultiplyVector(n)*w.weight2+d.MultiplyVector(n)*w.weight3).normalized;
                }
            }
            Refit();
        }
        void Refit()
        {
            for(int i=nodes.Count-1;i>=0;i--)
            {
                var n=nodes[i];if(n.left>=0){n.min=Vector3.Min(nodes[n.left].min,nodes[n.right].min);n.max=Vector3.Max(nodes[n.left].max,nodes[n.right].max);continue;}
                int first=triangles[order[n.start]*3];var min=vertices[first];var max=min;
                for(int j=n.start;j<n.start+n.count;j++)for(int k=0;k<3;k++){var v=vertices[triangles[order[j]*3+k]];min=Vector3.Min(min,v);max=Vector3.Max(max,v);}n.min=min;n.max=max;
            }
        }
        public static Vector3 ClosestOnTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 ab=b-a,ac=c-a,ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);if(d1<=0&&d2<=0)return a;
            Vector3 bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
            float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
            Vector3 cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
            float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/(d4-d3+d5-d6));
            float denom=1/(va+vb+vc);return a+ab*(vb*denom)+ac*(vc*denom);
        }
        void Nearest(int index,Vector3 p,ref float best,ref Vector3 surface,ref Vector3 normal)
        {
            var n=nodes[index];if(SquaredDistance(n,p)>best)return;
            if(n.left>=0)
            {
                int a=n.left,b=n.right;if(SquaredDistance(nodes[a],p)>SquaredDistance(nodes[b],p)){int swap=a;a=b;b=swap;}
                Nearest(a,p,ref best,ref surface,ref normal);Nearest(b,p,ref best,ref surface,ref normal);return;
            }
            for(int i=n.start;i<n.start+n.count;i++)
            {
                int t=order[i]*3,a=triangles[t],b=triangles[t+1],c=triangles[t+2];var q=ClosestOnTriangle(p,vertices[a],vertices[b],vertices[c]);float squared=(q-p).sqrMagnitude;
                if(squared>=best)continue;best=squared;surface=q;normal=(normals[a]+normals[b]+normals[c]).normalized;
            }
        }
        static float SquaredDistance(Node n,Vector3 p)
        {float x=Mathf.Max(Mathf.Max(n.min.x-p.x,0),p.x-n.max.x),y=Mathf.Max(Mathf.Max(n.min.y-p.y,0),p.y-n.max.y),z=Mathf.Max(Mathf.Max(n.min.z-p.z,0),p.z-n.max.z);return x*x+y*y+z*z;}
        static bool Intersects(Node bounds,Vector3 p,Vector3 inverse)
        {
            var min=bounds.min;var max=bounds.max;float a=(min.x-p.x)*inverse.x,b=(max.x-p.x)*inverse.x;float lo=Mathf.Max(0,Mathf.Min(a,b)),hi=Mathf.Max(a,b);if(hi<lo)return false;
            a=(min.y-p.y)*inverse.y;b=(max.y-p.y)*inverse.y;lo=Mathf.Max(lo,Mathf.Min(a,b));hi=Mathf.Min(hi,Mathf.Max(a,b));if(hi<lo)return false;
            a=(min.z-p.z)*inverse.z;b=(max.z-p.z)*inverse.z;lo=Mathf.Max(lo,Mathf.Min(a,b));hi=Mathf.Min(hi,Mathf.Max(a,b));return hi>=lo;
        }
        static bool RayTriangle(Vector3 p,Vector3 direction,Vector3 a,Vector3 b,Vector3 c,out float distance)
        {
            distance=0;var ab=b-a;var ac=c-a;var h=Vector3.Cross(direction,ac);float det=Vector3.Dot(ab,h);if(Mathf.Abs(det)<1e-8f)return false;
            float inv=1/det;var s=p-a;float u=inv*Vector3.Dot(s,h);if(u<0||u>1)return false;var q=Vector3.Cross(s,ab);float v=inv*Vector3.Dot(direction,q);if(v<0||u+v>1)return false;
            distance=inv*Vector3.Dot(ac,q);return distance>1e-5f;
        }
        void RayHits(int index,Vector3 p,Vector3 direction,Vector3 inverse)
        {
            var n=nodes[index];if(!Intersects(n,p,inverse))return;
            if(n.left>=0){RayHits(n.left,p,direction,inverse);RayHits(n.right,p,direction,inverse);return;}
            for(int i=n.start;i<n.start+n.count;i++){int t=order[i]*3;if(RayTriangle(p,direction,vertices[triangles[t]],vertices[triangles[t+1]],vertices[triangles[t+2]],out float d))hits.Add(d);}
        }
        int Intersections(Vector3 p,Vector3 direction)
        {
            hits.Clear();RayHits(0,p,direction,new Vector3(Inverse(direction.x),Inverse(direction.y),Inverse(direction.z)));hits.Sort();int count=0;float last=-1;
            foreach(float hit in hits)if(hit-last>.0001f){count++;last=hit;}return count;
        }
        static float Inverse(float value)=>Mathf.Abs(value)<1e-9f?1e12f:1/value;
        public bool IntersectsTriangle(Vector3 a,Vector3 b,Vector3 c)
        {if(!Valid)return false;var min=Vector3.Min(a,Vector3.Min(b,c));var max=Vector3.Max(a,Vector3.Max(b,c));return TriangleHits(0,a,b,c,min,max);}
        bool TriangleHits(int at,Vector3 a,Vector3 b,Vector3 c,Vector3 min,Vector3 max)
        {
            var n=nodes[at];const float epsilon=.000001f;if(min.x>n.max.x+epsilon||min.y>n.max.y+epsilon||min.z>n.max.z+epsilon||max.x<n.min.x-epsilon||max.y<n.min.y-epsilon||max.z<n.min.z-epsilon)return false;
            if(n.left>=0)return TriangleHits(n.left,a,b,c,min,max)||TriangleHits(n.right,a,b,c,min,max);
            for(int i=n.start;i<n.start+n.count;i++){int t=order[i]*3;if(TrianglesIntersect(a,b,c,vertices[triangles[t]],vertices[triangles[t+1]],vertices[triangles[t+2]]))return true;}return false;
        }
        internal bool Intersects(TriangleSurfaceTree other,float margin=0)=>Valid&&other.nodes.Count>0&&SurfacePairs(0,other,0,margin);
        bool SurfacePairs(int at,TriangleSurfaceTree other,int next,float margin)
        {
            var a=nodes[at];var b=other.nodes[next];float e=margin+.000001f;if(a.min.x>b.max.x+e||a.min.y>b.max.y+e||a.min.z>b.max.z+e||a.max.x<b.min.x-e||a.max.y<b.min.y-e||a.max.z<b.min.z-e)return false;
            if(a.left<0&&b.left<0){for(int i=a.start;i<a.start+a.count;i++){int t=order[i]*3;var p=vertices[triangles[t]];var q=vertices[triangles[t+1]];var r=vertices[triangles[t+2]];for(int j=b.start;j<b.start+b.count;j++){int s=other.order[j]*3;if(TrianglesIntersect(p,q,r,other.points[other.triangles[s]],other.points[other.triangles[s+1]],other.points[other.triangles[s+2]],margin))return true;}}return false;}
            if(b.left<0||a.left>=0&&a.count>=b.count)return SurfacePairs(a.left,other,next,margin)||SurfacePairs(a.right,other,next,margin);
            return SurfacePairs(at,other,b.left,margin)||SurfacePairs(at,other,b.right,margin);
        }
        // SAT, including the in-plane directions for coplanar triangles.
        // https://www.geometrictools.com/Documentation/MethodOfSeparatingAxes.pdf
        public static bool TrianglesIntersect(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 e,Vector3 f,float margin=0)
        {
            // Leaf nodes contain several triangles: reject disjoint triangle
            // bounds before evaluating the more expensive separating axes.
            var lo=Vector3.Min(a,Vector3.Min(b,c));var hi=Vector3.Max(a,Vector3.Max(b,c));var otherLo=Vector3.Min(d,Vector3.Min(e,f));var otherHi=Vector3.Max(d,Vector3.Max(e,f));float tolerance=margin+.000001f;
            if(lo.x>otherHi.x+tolerance||lo.y>otherHi.y+tolerance||lo.z>otherHi.z+tolerance||hi.x<otherLo.x-tolerance||hi.y<otherLo.y-tolerance||hi.z<otherLo.z-tolerance)return false;
            var ab=b-a;var bc=c-b;var ca=a-c;var de=e-d;var ef=f-e;var fd=d-f;var n=Vector3.Cross(ab,c-a);var m=Vector3.Cross(de,f-d);
            if(n.sqrMagnitude<1e-16f||m.sqrMagnitude<1e-16f)return false;
            if(Separated(n,a,b,c,d,e,f,margin)||Separated(m,a,b,c,d,e,f,margin))return false;
            if(Separated(Vector3.Cross(ab,de),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(ab,ef),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(ab,fd),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(bc,de),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(bc,ef),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(bc,fd),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(ca,de),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(ca,ef),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(ca,fd),a,b,c,d,e,f,margin))return false;
            return !(Separated(Vector3.Cross(n,ab),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(n,bc),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(n,ca),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(m,de),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(m,ef),a,b,c,d,e,f,margin)||Separated(Vector3.Cross(m,fd),a,b,c,d,e,f,margin));
        }
        static bool Separated(Vector3 axis,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 e,Vector3 f,float margin)
        {
            float length=axis.sqrMagnitude;if(length<1e-18f)return false;float a0=Vector3.Dot(axis,a),a1=Vector3.Dot(axis,b),a2=Vector3.Dot(axis,c),b0=Vector3.Dot(axis,d),b1=Vector3.Dot(axis,e),b2=Vector3.Dot(axis,f),epsilon=(margin+.000001f)*Mathf.Sqrt(length);
            return Mathf.Max(a0,Mathf.Max(a1,a2))<Mathf.Min(b0,Mathf.Min(b1,b2))-epsilon||Mathf.Max(b0,Mathf.Max(b1,b2))<Mathf.Min(a0,Mathf.Min(a1,a2))-epsilon;
        }
        public Contact Query(Vector3 p,float limit=.5f)
        {
            if(!Valid)return new Contact{distance=float.PositiveInfinity};float best=limit*limit;Vector3 q=Vector3.zero,n=Vector3.zero;Nearest(0,p,ref best,ref q,ref n);
            if(n.sqrMagnitude<.1f)return new Contact{distance=float.PositiveInfinity};float distance=Mathf.Sqrt(best);bool inside=false;
            if(Vector3.Dot(p-q,n)<-.0001f)
            {
                int votes=0;foreach(var d in Directions)if(Intersections(p,d)%2==1)votes++;inside=votes>=2;
            }
            return new Contact{found=true,point=q,normal=inside?n:distance>.00001f?(p-q)/distance:n,distance=inside?-distance:distance,inside=inside};
        }
        static readonly Vector3[] Directions={new Vector3(1,.013f,.073f).normalized,new Vector3(-.037f,-.019f,-1).normalized,new Vector3(.037f,.019f,1).normalized};
        public Contact Interior(Vector3 p)
        {
            if(!Valid||SquaredDistance(nodes[0],p)>0)return new Contact{distance=float.PositiveInfinity};
            int votes=(Intersections(p,Directions[1])%2)+(Intersections(p,Directions[2])%2);if(votes==0)return new Contact{distance=float.PositiveInfinity};
            if(votes==1&&Intersections(p,Directions[0])%2==0)return new Contact{distance=float.PositiveInfinity};
            float best=float.PositiveInfinity;Vector3 q=Vector3.zero,n=Vector3.zero;Nearest(0,p,ref best,ref q,ref n);
            if(Vector3.Dot(p-q,n)>=-.0001f)return new Contact{distance=float.PositiveInfinity};
            return new Contact{found=true,inside=true,point=q,normal=n,distance=-Mathf.Sqrt(best)};
        }
        public Vector3 Escape(Vector3 p,float radius,Vector3 preferredSide)
        {
            var c=Query(p);if(!c.found||c.distance>=radius)return Vector3.zero;
            return EscapeContact(p,radius,preferredSide,c);
        }
        public Vector3 EscapeNear(Vector3 p,float radius,Vector3 preferredSide,bool allowDeep=true)
        {
            // The nearest surface rejects an exterior point without ray
            // casts. This is faster than testing parity for every clear point.
            var c=Query(p,radius+.003f);if(!c.found&&allowDeep)c=Interior(p);if(!c.found||c.distance>=radius)return Vector3.zero;
            return EscapeContact(p,radius,preferredSide,c);
        }
        Vector3 EscapeContact(Vector3 p,float radius,Vector3 preferredSide,Contact c)
        {
            var push=c.normal*(radius-c.distance+.001f);
            if(c.inside&&c.distance<-.025f&&preferredSide.sqrMagnitude>.1f)
            {
                var direction=(preferredSide+Vector3.up*.011f+Vector3.forward*.007f).normalized;Intersections(p,direction);
                if(hits.Count>0)
                {
                    var side=direction*(hits[0]+radius+.003f);float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.025f,.065f,-c.distance));push=Vector3.Lerp(push,side,blend);
                }
            }
            return Vector3.ClampMagnitude(push,.22f);
        }
        public void Dispose(){if(baked){if(Application.isPlaying)UnityEngine.Object.Destroy(baked);else UnityEngine.Object.DestroyImmediate(baked);}}
    }
}
