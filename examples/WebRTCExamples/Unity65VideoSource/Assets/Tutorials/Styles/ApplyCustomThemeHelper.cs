using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using Unity.Tutorials.Core.Editor;
#endif

[CreateAssetMenu(fileName = "ApplyCustomThemeHelper", menuName = "Scriptable Objects/ApplyCustomThemeHelper")]
public class ApplyCustomThemeHelper : ScriptableObject
{
#if UNITY_EDITOR
    public void ApplyCustomTheme()
    {
        EditorCoroutineUtility.StartCoroutineOwnerless(WaitToApply());
    }

    IEnumerator WaitToApply()
    {
        yield return null;

        TutorialWindow.Instance.rootVisualElement.styleSheets.Add(EditorGUIUtility.isProSkin ?
            TutorialProjectSettings.Instance.TutorialStyle.DarkThemeStyleSheet :
            TutorialProjectSettings.Instance.TutorialStyle.LightThemeStyleSheet);
    }
#endif
}
