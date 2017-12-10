using AssetBundles;
using UnityEditor;
using UnityEngine;

namespace AssetBundleBrowser
{
    public class InspectVersion
    {
        internal InspectVersion() { }

        private AssetBundleVersionInfo mVersion;

        private Rect mPosition;

        [SerializeField]
        private Vector2 mScrollPosition;

        internal void SetVersion(AssetBundleVersionInfo version)
        {
            //members
            mVersion = version;
        }

        internal void OnGUI(Rect pos)
        {
            mPosition = pos;

            DrawBundleData();
        }

        private void DrawBundleData()
        {
            if (mVersion != null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    GUILayout.BeginArea(mPosition);
                    mScrollPosition = EditorGUILayout.BeginScrollView(mScrollPosition);
                    EditorGUILayout.LabelField("MarjorVersion", mVersion.MarjorVersion.ToString());
                    EditorGUILayout.LabelField("MinorVersion", mVersion.MinorVersion.ToString());
                    EditorGUILayout.EndScrollView();
                    GUILayout.EndArea();
                }
            }
        }
    }
}