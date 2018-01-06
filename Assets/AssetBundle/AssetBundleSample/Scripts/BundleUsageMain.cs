using System;
using System.Collections;
using AssetBundles;
using UnityEngine;
using FairyGUI;

/// <summary>
/// Demonstrated how to load UI package from assetbundle. The bundle can be build from the Window Menu->Build FairyGUI example bundles.
/// </summary>
class BundleUsageMain : MonoBehaviour
{
	GComponent _mainView;

	void OnEnable()
	{
	    if (_mainView != null)
	    {
	        _mainView.visible = true;
	        _mainView.GetTransition("t0").Play();
        }
	    else
	    {
	        Application.targetFrameRate = 60;
	        FairyUIPackage.AddPackage("UI/BundleUsage", OnAddPackageCallback);
        }
	}

    private void OnAddPackageCallback()
    {
        _mainView = UIPackage.CreateObject("BundleUsage", "Main").asCom;
        _mainView.fairyBatching = true;
        _mainView.SetSize(GRoot.inst.width, GRoot.inst.height);
        _mainView.AddRelation(GRoot.inst, RelationType.Size);

        GRoot.inst.AddChild(_mainView);
        _mainView.GetTransition("t0").Play();
    }

    void OnDisable()
    {
        if (_mainView != null)
        {
            _mainView.visible = false;
        }
    }
}
