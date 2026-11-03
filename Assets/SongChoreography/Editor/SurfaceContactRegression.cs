using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class SurfaceContactRegression
    {
        [MenuItem("Tools/CHOREO AI/Review/Verify triangle contact geometry")]
        public static void Run()
        {
            var a=Vector3.zero;var b=Vector3.right;var c=Vector3.up;int checks=0;
            Action<Vector3,Vector3,Vector3,bool> test=(d,e,f,expected)=>{foreach(var shift in new[]{Vector3.zero,new Vector3(7,-3,11)}){bool direct=SkinnedBodySurface.TrianglesIntersect(a+shift,b+shift,c+shift,d+shift,e+shift,f+shift),reverse=SkinnedBodySurface.TrianglesIntersect(d+shift,e+shift,f+shift,a+shift,b+shift,c+shift);if(direct!=expected||reverse!=expected)throw new InvalidOperationException("Triangle contact regression failed.");checks+=2;}};
            test(new Vector3(.2f,.2f,-1),new Vector3(.2f,.2f,1),new Vector3(.8f,.2f,0),true);
            test(Vector3.forward,Vector3.forward+Vector3.right,Vector3.forward+Vector3.up,false);
            test(new Vector3(.1f,.1f,0),new Vector3(.4f,.1f,0),new Vector3(.1f,.4f,0),true);
            test(new Vector3(2,2,0),new Vector3(3,2,0),new Vector3(2,3,0),false);
            test(Vector3.right,Vector3.right*2,Vector3.right+Vector3.up,true);
            test(Vector3.zero,Vector3.zero,Vector3.zero,false);
            Directory.CreateDirectory(".generation");File.WriteAllText(".generation/triangle-contact-regression.json","{\"passed\":true,\"checks\":"+checks+"}");Debug.Log("TRIANGLE CONTACT REGRESSION: "+checks+" passed.");
        }
    }
}
