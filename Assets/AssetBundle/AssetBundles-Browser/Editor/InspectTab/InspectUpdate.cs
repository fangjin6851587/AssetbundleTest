using System.Collections.Generic;
using System.Linq;
using AssetBundles;
using UnityEditor;
using UnityEngine;

namespace AssetBundleBrowser
{
    public class InspectUpdate
    {
        internal InspectUpdate() { }

        private AssetBundleUpdateInfo mUpdateInfo;

        private Rect mPosition;

        [SerializeField]
        private Vector2 mScrollPosition;

        private bool mAllVariant;
        private bool mPendingList;

        internal class AssetBundleInfoFoldout
        {
            public bool Foldout;
            public bool DependenciesFoldout;
        }

        private Dictionary<string, AssetBundleInfoFoldout> mAssetBundleInfoFoldouts = new Dictionary<string, AssetBundleInfoFoldout>();

        internal void SetUpdateInfo(AssetBundleUpdateInfo updateInfo)
        {
            mAssetBundleInfoFoldouts.Clear();
            //members
            mUpdateInfo = updateInfo;
            if (updateInfo != null)
            {
                foreach (var keyPairValue in mUpdateInfo.PendingList)
                {
                    mAssetBundleInfoFoldouts.Add(keyPairValue.Key, new AssetBundleInfoFoldout());
                }
            }
        }

        internal void OnGUI(Rect pos)
        {
            mPosition = pos;

            DrawBundleData();
        }

        private void DrawBundleData()
        {
            if (mUpdateInfo != null)
            {
                GUILayout.BeginArea(mPosition);
                mScrollPosition = EditorGUILayout.BeginScrollView(mScrollPosition);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.LabelField("Current Version", mUpdateInfo.CurrentVersion.ToString());
                    EditorGUILayout.LabelField("Target Version", mUpdateInfo.CurrentVersion.ToString());
                    mAllVariant = EditorGUILayout.Foldout(mAllVariant, "All Variant");
                    if (mAllVariant)
                    {
                        int indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 1;
                        int size = mUpdateInfo.AllAssetBundlesWithVariant.Length;
                        EditorGUILayout.LabelField("Size", size.ToString());
                        for (int i = 0; i < size; i++)
                        {
                            EditorGUILayout.LabelField((i + 1).ToString(), mUpdateInfo.AllAssetBundlesWithVariant[i]);
                        }
                        EditorGUI.indentLevel = indent;
                    }

                    mPendingList = EditorGUILayout.Foldout(mPendingList, "Pending List");
                    if (mPendingList)
                    {
                        int indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 1;
                        int size = mUpdateInfo.PendingList.Count;
                        EditorGUILayout.LabelField("Size", size.ToString());
                        foreach (var keyPairValue in mUpdateInfo.PendingList)
                        {
                            var foldout = mAssetBundleInfoFoldouts[keyPairValue.Key];
                            foldout.Foldout = EditorGUILayout.Foldout(foldout.Foldout, keyPairValue.Key);
                            if (foldout.Foldout)
                            {
                                EditorGUI.indentLevel = 2;
                                EditorGUILayout.LabelField("AssetBundleName", keyPairValue.Value.AssetBundleName);
                                EditorGUILayout.LabelField("Hash", keyPairValue.Value.Hash);
                                EditorGUILayout.LabelField("Size", keyPairValue.Value.Size.ToString());

                                int dependencySize = 0;
                                if (keyPairValue.Value.Dependencies != null)
                                {
                                    dependencySize = keyPairValue.Value.Dependencies.Length;
                                }

                                if (dependencySize > 0)
                                {
                                    foldout.DependenciesFoldout = EditorGUILayout.Foldout(foldout.DependenciesFoldout, "Dependencies");
                                    if (foldout.DependenciesFoldout)
                                    {
                                        EditorGUI.indentLevel = 3;
                                        EditorGUILayout.LabelField("Size", dependencySize.ToString());
                                        int i = 1;
                                        foreach (var dependency in keyPairValue.Value.Dependencies)
                                        {
                                            EditorGUILayout.LabelField(i.ToString(), dependency);
                                            i++;
                                        }
                                    }
                                }
                                
                                EditorGUI.indentLevel = 2;
                            }
                            EditorGUI.indentLevel = 1;
                        }

                        EditorGUI.indentLevel = indent;
                    }
                }
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }
    }
}