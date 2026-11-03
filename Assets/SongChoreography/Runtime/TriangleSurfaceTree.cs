using System;
using System.Collections.Generic;
using UnityEngine;

namespace SongChoreography
{
    // Static triangle topology with refitted bounds for deformed skin.
    internal sealed class TriangleSurfaceTree
    {
        internal sealed class Node {public Vector3 min,max;public int start,count,left=-1,right;}
        internal readonly Vector3[] points;internal readonly int[] triangles,order;internal readonly List<Node> nodes=new List<Node>();
        public TriangleSurfaceTree(Vector3[] points,int[] triangles)
        {this.points=points;this.triangles=triangles;order=new int[triangles.Length/3];for(int i=0;i<order.Length;i++)order[i]=i;if(order.Length>0){Build(0,order.Length);Refit();}}
        Vector3 Center(int triangle)=>(points[triangles[triangle*3]]+points[triangles[triangle*3+1]]+points[triangles[triangle*3+2]])/3;
        int Build(int start,int count)
        {
            int at=nodes.Count;var n=new Node{start=start,count=count};nodes.Add(n);if(count<=12)return at;
            var min=Center(order[start]);var max=min;for(int i=start+1;i<start+count;i++){var p=Center(order[i]);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}var size=max-min;int axis=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
            Array.Sort(order,start,count,Comparer<int>.Create((a,b)=>Center(a)[axis].CompareTo(Center(b)[axis])));int half=count/2;n.left=Build(start,half);n.right=Build(start+half,count-half);return at;
        }
        public void Refit()
        {
            for(int i=nodes.Count-1;i>=0;i--){var n=nodes[i];if(n.left>=0){n.min=Vector3.Min(nodes[n.left].min,nodes[n.right].min);n.max=Vector3.Max(nodes[n.left].max,nodes[n.right].max);continue;}var min=points[triangles[order[n.start]*3]];var max=min;for(int j=n.start;j<n.start+n.count;j++)for(int k=0;k<3;k++){var p=points[triangles[order[j]*3+k]];min=Vector3.Min(min,p);max=Vector3.Max(max,p);}n.min=min;n.max=max;}
        }
    }
}
