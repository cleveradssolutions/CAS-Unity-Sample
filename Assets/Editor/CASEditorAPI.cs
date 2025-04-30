using System;
using UnityEngine;
using UnityEditor;

namespace CAS.UEditor
{
    /// <summary>
    /// Attention. This script is not the final implementation; 
    /// it is just an example of potential use. 
    /// 
    /// In the future, the CAS Unity plugin will introduce new public functionality 
    /// for configuring the application using only editor scripts.
    /// 
    /// Please feel free to create an GitHub issue if you encounter any problems 
    /// or wish to request a feature, and we will definitely review it.
    /// </summary>
    public static class CASEditorAPI
    {
        public static void ConfigureApp(BuildTarget target, string casId, bool testMode, AdFlags usedAds)
        {
            var asset = CASEditorUtils.GetSettingsAsset(target);
            var serializedObject = new SerializedObject(asset);
            serializedObject.UpdateIfRequiredOrScript();

            var managerIdsProp = serializedObject.FindProperty("managerIds");
            managerIdsProp.arraySize = 1;
            managerIdsProp.GetArrayElementAtIndex(0).stringValue = casId;

            var testAdModeProp = serializedObject.FindProperty("testAdMode");
            testAdModeProp.boolValue = testMode;

            var usedTypesProp = serializedObject.FindProperty("allowedAdFlags");
            usedTypesProp.intValue = (int)usedAds;

            serializedObject.ApplyModifiedProperties();
        }

        public static void ActivateFamiliesSolution(BuildTarget target){
            var manager = DependencyManager.Create(target, Audience.Mixed, true);
            var dependency = manager.Find(Dependency.adsFamilies);
            dependency.ActivateDependencies(target, manager);

            if (target == BuildTarget.Android)
            {
                bool success = CASEditorUtils.TryResolveAndroidDependencies();
            }
        }
    }
}