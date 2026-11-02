using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Run in the disposable personal build snapshot. Never approves new data.
    public static class ApprovalAnatomyRegression
    {
        [Serializable]class Result {public bool legacyPersonalRejected,changedPersonalRejected,legacyPublicRejected,changedPublicRejected,restoredApprovalsPassed;}
        public static void Run()
        {
            if(!Path.GetFullPath(".").Contains(".build-cache"))throw new InvalidOperationException("Use an isolated build snapshot for approval mutation tests.");
            const string personal="PersonalAssets/FineDance/reviewed.json",shared="Assets/SongChoreography/Editor/MocapData/reviewed-charleston.json";
            if(File.Exists(personal))throw new InvalidOperationException("Existing personal override preserved.");
            string privateReview=File.ReadAllText("Assets/SongChoreography/Editor/PersonalMotionReview/finedance-001.json"),publicReview=File.ReadAllText(shared);
            var result=new Result();
            Func<MotionCatalog> publicCandidates=()=>
            {
                var catalog=JsonUtility.FromJson<MotionCatalog>(Resources.Load<TextAsset>("MotionCatalog").text);
                foreach(var motion in catalog.motions.Where(m=>m.id.StartsWith("charleston-review-"))){motion.style="charleston-review";motion.approved=false;}
                return catalog;
            };
            try
            {
                File.WriteAllText(personal,privateReview.Replace("\"schemaVersion\": 2","\"schemaVersion\": 1"));PersonalMotionBuilder.Prepare();
                result.legacyPersonalRejected=!JsonUtility.FromJson<MotionCatalog>(File.ReadAllText("Assets/Resources/PersonalMotionCatalog.json")).motions.Any(m=>m.approved);
                File.WriteAllText(personal,privateReview.Replace("\"sha256\": \"","\"sha256\": \"changed-"));
                try{PersonalMotionBuilder.Prepare();}catch(InvalidDataException){result.changedPersonalRejected=!JsonUtility.FromJson<MotionCatalog>(File.ReadAllText("Assets/Resources/PersonalMotionCatalog.json")).motions.Any(m=>m.approved);}
                File.WriteAllText(shared,publicReview.Replace("\"schemaVersion\": 2","\"schemaVersion\": 1"));var candidates=publicCandidates();MocapReviewGate.Apply(candidates);
                result.legacyPublicRejected=!candidates.motions.Any(m=>m.approved);
                File.WriteAllText(shared,publicReview.Replace("\"sha256\": \"","\"sha256\": \"changed-"));
                try{MocapReviewGate.Apply(publicCandidates());}catch(InvalidDataException){result.changedPublicRejected=true;}
            }
            finally{File.Delete(personal);File.WriteAllText(shared,publicReview);}
            PersonalMotionBuilder.Prepare();var restored=publicCandidates();MocapReviewGate.Apply(restored);
            result.restoredApprovalsPassed=restored.motions.Count(m=>m.approved)==8&&JsonUtility.FromJson<MotionCatalog>(File.ReadAllText("Assets/Resources/PersonalMotionCatalog.json")).motions.Count(m=>m.approved)==8;
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/approval-anatomy-regression.json",JsonUtility.ToJson(result,true));
            if(!result.legacyPersonalRejected||!result.changedPersonalRejected||!result.legacyPublicRejected||!result.changedPublicRejected||!result.restoredApprovalsPassed)throw new Exception("Approval anatomy regression failed.");
            Debug.Log("APPROVAL ANATOMY REGRESSION PASSED");
        }
    }
}
