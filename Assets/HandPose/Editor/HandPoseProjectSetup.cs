using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using Unity.XR.PXR;

namespace HandPose.Editor
{
    public static class HandPoseProjectSetup
    {
        public const string DemoScene="Assets/HandPose/Scenes/HandPoseDemo.unity";
        [MenuItem("Hand Pose/Generate demo and configure Android")]
        public static void GenerateProject()
        {
            Directory.CreateDirectory("Assets/HandPose/Scenes"); Directory.CreateDirectory("Assets/Resources"); Directory.CreateDirectory("Assets/XR/Settings");
            AssetDatabase.Refresh();
            var config=AssetDatabase.LoadAssetAtPath<PoseConfiguration>("Assets/Resources/DefaultPoseConfiguration.asset");
            if(!config) { config=ScriptableObject.CreateInstance<PoseConfiguration>(); AssetDatabase.CreateAsset(config,"Assets/Resources/DefaultPoseConfiguration.asset"); }
            config.settings=config.settings.CopyForLive(); EditorUtility.SetDirty(config);
            // SDK 3.4.0's Editor/development initializer dereferences this resource without a null guard.
            // Supply its normal configuration asset instead of modifying SDK code.
            if(!Resources.Load<Unity.XR.PXR.Debugger.PXR_PicoDebuggerSO>("PXR_PicoDebuggerSO"))
            {
                var debugger=ScriptableObject.CreateInstance<Unity.XR.PXR.Debugger.PXR_PicoDebuggerSO>();
                debugger.isOpen=false;
                AssetDatabase.CreateAsset(debugger,"Assets/Resources/PXR_PicoDebuggerSO.asset");
            }
            if(!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/JointMarkerMaterial.mat"))
            {
                var shader=Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if(!shader) throw new InvalidOperationException("No unlit marker shader is available.");
                AssetDatabase.CreateAsset(new Material(shader),"Assets/Resources/JointMarkerMaterial.mat");
            }
            if(!File.Exists(DemoScene))
            {
                if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var origin=new GameObject("Tracking Origin (meters)");
                var cameraObject=new GameObject("Main Camera"); cameraObject.tag="MainCamera"; cameraObject.transform.SetParent(origin.transform,false);
                cameraObject.transform.localPosition=new Vector3(0,1.5f,0);
                var camera=cameraObject.AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.018f,.027f,.045f);
                camera.nearClipPlane=.03f; camera.farClipPlane=20;
                cameraObject.AddComponent<AudioListener>(); cameraObject.AddComponent<TrackingOriginCamera>();
                origin.AddComponent<PXR_Manager>();
                var system=new GameObject("Hand Pose System"); var app=system.AddComponent<PoseApplication>();
                app.configuration=config; app.headCamera=camera; app.trackingOrigin=origin.transform;
                app.pico=system.AddComponent<PicoHandJointProvider>(); app.synthetic=system.AddComponent<SyntheticHandJointProvider>(); app.replay=system.AddComponent<ReplayHandJointProvider>();
                system.AddComponent<PoseDebugView>().app=app;
                system.AddComponent<HandPoseHandView>().app=app;
                EditorSceneManager.SaveScene(scene,DemoScene);
            }
            var picoConfig=PXR_ProjectSetting.GetProjectConfig(); picoConfig.handTracking=true; picoConfig.handTrackingSupportType=HandTrackingSupport.HandsOnly;
            // The demo does not capture video or use adaptive meshes/high-frequency mode for classification.
            EditorUtility.SetDirty(picoConfig);
            ConfigureLoader();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.applicationEntry=AndroidApplicationEntry.Activity;
            // Keep the original device installation and its data as a separate app.
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.labolatry.handpose.benttest");
            PlayerSettings.productName = "HandPose Bent Test";
            PlayerSettings.runInBackground=true; PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[] {GraphicsDeviceType.OpenGLES3});
            var scenes=new List<EditorBuildSettingsScene> {new EditorBuildSettingsScene(DemoScene,true)};
            foreach(var s in EditorBuildSettings.scenes) if(s.path!=DemoScene) scenes.Add(s);
            EditorBuildSettings.scenes=scenes.ToArray(); AssetDatabase.SaveAssets();
            Debug.Log("HandPose setup complete. Unity="+Application.unityVersion+", PICO=3.4.0, native loader, ARM64/IL2CPP. Existing Unity version and SampleScene preserved.");
        }
        static void ConfigureLoader()
        {
            XRGeneralSettingsPerBuildTarget container;
            if(!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,out container))
            {
                container=ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(container,"Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,container,true);
            }
            var general=container.SettingsForBuildTarget(BuildTargetGroup.Android);
            if(!general)
            {
                container.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
                general=container.SettingsForBuildTarget(BuildTargetGroup.Android);
            }
            if(!general.Manager)
            {
                var manager=ScriptableObject.CreateInstance<XRManagerSettings>(); manager.name="Android XR Manager";
                AssetDatabase.AddObjectToAsset(manager,container); general.Manager=manager;
            }
            var loaders=new List<XRLoader>(general.Manager.activeLoaders);
            if(loaders.Count>0 && !(loaders.Count==1 && loaders[0] is PXR_Loader)) throw new InvalidOperationException("Another Android XR loader exists. Review it before changing SDK input paths.");
            if(loaders.Count==0)
            {
                var loader=ScriptableObject.CreateInstance<PXR_Loader>(); loader.name="PICO Native Loader";
                AssetDatabase.CreateAsset(loader,"Assets/XR/Settings/PicoNativeLoader.asset");
                if(!general.Manager.TryAddLoader(loader)) throw new InvalidOperationException("Cannot add PICO XR loader.");
            }
            general.InitManagerOnStart=true; general.Manager.automaticLoading=true; general.Manager.automaticRunning=true;
            EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager); EditorUtility.SetDirty(container);
            if(!PXR_Settings.GetSettings())
            {
                var settings=ScriptableObject.CreateInstance<PXR_Settings>();
                settings.stereoRenderingModeAndroid=PXR_Settings.StereoRenderingModeAndroid.Multiview;
                AssetDatabase.CreateAsset(settings,"Assets/XR/Settings/PicoSettings.asset");
                EditorBuildSettings.AddConfigObject("Unity.XR.PXR.Settings",settings,true);
            }
        }
        [MenuItem("Hand Pose/Build Android APK")]
        public static void BuildAndroid()
        {
            GenerateProject(); Directory.CreateDirectory("Builds");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[] {DemoScene},locationPathName="Builds/HandPose-PICO4Ultra.apk",target=BuildTarget.Android,options=BuildOptions.Development});
            var summary=report.summary;
            File.WriteAllText("Builds/build-result.txt",$"Result: {summary.result}\nUnity: {Application.unityVersion}\nSDK: PICO Integration 3.4.0\nPlatform: Android ARM64 IL2CPP\nErrors: {summary.totalErrors}\nWarnings: {summary.totalWarnings}\nDuration: {summary.totalTime}\nOutput: {summary.outputPath}\n");
            if(summary.result!=BuildResult.Succeeded) throw new Exception("Android build failed: "+summary.result);
            Debug.Log("HandPose Android APK built: "+summary.outputPath);
        }
    }
}
