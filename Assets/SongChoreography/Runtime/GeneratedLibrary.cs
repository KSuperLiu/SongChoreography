using System;
using UnityEngine;

namespace SongChoreography
{
    [Serializable] public class CharacterAsset
    {
        public string id="";
        public GameObject prefab;
    }
    [Serializable] public class MotionAsset
    {
        public string id="";
        public AnimationClip clip;
    }
    public sealed class GeneratedLibrary : ScriptableObject
    {
        public AssetCatalog catalog=new AssetCatalog();
        public CharacterAsset[] characters=new CharacterAsset[0];
        public MotionAsset[] motions=new MotionAsset[0];
        public string[] importIssues=new string[0];
        public CharacterAsset Character(string id)=>Array.Find(characters,a=>a.id==id);
        public MotionAsset Motion(string id)=>Array.Find(motions,a=>a.id==id);
    }
}
