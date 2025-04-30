#if CASDeveloper
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Xml;
using System.Text.RegularExpressions;

namespace CAS.UEditor
{
    public static class ExportPackages
    {
        private const string casUnityRepoPath = "Assets/CleverAdsSolutions";

        [MenuItem("Release/Copy Native Unity bridge", priority = 99)]
        public static void CopyUnityBridge()
        {
            string root = casUnityRepoPath + "/";

            var androidLibPath = Path.Combine(GetNativeRepoDir(BuildTarget.Android), "buildCAS", "CASUnityBridge.aar");
            if (File.Exists(androidLibPath))
            {
                var destPath = root + "/Plugins/Android/CASUnityBridge.aar";
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(androidLibPath, destPath);
                AssetDatabase.ImportAsset(destPath);
                Debug.Log("Updated from: " + androidLibPath + "\nto: " + destPath);
            }
            var iOSBridgeDir = Path.Combine(GetNativeRepoDir(BuildTarget.iOS), "CASUnityBridge", "CASUnityBridge");
            if (Directory.Exists(iOSBridgeDir))
            {
                var files = Directory.GetFiles(iOSBridgeDir);
                for (int i = 0; i < files.Length; i++)
                {
                    var fileName = Path.GetFileName(files[i]);
                    if (fileName == ".DS_Store")
                        continue;
                    CopyFile(files[i], root + "Plugins/iOS/" + fileName);
                }
            }
        }

        [MenuItem("Release/SK AdNetworks list", priority = 101)]
        public static void CopySKAdNetworkList()
        {
            var itemsFile = Path.Combine(GetNativeRepoDir(BuildTarget.iOS), "PublicSamplesRepo", "SKAdNetworkCompact.txt");
            if (File.Exists(itemsFile))
            {
                File.Copy(itemsFile, casUnityRepoPath + "/Editor/BuildConfig/CASSKAdNetworks.txt", true);
            }
            else
            {
                Debug.LogError("File not found: " + itemsFile +
                    "\nYou can change path to files in `Release/Custom settings` asset.");
            }
        }

        [MenuItem("Release/Clever Ads Solutions", priority = 10)]
        public static void ExportCAS()
        {
            UpdateDependencies();
            CopySKAdNetworkList();
            CopyUnityBridge();
            CopySampleAssets();
            ExportCASArchive();
        }

        [MenuItem("Release/Sample assets", priority = 102)]
        public static void CopySampleAssets()
        {
            CopySampleAssets("SampleComponents");
            CopySampleAssets("SampleScripts");
        }

        private static void CopySampleAssets(string dirName)
        {
            string targetDir = Path.Combine(Path.GetFullPath(casUnityRepoPath), "Samples~", dirName);
            string[] files = Directory.GetFiles(Path.Combine(Application.dataPath, dirName), "*");

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, true);
            }
        }

        private static string GetNativeRepoDir(BuildTarget platform)
        {
            var source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            if (platform == BuildTarget.Android)
                return Path.Combine(source, "CAS-Kotlin");
            return Path.Combine(source, "CAS-Swift");
        }

        private static string PrepareMediationAndGetVersion(BuildTarget platform)
        {
            var template = casUnityRepoPath + "/Editor/BuildConfig/CAS" + platform.ToString() + "Mediation.list";
            var source = Path.Combine(GetNativeRepoDir(platform), "CASMediation.list");

            if (!File.Exists(source))
            {
                Debug.LogError("Invalid path: " + source);
                return MobileAds.wrapperVersion;
            }
            File.Copy(source, template, true);

            return Regex.Match(File.ReadAllText(template), "\"version\":\\s*\"(.*?)\"").Groups[1].Value;
        }

        [MenuItem("Release/Mediation List Specs", priority = 100)]
        public static void UpdateDependencies()
        {
            var settings = new XmlWriterSettings()
            {
                Indent = true,
                CloseOutput = true,
            };

            var androidVersion = PrepareMediationAndGetVersion(BuildTarget.Android);
            var iosVersion = PrepareMediationAndGetVersion(BuildTarget.iOS);

            if (androidVersion != CAS.MobileAds.wrapperVersion || iosVersion != CAS.MobileAds.wrapperVersion)
            {
                if (!EditorUtility.DisplayDialog("Plugin version missmatch",
                        "Plugin: " + CAS.MobileAds.wrapperVersion +
                        "\nAndroid: " + androidVersion +
                        "\nIOS: " + iosVersion,
                    "Continue", "Cancel"))
                {
                    throw new System.Exception("Canceled");
                }
            }
            var baseDepPath = Path.GetFullPath(CASEditorUtils.editorFolderPath + "/CASBaseDependencies.xml");
            using (var xml = XmlWriter.Create(baseDepPath, settings))
            {
                xml.WriteStartElement("dependencies");

                xml.WriteStartElement("androidPackages");
                // Check beta version. Such as: 1.1.1-rc1 or 1.1.1-beta1
                if (androidVersion.IndexOf('-') > 0)
                {
                    xml.WriteStartElement("repositories");
                    xml.WriteElementString("repository", "https://repo.repsy.io/mvn/cleveradssolutions/beta");
                    xml.WriteEndElement();
                }
                WriteAndroidDep(xml, "com.cleveradssolutions:cas-sdk:", androidVersion);
                WriteAndroidDep(xml, "androidx.lifecycle:lifecycle-process:", "2.6.2");
                xml.WriteEndElement();

                xml.WriteStartElement("iosPods");
                xml.WriteStartElement("sources");
                xml.WriteElementString("source", "https://github.com/cleveradssolutions/CAS-Specs.git");
                xml.WriteEndElement();
                WriteIOSDep(xml, "CleverAdsSolutions-Base", iosVersion);
                xml.WriteEndElement();

                xml.WriteEndElement();
                xml.Flush();
            }
        }

        private static void WriteAndroidDep(XmlWriter xml, string dependency, string version)
        {
            xml.WriteStartElement("androidPackage");
            xml.WriteAttributeString("spec", dependency + version);
            xml.WriteAttributeString("version", version);
            xml.WriteEndElement();
        }

        private static void WriteIOSDep(XmlWriter xml, string dependency, string version)
        {
            xml.WriteStartElement("iosPod");
            xml.WriteAttributeString("name", dependency);
            xml.WriteAttributeString("version", version);
            xml.WriteAttributeString("minTargetSdk", CASEditorUtils.targetIOSVersion + ".0");
            xml.WriteEndElement();
        }

#if false
        [MenuItem("Release/Generate Asset manifests", priority = 110)]
        public static void GenerateAssetManifest()
        {
            //var asset = CustomReleaseSettings.Load();
            //var files = Directory.GetFiles(asset.casUnityRepoPath, "*.*", SearchOption.AllDirectories)
            //    .Where((file) => !file.EndsWith(".meta") && !file.EndsWith("/.DS_Store"))
            //    .ToArray();

            Google.VersionHandler.Enabled = false;
            Google.VersionHandlerImpl.Enabled = false;

            SaveManifest("3.2.0", new[]
            {
                "Assets/CleverAdsSolutions/Editor/CASBaseDependencies.xml"
            });

            SaveManifest("2.0.0", new[]{
                "Assets/CleverAdsSolutions/Runtime/Common/AdError.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/AdMetaData.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/AdNetwork.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/AdPosition.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/AdSize.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/AdType.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/ConsentFlow.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/EventExecutor.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/IAdsPreset.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/IAdsSettings.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/IAdView.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/IManagerBuilder.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/IMediationManager.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/ISingleBannerManager.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/ITargetingOptions.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/LastPageAdContent.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/LoadingManagerMode.cs",
                "Assets/CleverAdsSolutions/Runtime/Common/PriceAccuracy.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/Android/CASJavaProxy.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/Android/CASMediationManager.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/Android/CASSettings.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/Android/CASView.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/CASViewFactory.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/iOS/CASMediationManager.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/iOS/CASSettings.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/iOS/CASView.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/Unity/CASMediationManager.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/Unity/CASSettings.cs",
                "Assets/CleverAdsSolutions/Runtime/Internal/Unity/CASView.cs",
                "Assets/Plugins/Android/CASPlugin.androidlib/res/raw/cas_settings.json",
                "Assets/CleverAdsSolutions/Editor/CASAndroidBaseDependencies.xml",
                "Assets/CleverAdsSolutions/Editor/CASiOSBaseDependencies.xml",
                "Assets/Plugins/CAS/res/raw/cas_settings.json",
                "Assets/Plugins/CAS/AndroidManifest.xml",
                "Assets/Plugins/CAS/project.properties",
            });
            AssetDatabase.Refresh();
        }

        private static void DeleteAssetManifest()
        {
            AssetDatabase.DeleteAsset(GetManifestPath("2.0.0"));
            AssetDatabase.DeleteAsset(GetManifestPath("3.2.0"));
            Google.VersionHandler.Enabled = true;
            Google.VersionHandlerImpl.Enabled = true;
        }

        private static void SaveManifest(string version, string[] assets)
        {
            var path = GetManifestPath(version);
            File.WriteAllLines(path, assets);
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            AssetDatabase.SetLabels(asset, new[]
            {
            "gvh",
            "CleverAdsSolutions",
            "gvh_manifest",
            "gvh_version-" + version,
            "gvhp_manifestname-CleverAdsSolutions"
        });
        }

        private static string GetManifestPath(string version)
        {
            return casUnityRepoPath + "/Editor/clever-ads-solutions-version-" + version + "-manifest.txt";
        }
#endif

        [MenuItem("Release/CAS Unity package", priority = 98)]
        public static void ExportCASArchive()
        {
            //GenerateAssetManifest();

            string root = casUnityRepoPath + "/";
            SetVersionInManifest(root + "package.json", MobileAds.wrapperVersion);

            string[] assets =
            {
                root + "Editor",
                root + "LICENSE.md",
                root + "Plugins",
                root + "Runtime"
            };

            if (!Directory.Exists("ExportedPackages"))
                Directory.CreateDirectory("ExportedPackages");

            var path = "ExportedPackages/CleverAdsSolutions.unitypackage";
            AssetDatabase.ExportPackage(
                assets,
                path,
                ExportPackageOptions.Recurse);
            EditorUtility.DisplayDialog("Package", "Exported to " + path, "OK");
            //DeleteAssetManifest();
        }

        private static void CopyFile(string from, string to)
        {
            File.Copy(from, to, true);
            AssetDatabase.ImportAsset(to);
            Debug.Log("Updated from: " + from + "\nto: " + to);
        }

        private static void SetVersionInManifest(string path, string version)
        {
            const string begin = "\"version\": \"";
            var manifest = File.ReadAllText(path);
            int start = manifest.IndexOf(begin) + begin.Length;
            var textBefore = manifest.Substring(0, start);
            var textAfter = manifest.Substring(manifest.IndexOf('\"', start));
            manifest = textBefore + version + textAfter;
            File.WriteAllText(path, manifest);
            AssetDatabase.ImportAsset(path);
        }
    }
}
#endif