using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    // An approval is bound to the exact baked curve data and support profile.
    // Re-baking a changed motion invalidates the approval instead of silently
    // inheriting a review performed on an older clip.
    public static class MocapReviewGate
    {
        public static void Export()=>Apply(JsonUtility.FromJson<MotionCatalog>(File.ReadAllText("Assets/Resources/MotionCatalog.json")));
        [Serializable]public class Entry
        {
            public string id,sha256,name,reviewer,reviewNotes;
            public float energy=.5f;
            public List<string> safeNext=new List<string>();
        }
        [Serializable]public class Review {public int schemaVersion=1;public List<Entry> motions=new List<Entry>();}
        public static string Digest(MotionDefinition motion)
        {
            var clip=Resources.Load<AnimationClip>(motion.resource);var text=new StringBuilder();
            text.Append(motion.id).Append('|').Append(motion.beats).Append('|').Append(motion.source).Append('|').Append(motion.license);
            Action<float> number=value=>text.Append('|').Append(value.ToString("R",CultureInfo.InvariantCulture));
            number(motion.transitionBeats);number(clip.length);
            foreach(var value in motion.supportHeights)number(value);
            foreach(var binding in AnimationUtility.GetCurveBindings(clip).OrderBy(b=>b.propertyName,StringComparer.Ordinal))
            {
                text.Append('|').Append(binding.path).Append('|').Append(binding.propertyName);
                foreach(var key in AnimationUtility.GetEditorCurve(clip,binding).keys)
                {number(key.time);number(key.value);number(key.inTangent);number(key.outTangent);}
            }
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","").ToLowerInvariant();
        }
        public static void Apply(MotionCatalog catalog)
        {
            var candidates=catalog.motions.Where(m=>m.style=="charleston-review").ToArray();
            var measured=new Review{motions=candidates.Select(m=>new Entry{id=m.id,sha256=Digest(m)}).ToList()};
            Directory.CreateDirectory("Builds/Windows/MotionPhase");
            File.WriteAllText("Builds/Windows/MotionPhase/clip-hashes.json",JsonUtility.ToJson(measured,true));
            const string reviewPath="Assets/SongChoreography/Editor/MocapData/reviewed-charleston.json";
            if(!File.Exists(reviewPath))return;
            var review=JsonUtility.FromJson<Review>(File.ReadAllText(reviewPath));
            if(review.schemaVersion!=1||review.motions.Count!=8||review.motions.Select(m=>m.id).Distinct().Count()!=8)throw new InvalidDataException("Charleston review manifest must contain eight unique reviewed clips.");
            foreach(var entry in review.motions)
            {
                var motion=candidates.SingleOrDefault(m=>m.id==entry.id);
                if(motion==null||Digest(motion)!=entry.sha256)throw new InvalidDataException("Motion changed after visual review: "+entry.id);
                if(string.IsNullOrWhiteSpace(entry.reviewer)||string.IsNullOrWhiteSpace(entry.reviewNotes)||string.IsNullOrWhiteSpace(entry.name)||entry.safeNext.Count==0||entry.safeNext.Any(id=>!review.motions.Any(m=>m.id==id)))throw new InvalidDataException("Incomplete review: "+entry.id);
                motion.style=DanceStyles.Charleston;motion.approved=true;motion.name=entry.name;motion.energy=entry.energy;
                motion.reviewer=entry.reviewer;motion.reviewNotes=entry.reviewNotes;motion.safeNext=entry.safeNext;
            }
            Directory.CreateDirectory("Assets/StreamingAssets/MotionCredits");
            File.Copy(reviewPath,"Assets/StreamingAssets/MotionCredits/reviewed-charleston.json",true);
            Debug.Log("REVIEWED CHARLESTON LIBRARY VERIFIED: "+review.motions.Count);
        }
    }
}
