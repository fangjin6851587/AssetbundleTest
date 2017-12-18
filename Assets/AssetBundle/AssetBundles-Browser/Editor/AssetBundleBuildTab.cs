using System;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using AssetBundleBrowser.AssetBundleDataSource;
using AssetBundles;
using Object = UnityEngine.Object;

namespace AssetBundleBrowser
{
    [System.Serializable]
    internal class AssetBundleBuildTab
    {
        private string m_streamingPath = "Assets/StreamingAssets";

        [SerializeField]
        private bool m_AdvancedSettings;

        [SerializeField]
        private Vector2 m_ScrollPosition;


        class ToggleData
        {
            internal ToggleData(bool s, 
                string title, 
                string tooltip,
                List<string> onToggles,
                BuildAssetBundleOptions opt = BuildAssetBundleOptions.None)
            {
                if (onToggles.Contains(title))
                    state = true;
                else
                    state = s;
                content = new GUIContent(title, tooltip);
                option = opt;
            }
            //internal string prefsKey
            //{ get { return k_BuildPrefPrefix + content.text; } }
            internal bool state;
            internal GUIContent content;
            internal BuildAssetBundleOptions option;
        }

        private AssetBundleInspectTab m_InspectTab;

        [SerializeField]
        private BuildTabData m_UserData;

        List<ToggleData> m_ToggleData;
        ToggleData m_ForceRebuild;
        ToggleData m_CopyToStreaming;
        ToggleData m_Encrypt;
        ToggleData m_Merge;
        ToggleData m_CopyUpdateListToLocal;

        GUIContent m_TargetContent;
        GUIContent m_CompressionContent;
        internal enum CompressOptions
        {
            Uncompressed = 0,
            StandardCompression,
            ChunkBasedCompression,
        }
        GUIContent[] m_CompressionOptions =
        {
            new GUIContent("No Compression"),
            new GUIContent("Standard Compression (LZMA)"),
            new GUIContent("Chunk Based Compression (LZ4)")
        };
        int[] m_CompressionValues = { 0, 1, 2 };


        internal AssetBundleBuildTab()
        {
            m_AdvancedSettings = false;
            m_UserData = new BuildTabData();
            m_UserData.m_OnToggles = new List<string>();
            m_UserData.m_UseDefaultPath = true;
        }

        internal void OnDisable()
        {
            var dataPath = System.IO.Path.GetFullPath(".");
            dataPath = dataPath.Replace("\\", "/");
            dataPath += "/Library/AssetBundleBrowserBuild.dat";

            BinaryFormatter bf = new BinaryFormatter();
            FileStream file = File.Create(dataPath);

            bf.Serialize(file, m_UserData);
            file.Close();

        }
        internal void OnEnable(Rect pos, EditorWindow parent)
        {
            m_InspectTab = (parent as AssetBundleBrowserMain).m_InspectTab;

            //LoadData...
            var dataPath = System.IO.Path.GetFullPath(".");
            dataPath = dataPath.Replace("\\", "/");
            dataPath += "/Library/AssetBundleBrowserBuild.dat";

            if (File.Exists(dataPath))
            {
                BinaryFormatter bf = new BinaryFormatter();
                FileStream file = File.Open(dataPath, FileMode.Open);
                var data = bf.Deserialize(file) as BuildTabData;
                if (data != null)
                    m_UserData = data;
                file.Close();
            }
            
            m_ToggleData = new List<ToggleData>();
            m_ToggleData.Add(new ToggleData(
                false,
                "Exclude Type Information",
                "Do not include type information within the asset bundle (don't write type tree).",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.DisableWriteTypeTree));
            m_ToggleData.Add(new ToggleData(
                false,
                "Force Rebuild",
                "Force rebuild the asset bundles",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.ForceRebuildAssetBundle));
            m_ToggleData.Add(new ToggleData(
                false,
                "Ignore Type Tree Changes",
                "Ignore the type tree changes when doing the incremental build check.",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.IgnoreTypeTreeChanges));
            m_ToggleData.Add(new ToggleData(
                false,
                "Append Hash",
                "Append the hash to the assetBundle name.",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.AppendHashToAssetBundleName));
            m_ToggleData.Add(new ToggleData(
                false,
                "Strict Mode",
                "Do not allow the build to succeed if any errors are reporting during it.",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.StrictMode));
            m_ToggleData.Add(new ToggleData(
                false,
                "Dry Run Build",
                "Do a dry run build.",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.DryRunBuild));


            m_ForceRebuild = new ToggleData(
                false,
                "Clear Folders",
                "Will wipe out all contents of build directory as well as StreamingAssets/AssetBundles if you are choosing to copy build there.",
                m_UserData.m_OnToggles);
            m_CopyToStreaming = new ToggleData(
                false,
                "Copy to StreamingAssets",
                "After build completes, will copy all build content to " + m_streamingPath + " for use in stand-alone player.",
                m_UserData.m_OnToggles);
            m_Encrypt = new ToggleData(
                false,
                "Encrpyt AssetBundle",
                "Encrypt AssetBundle with AES, you can change the key in the Crypto.cs file.",
                m_UserData.m_OnToggles);
            m_Merge = new ToggleData(
                false,
                "Merge AssetBundle",
                "Merge all AssetBundle into one file.",
                m_UserData.m_OnToggles);
            m_CopyUpdateListToLocal = new ToggleData(
                false,
                "Copy Local Assets Info",
                "Copy version and AssetBundle List file to local.",
                m_UserData.m_OnToggles);

            m_TargetContent = new GUIContent("Build Target", "Choose target platform to build for.");
            m_CompressionContent = new GUIContent("Compression", "Choose no compress, standard (LZMA), or chunk based (LZ4)");

            if(m_UserData.m_UseDefaultPath)
            {
                ResetPathToDefault();
            }
        }

        internal void OnGUI(Rect pos)
        {
            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
            bool newState = false;
            var centeredStyle = GUI.skin.GetStyle("Label");
            centeredStyle.alignment = TextAnchor.UpperCenter;
            GUILayout.Label(new GUIContent("Example build setup"), centeredStyle);
            //basic options
            EditorGUILayout.Space();
            GUILayout.BeginVertical();

            // build target
            using (new EditorGUI.DisabledScope (!AssetBundleModel.Model.DataSource.CanSpecifyBuildTarget)) {
                ValidBuildTarget tgt = (ValidBuildTarget)EditorGUILayout.EnumPopup(m_TargetContent, m_UserData.m_BuildTarget);
                if (tgt != m_UserData.m_BuildTarget)
                {
                    m_UserData.m_BuildTarget = tgt;
                    if(m_UserData.m_UseDefaultPath)
                    {
                        m_UserData.m_OutputPath = "AssetBundles/";
                        m_UserData.m_OutputPath += m_UserData.m_BuildTarget.ToString();
                        //EditorUserBuildSettings.SetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath", m_OutputPath);
                    }
                }
            }

            if (GUILayout.Button("Add Current Selected Folder", GUILayout.MaxWidth(200)))
            {
                AddSelectedFolder();
            }
            if (m_UserData.m_BuildFolderList != null && m_UserData.m_BuildFolderList.Count > 0)
            {
                EditorGUILayout.Space();
                GUILayout.BeginVertical();
                int buildFolderIndex = 1;
                int removeIndex = -1;
                foreach (var buildFolder in m_UserData.m_BuildFolderList)
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(buildFolderIndex.ToString(), buildFolder.Path);
                    buildFolder.SingleAssetBundle = GUILayout.Toggle(buildFolder.SingleAssetBundle, "Single AssetBundle");
                    if (buildFolder.SingleAssetBundle)
                    {
                        if (string.IsNullOrEmpty(buildFolder.AssetBundleName))
                        {
                            buildFolder.AssetBundleName = buildFolder.Path.Substring(7);
                            int resourceIndex = buildFolder.AssetBundleName.IndexOf("Resources/", StringComparison.Ordinal);
                            if (resourceIndex >= 0)
                            {
                                buildFolder.AssetBundleName = buildFolder.AssetBundleName.Substring(resourceIndex);
                            }
                        }
                        EditorGUILayout.LabelField("AssetBundle Name:", buildFolder.AssetBundleName);
                    }
                    else
                    {
                        buildFolder.AssetBundleName = string.Empty;
                    }
                    buildFolder.VariantType = EditorGUILayout.TextField("AssetBundle Variant", buildFolder.VariantType);
                    if (GUILayout.Button("-", GUILayout.MaxWidth(30)))
                    {
                        removeIndex = buildFolderIndex - 1;
                    }
                    buildFolderIndex++;
                    GUILayout.EndHorizontal();
                }

                if (removeIndex >= 0)
                {
                    m_UserData.m_BuildFolderList.RemoveAt(removeIndex);
                }

                GUILayout.EndVertical();
            }

            ////output path
            using (new EditorGUI.DisabledScope (!AssetBundleModel.Model.DataSource.CanSpecifyBuildOutputDirectory)) {
                EditorGUILayout.Space();
                GUILayout.BeginHorizontal();
                var newPath = EditorGUILayout.TextField("Output Path", m_UserData.m_OutputPath);
                if ( (newPath != m_UserData.m_OutputPath) &&
                     (newPath != string.Empty) )
                {
                    m_UserData.m_UseDefaultPath = false;
                    m_UserData.m_OutputPath = newPath;
                    //EditorUserBuildSettings.SetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath", m_OutputPath);
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Browse", GUILayout.MaxWidth(75f)))
                    BrowseForFolder();
                if (GUILayout.Button("Reset", GUILayout.MaxWidth(75f)))
                    ResetPathToDefault();
                //if (string.IsNullOrEmpty(m_OutputPath))
                //    m_OutputPath = EditorUserBuildSettings.GetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath");
                GUILayout.EndHorizontal();
                EditorGUILayout.Space();

                newState = GUILayout.Toggle(
                    m_ForceRebuild.state,
                    m_ForceRebuild.content);
                if (newState != m_ForceRebuild.state)
                {
                    if (newState)
                        m_UserData.m_OnToggles.Add(m_ForceRebuild.content.text);
                    else
                        m_UserData.m_OnToggles.Remove(m_ForceRebuild.content.text);
                    m_ForceRebuild.state = newState;
                }
                newState = GUILayout.Toggle(
                    m_CopyToStreaming.state,
                    m_CopyToStreaming.content);
                if (newState != m_CopyToStreaming.state)
                {
                    if (newState)
                        m_UserData.m_OnToggles.Add(m_CopyToStreaming.content.text);
                    else
                        m_UserData.m_OnToggles.Remove(m_CopyToStreaming.content.text);
                    m_CopyToStreaming.state = newState;
                }

                newState = GUILayout.Toggle(
                    m_Encrypt.state,
                    m_Encrypt.content);
                if (newState != m_Encrypt.state)
                {
                    if (newState)
                        m_UserData.m_OnToggles.Add(m_Encrypt.content.text);
                    else
                        m_UserData.m_OnToggles.Remove(m_Encrypt.content.text);
                    m_Encrypt.state = newState;
                }


                newState = GUILayout.Toggle(
                    m_Merge.state,
                    m_Merge.content);
                if (newState != m_Merge.state)
                {
                    if (newState)
                        m_UserData.m_OnToggles.Add(m_Merge.content.text);
                    else
                        m_UserData.m_OnToggles.Remove(m_Merge.content.text);
                    m_Merge.state = newState;
                }

                newState = GUILayout.Toggle(
                    m_CopyUpdateListToLocal.state,
                    m_CopyUpdateListToLocal.content);
                if (newState != m_CopyUpdateListToLocal.state)
                {
                    if (newState)
                        m_UserData.m_OnToggles.Add(m_CopyUpdateListToLocal.content.text);
                    else
                        m_UserData.m_OnToggles.Remove(m_CopyUpdateListToLocal.content.text);
                    m_CopyUpdateListToLocal.state = newState;
                }
            }

            // advanced options
            using (new EditorGUI.DisabledScope (!AssetBundleModel.Model.DataSource.CanSpecifyBuildOptions)) {
                EditorGUILayout.Space();
                m_AdvancedSettings = EditorGUILayout.Foldout(m_AdvancedSettings, "Advanced Settings");
                if(m_AdvancedSettings)
                {
                    var indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 1;
                    CompressOptions cmp = (CompressOptions)EditorGUILayout.IntPopup(
                        m_CompressionContent, 
                        (int)m_UserData.m_Compression,
                        m_CompressionOptions,
                        m_CompressionValues);

                    if (cmp != m_UserData.m_Compression)
                    {
                        m_UserData.m_Compression = cmp;
                    }
                    foreach (var tog in m_ToggleData)
                    {
                        newState = EditorGUILayout.ToggleLeft(
                            tog.content,
                            tog.state);
                        if (newState != tog.state)
                        {

                            if (newState)
                                m_UserData.m_OnToggles.Add(tog.content.text);
                            else
                                m_UserData.m_OnToggles.Remove(tog.content.text);
                            tog.state = newState;
                        }
                    }
                    EditorGUILayout.Space();
                    EditorGUI.indentLevel = indent;
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Set AssetBundle Name"))
            {
                EditorApplication.delayCall += ExecuteSetAssetBundleName;
            }


            // build.
            EditorGUILayout.Space();
            if (GUILayout.Button("Build") )
            {
                EditorApplication.delayCall += ExecuteBuild;
            }
            GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void ExecuteSetAssetBundleName()
        {
            AssetBundleBuilder builder = new AssetBundleBuilder();
            ABBuildInfo abBuildInfo = new ABBuildInfo();
            abBuildInfo.buildFolderList = m_UserData.m_BuildFolderList;
            builder.SetAssetBundleNames(abBuildInfo);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private void AddSelectedFolder()
        {
            Object[] selectedAsset = Selection.GetFiltered(typeof(Object), SelectionMode.DeepAssets);
            string dirPath = string.Empty;
            foreach (Object o in selectedAsset)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (Directory.Exists(path) && (string.IsNullOrEmpty(dirPath) || dirPath.Length > path.Length))
                {
                    dirPath = path;
                }
            }

            if (!string.IsNullOrEmpty(dirPath))
            {
                if (m_UserData.m_BuildFolderList == null)
                {
                    m_UserData.m_BuildFolderList = new List<BuildFolder>();
                }
                if (m_UserData.m_BuildFolderList.FindIndex(bf => bf.Path == dirPath) < 0)
                {
                    var buildFolder = new BuildFolder();
                    buildFolder.Path = dirPath;
                    m_UserData.m_BuildFolderList.Add(buildFolder);
                }
            }
        }
        private void ExecuteBuild()
        {
            if (AssetBundleModel.Model.DataSource.CanSpecifyBuildOutputDirectory) {
                if (string.IsNullOrEmpty(m_UserData.m_OutputPath))
                    BrowseForFolder();

                if (string.IsNullOrEmpty(m_UserData.m_OutputPath)) //in case they hit "cancel" on the open browser
                {
                    Debug.LogError("AssetBundle Build: No valid output path for build.");
                    return;
                }

                if (m_ForceRebuild.state)
                {
                    string message = "Do you want to delete all files in the directory " + m_UserData.m_OutputPath;
                    if (m_CopyToStreaming.state)
                        message += " and " + m_streamingPath;
                    message += "?";
                    if (EditorUtility.DisplayDialog("File delete confirmation", message, "Yes", "No"))
                    {
                        try
                        {
                            if (Directory.Exists(m_UserData.m_OutputPath))
                                Directory.Delete(m_UserData.m_OutputPath, true);

                            if (m_CopyToStreaming.state)
                            if (Directory.Exists(m_streamingPath))
                                Directory.Delete(m_streamingPath, true);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
                if (!Directory.Exists(m_UserData.m_OutputPath))
                    Directory.CreateDirectory(m_UserData.m_OutputPath);
            }

            BuildAssetBundleOptions opt = BuildAssetBundleOptions.None;

            if (AssetBundleModel.Model.DataSource.CanSpecifyBuildOptions) {
                if (m_UserData.m_Compression == CompressOptions.Uncompressed)
                    opt |= BuildAssetBundleOptions.UncompressedAssetBundle;
                else if (m_UserData.m_Compression == CompressOptions.ChunkBasedCompression)
                    opt |= BuildAssetBundleOptions.ChunkBasedCompression;
                foreach (var tog in m_ToggleData)
                {
                    if (tog.state)
                        opt |= tog.option;
                }
            }

            ABBuildInfo buildInfo = new ABBuildInfo();

            buildInfo.outputDirectory = m_UserData.m_OutputPath;
            buildInfo.options = opt;
            buildInfo.buildTarget = (BuildTarget)m_UserData.m_BuildTarget;
            buildInfo.buildFolderList = m_UserData.m_BuildFolderList;
            buildInfo.isEncrypt = m_Encrypt.state;
            buildInfo.mergeOneFile = m_Merge.state;
            buildInfo.copyLocalAssets = m_CopyUpdateListToLocal.state;
            buildInfo.onBuild = (assetBundleName) =>
            {
                if (m_InspectTab == null)
                    return;
                m_InspectTab.AddBundleFolder(buildInfo.outputDirectory);
                m_InspectTab.RefreshBundles();
            };

            AssetBundleBuilder builder = new AssetBundleBuilder();
            builder.BuildAssetBundle(buildInfo);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if(m_CopyToStreaming.state)
                DirectoryCopy(m_UserData.m_OutputPath, m_streamingPath);
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName)
        {
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            foreach (string folderPath in Directory.GetDirectories(sourceDirName, "*", SearchOption.AllDirectories))
            {
                if (!Directory.Exists(folderPath.Replace(sourceDirName, destDirName)))
                    Directory.CreateDirectory(folderPath.Replace(sourceDirName, destDirName));
            }

            foreach (string filePath in Directory.GetFiles(sourceDirName, "*.*", SearchOption.AllDirectories))
            {
                string newFilePath = Path.Combine(Path.GetDirectoryName(filePath).Replace(sourceDirName, destDirName),
                    Path.GetFileName(filePath));

                //using (var fs = new FileStream(filePath, FileMode.Open))
                //{
                //    byte[] buffer = new byte[fs.Length];
                //    fs.Read(buffer, 0, buffer.Length);
                //    buffer = Crypto.AesDecryptBytes(buffer);
                //    using (var newFs = new FileStream(newFilePath, FileMode.Create))
                //    {
                //        newFs.Write(buffer, 0, buffer.Length);
                //        newFs.SetLength(buffer.Length);
                //    }
                //}

                File.Copy(filePath, newFilePath, true);
            }
        }

        private void BrowseForFolder()
        {
            m_UserData.m_UseDefaultPath = false;
            var newPath = EditorUtility.OpenFolderPanel("Bundle Folder", m_UserData.m_OutputPath, string.Empty);
            if (!string.IsNullOrEmpty(newPath))
            {
                var gamePath = System.IO.Path.GetFullPath(".");
                gamePath = gamePath.Replace("\\", "/");
                if (newPath.StartsWith(gamePath) && newPath.Length > gamePath.Length)
                    newPath = newPath.Remove(0, gamePath.Length+1);
                m_UserData.m_OutputPath = newPath;
                //EditorUserBuildSettings.SetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath", m_OutputPath);
            }
        }
        private void ResetPathToDefault()
        {
            m_UserData.m_UseDefaultPath = true;
            m_UserData.m_OutputPath = "AssetBundles/";
            m_UserData.m_OutputPath += m_UserData.m_BuildTarget.ToString();
            //EditorUserBuildSettings.SetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath", m_OutputPath);
        }

        //Note: this is the provided BuildTarget enum with some entries removed as they are invalid in the dropdown
        internal enum ValidBuildTarget
        {
            //NoTarget = -2,        --doesn't make sense
            //iPhone = -1,          --deprecated
            //BB10 = -1,            --deprecated
            //MetroPlayer = -1,     --deprecated
            StandaloneOSXUniversal = 2,
            StandaloneOSXIntel = 4,
            StandaloneWindows = 5,
            WebPlayer = 6,
            WebPlayerStreamed = 7,
            iOS = 9,
            PS3 = 10,
            XBOX360 = 11,
            Android = 13,
            StandaloneLinux = 17,
            StandaloneWindows64 = 19,
            WebGL = 20,
            WSAPlayer = 21,
            StandaloneLinux64 = 24,
            StandaloneLinuxUniversal = 25,
            WP8Player = 26,
            StandaloneOSXIntel64 = 27,
            BlackBerry = 28,
            Tizen = 29,
            PSP2 = 30,
            PS4 = 31,
            PSM = 32,
            XboxOne = 33,
            SamsungTV = 34,
            N3DS = 35,
            WiiU = 36,
            tvOS = 37,
            Switch = 38
        }

        [System.Serializable]
        internal class BuildTabData
        {
            internal List<string> m_OnToggles;
            internal ValidBuildTarget m_BuildTarget = ValidBuildTarget.StandaloneWindows;
            internal CompressOptions m_Compression = CompressOptions.StandardCompression;
            internal string m_OutputPath = string.Empty;
            internal bool m_UseDefaultPath = true;
            internal List<BuildFolder> m_BuildFolderList = new List<BuildFolder>();
        }
    }

}