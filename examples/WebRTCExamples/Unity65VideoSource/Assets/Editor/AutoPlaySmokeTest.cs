// Temporary automation hook: opens the game scene and enters play mode so the WebRTC
// signaling listener can be smoke-tested from outside the editor. Invoked with
// Unity.exe -executeMethod AutoPlaySmokeTest.Run. Safe to delete.
using UnityEditor;
using UnityEditor.SceneManagement;

public static class AutoPlaySmokeTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GetStarted_Scene.unity");
        EditorApplication.EnterPlaymode();
    }
}
