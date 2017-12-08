using System.Collections.Generic;
using System.IO;
using AssetBundleBrowser.AssetBundleDataSource;
using UnityEditor;
using UnityEngine;

namespace AssetBundleBrowser
{
    public class AssetNode
    {
        public readonly List<AssetNode> Parents = new List<AssetNode>();
        public string Path;
        public int Depth;
    }

    [System.Serializable]
    public class BuildAsset
    {
        public string Path { get; private set; }

        public bool CollectDependency { get; private set; }

        public BuildAsset(string path, bool collectDependency)
        {
            Path = path;
            CollectDependency = collectDependency;
        }
    }


    public class AssetBundleBuilder
    {
        //需要打包的资源路径（相对于Assets目录），通常是prefab,lua,及其他数据。（贴图，动画，模型，材质等可以通过依赖自己关联上，不需要添加在该路径里，除非是特殊需要）
        //注意这里是目录，单独零散的文件，可以新建一个目录，都放在里面打包
        public static List<string> abResourcePath = new List<string>()
        {
		    //"Examples/Prefab",
		    "AssetBundleSample/Prefabs",
        };

        private static string ASSSETS_STRING = "Assets";

        private static readonly List<AssetNode> sLeafNodes = new List<AssetNode>();
        private static readonly Dictionary<string, AssetNode> sAllAssetNodes = new Dictionary<string, AssetNode>();
        private static readonly List<string> sBuildMap = new List<string>();
        private static ABBuildInfo sABBuildInfo;

        public static void BuildAssetBundle(ABBuildInfo buildInfo)
        {
            sABBuildInfo = buildInfo;
            sBuildMap.Clear();
            sLeafNodes.Clear();
            sAllAssetNodes.Clear();

            CollectDependcy();
            BuildResourceBuildMap();
            BuildAssetBundleWithBuildMap();
        }

        private static void ClearAssetBundleNames()
        {
            var assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
            foreach(string name in assetBundleNames)
            {
                AssetDatabase.RemoveAssetBundleName(name, true);
            }
        }

        private static void CollectDependcy()
        {
            for (int i = 0; i < abResourcePath.Count; i++)
            {
                string path = Application.dataPath + "/" + abResourcePath[i];
                if (!Directory.Exists(path))
                {
                    Debug.LogError(string.Format("abResourcePath {0} not exist", abResourcePath[i]));
                }
                else
                {
                    DirectoryInfo dir = new DirectoryInfo(path);
                    FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);
                    for (int j = 0; j < files.Length; j++)
                    {
                        if (CheckFileSuffixNeedIgnore(files[j].Name))
                            continue;
                        string fileRelativePath = GetReleativeToAssets(files[j].FullName);
                        AssetNode root;
                        sAllAssetNodes.TryGetValue(fileRelativePath, out root);
                        if (root == null)
                        {
                            root = new AssetNode();
                            root.Path = fileRelativePath;
                            sAllAssetNodes[root.Path] = root;
                            GetDependcyRecursive(fileRelativePath, root);
                        }
                    }
                }
            }
            //PrintDependcy();
        }

        private static void GetDependcyRecursive(string path, AssetNode parentNode)
        {
            string[] dependcy = AssetDatabase.GetDependencies(path, false);
            for (int i = 0; i < dependcy.Length; i++)
            {
                AssetNode node;
                sAllAssetNodes.TryGetValue(dependcy[i], out node);
                if (node == null)
                {
                    node = new AssetNode();
                    node.Path = dependcy[i];
                    node.Depth = parentNode.Depth + 1;
                    node.Parents.Add(parentNode);
                    sAllAssetNodes[node.Path] = node;
                    GetDependcyRecursive(dependcy[i], node);
                }
                else
                {
                    if (!node.Parents.Contains(parentNode))
                    {
                        node.Parents.Add(parentNode);
                    }
                    if (node.Depth < parentNode.Depth + 1)
                    {
                        node.Depth = parentNode.Depth + 1;
                        GetDependcyRecursive(dependcy[i], node);
                    }

                }
                //Debug.Log("dependcy path is " +dependcy[i] + " parent is " + parentNode.path);
            }
            if (dependcy.Length == 0)
            {
                if (!sLeafNodes.Contains(parentNode))
                {
                    sLeafNodes.Add(parentNode);
                }
            }
        }

        private static void BuildResourceBuildMap()
        {
            int maxDepth = GetMaxDepthOfLeafNodes();
            while (sLeafNodes.Count > 0)
            {
                List<AssetNode> curDepthNodesList = new List<AssetNode>();
                for (int i = 0; i < sLeafNodes.Count; i++)
                {
                    if (sLeafNodes[i].Depth == maxDepth)
                    {
                        if (sLeafNodes[i].Parents.Count != 1)
                        {
                            if (!ShouldIgnoreFile(sLeafNodes[i].Path))
                            {
                                sBuildMap.Add(sLeafNodes[i].Path);
                            }
                        }
                        curDepthNodesList.Add(sLeafNodes[i]);
                    }
                }
                for (int i = 0; i < curDepthNodesList.Count; i++)
                {
                    sLeafNodes.Remove(curDepthNodesList[i]);
                    foreach (AssetNode node in curDepthNodesList[i].Parents)
                    {
                        if (!sLeafNodes.Contains(node))
                        {
                            sLeafNodes.Add(node);
                        }
                    }
                }
                maxDepth -= 1;
            }
        }

        private static bool ShouldIgnoreFile(string path)
        {
            if (path.EndsWith(".cs"))
                return true;
            return false;
        }

        private static int GetMaxDepthOfLeafNodes()
        {
            if (sLeafNodes.Count == 0)
                return 0;
            sLeafNodes.Sort((x, y) => y.Depth - x.Depth);
            return sLeafNodes[0].Depth;
        }

        private static bool BuildAssetBundleWithBuildMap()
        {
            ClearAssetBundleNames();

            foreach (string path in sBuildMap)
            {
                AssetImporter assetImporter = AssetImporter.GetAtPath(path);
                assetImporter.SetAssetBundleNameAndVariant(path.Substring(ASSSETS_STRING.Length + 1), string.Empty);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!Directory.Exists(sABBuildInfo.outputDirectory))
                Directory.CreateDirectory(sABBuildInfo.outputDirectory);
            var buildManifest = BuildPipeline.BuildAssetBundles(sABBuildInfo.outputDirectory, sABBuildInfo.options, sABBuildInfo.buildTarget);

            if (buildManifest == null)
            {
                Debug.Log("Error in build");
                return false;
            }

            foreach (var assetBundleName in buildManifest.GetAllAssetBundles())
            {
                if (sABBuildInfo.onBuild != null)
                {
                    sABBuildInfo.onBuild(assetBundleName);
                }
            }
            return true;
        }

        private static string GetReleativeToAssets(string fullName)
        {
            string fileRelativePath = fullName.Substring(Application.dataPath.Length - ASSSETS_STRING.Length);
            fileRelativePath = fileRelativePath.Replace(@"\\", "/");
            return fileRelativePath;
        }

        private static bool CheckFileSuffixNeedIgnore(string fileName)
        {
            if (fileName.EndsWith(".meta") || fileName.EndsWith(".DS_Store") || fileName.EndsWith(".cs"))
                return true;
            return false;
        }
    }
}


