using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SongChoreography.Editor
{
    public static class StudioBuild
    {
        [Serializable] class BuildMetadata
        {
            public int schemaVersion=3,characters,motions,reviewedMotions,transitions,playbackRevision;
            public string version="0.6.0",builtAtUtc,characterSignature;
            public bool requiresGeneration;
        }
        [MenuItem("Tools/CHOREO AI/Prepare studio")]
        public static void Prepare()
        {
            GeneratedAssetBuilder.Import();
            Directory.CreateDirectory("Assets/Scenes");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            // Runtime bootstrap creates stage and application consistently in
            // editor, shipped builds, empty libraries and prepared libraries.
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/Studio.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Studio.unity",true)};
            PlayerSettings.companyName="SongChoreography";PlayerSettings.productName="CHOREO AI Studio";PlayerSettings.bundleVersion="0.6.0";
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=960;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);PlayerSettings.colorSpace=ColorSpace.Linear;QualitySettings.vSyncCount=1;
            var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders=graphics.FindProperty("m_AlwaysIncludedShaders");var standard=Shader.Find("Standard");bool has=false;
            for(int i=0;i<shaders.arraySize;i++)has|=shaders.GetArrayElementAtIndex(i).objectReferenceValue==standard;
            if(!has){shaders.InsertArrayElementAtIndex(shaders.arraySize);shaders.GetArrayElementAtIndex(shaders.arraySize-1).objectReferenceValue=standard;graphics.ApplyModifiedPropertiesWithoutUndo();}
            AssetDatabase.SaveAssets();Debug.Log("AI STUDIO PREPARED");
        }
        [MenuItem("Tools/CHOREO AI/Build Windows")]
        public static void Build()
        {
            Prepare();
            var library=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);Choreographer.ValidateCatalog(library.catalog);
            if(library.importIssues.Length>0)throw new InvalidDataException("素材导入错误："+string.Join("; ",library.importIssues));
            string characterId=library.catalog.characters.FirstOrDefault()?.id;
            int reviewed=library.catalog.motions.Count(m=>m.reviewedCharacters.Contains(characterId));
            if(library.characters.Length>0&&library.motions.Length>0&&reviewed==0)throw new InvalidDataException("人物素材已导入，但动作复查签名失效，不能发布不可编排的成品。");
            Directory.CreateDirectory("Builds/AIStudio");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Studio.unity"},locationPathName="Builds/AIStudio/ChoreoAIStudio.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Unity build: "+report.summary.result);
            File.WriteAllText("Builds/AIStudio/BUILD.json",JsonUtility.ToJson(new BuildMetadata{characters=library.characters.Length,motions=library.motions.Length,reviewedMotions=reviewed,transitions=library.catalog.transitions.Count,playbackRevision=MotionPlayer.PlaybackRevision,builtAtUtc=DateTime.UtcNow.ToString("o"),characterSignature=library.catalog.characters.FirstOrDefault()?.signature??"",requiresGeneration=library.characters.Length==0||library.motions.Length==0},true));
            Debug.Log("AI STUDIO BUILD SUCCEEDED");
        }
    }
}
