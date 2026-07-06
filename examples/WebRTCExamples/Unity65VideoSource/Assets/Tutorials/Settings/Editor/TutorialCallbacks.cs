using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using Unity.Tutorials.Core.Editor;
using UnityEngine.SceneManagement;
using System.Reflection;

/// <summary>
/// Implement your Tutorial callbacks here.
/// </summary>
[CreateAssetMenu(fileName = DefaultFileName, menuName = "Tutorials/" + DefaultFileName + " Instance")]
public class TutorialCallbacks : ScriptableObject
{
    /// <summary>
    /// The default file name used to create asset of this class type.
    /// </summary>
    public const string DefaultFileName = "TutorialCallbacks";

    /// <summary>
    /// Creates a TutorialCallbacks asset and shows it in the Project window.
    /// </summary>
    /// <param name="assetPath">
    /// A relative path to the project's root. If not provided, the Project window's currently active folder path is used.
    /// </param>
    /// <returns>The created asset</returns>
    public static ScriptableObject CreateAndShowAsset(string assetPath = null)
    {
        assetPath = assetPath ?? $"{TutorialEditorUtils.GetActiveFolderPath()}/{DefaultFileName}.asset";
        var asset = CreateInstance<TutorialCallbacks>();
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(assetPath));
        EditorUtility.FocusProjectWindow(); // needed in order to make the selection of newly created asset to really work
        Selection.activeObject = asset;
        return asset;
    }

    // ****************************
    // Added by VB with help from ClaudeAI
    // Used in early tutorials to select GameObject for the user.
    public void SelectGameObjectInScene(string gameObjectName)
    {
        // Find the GameObject in the current scene
        GameObject targetObject = GameObject.Find(gameObjectName);

        if (targetObject != null)
        {
            // Select the GameObject
            Selection.activeGameObject = targetObject;

            // Optionally, you can also focus the Scene view on this object
            if (SceneView.lastActiveSceneView != null)
            {
                //SceneView.lastActiveSceneView.FrameSelected();
            }
        }
        else
        {
            Debug.LogWarning($"GameObject not found: {gameObjectName}");
        }
    }
    // ****************************
    // Added by VB with help from ClaudeAI
    // Used in early tutorials to select asset in project for the user.
    public void SelectAssetinProject(string assetName)
    {
        string[] guids = AssetDatabase.FindAssets(assetName);
        if (guids.Length == 0)
        {
            Debug.LogWarning($"No asset found with name: {assetName}");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);

        if (asset != null)
        {
            Selection.activeObject = asset;
            EditorUtility.FocusProjectWindow();
            EditorGUIUtility.PingObject(asset);
        }
        else
        {
            Debug.LogError($"Failed to load asset at path: {path}");
        }
    }

    // ****************************
    // Added by VB with help from UAI
    public void SelectFolderInProject(string folderPath)
    {
        Object folderObject = AssetDatabase.LoadAssetAtPath<Object>(folderPath);

        if (folderObject != null)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = folderObject;
        }
        else
        {
            Debug.LogError("Folder not found: " + folderPath);
        }
    }

    // ****************************
    // Added by VB with help from Guillaume
    // Used in Welcome Dialog to start tutorials from buttons.
    public void StartTutorial(Tutorial tutorial)
    {
        TutorialWindowUtils.StartTutorial(tutorial);
    }

    // ****************************
    // Added by VB with help from ClaudeAI
    public void DisableGameObject(string objectName)
    {
        GameObject objectToDisable = GameObject.Find(objectName);

        if (objectToDisable != null)
        {
            objectToDisable.SetActive(false);
            Debug.Log($"GameObject '{objectName}' has been disabled.");
        }
        else
        {
            Debug.LogWarning($"GameObject '{objectName}' not found. Unable to disable.");
        }
    }
    // ****************************
    // Added by VB with help from ClaudeAI
    public void EnableGameObject(string objectName)
    {
        Debug.Log("EnableGameObject ran. Looking for: " + objectName);
        GameObject objectToEnable = GameObject.FindGameObjectWithTag(objectName);

        if (objectToEnable != null)
        {
            objectToEnable.SetActive(false);
            Debug.Log($"GameObject '{objectName}' has been enabled.");
        }
        else
        {
            Debug.LogWarning($"GameObject '{objectName}' not found. Unable to enable.");
        }
    }

public void EnableChildObjects(string objectTag)
{
    // Find the object with the specified tag
    GameObject objectToEnable = GameObject.FindGameObjectWithTag(objectTag);

    if (objectToEnable != null)
    {
        // Iterate through all the child objects and enable them
        foreach (Transform child in objectToEnable.transform)
        {
            child.gameObject.SetActive(true);
        }
    }

}

public void FrameObjectWithTag(string objectTag)
{
    // Find the object with the specified tag
    GameObject objectToFrame = GameObject.FindGameObjectWithTag(objectTag);

    if (objectToFrame != null)
    {
        Selection.activeGameObject = objectToFrame;

            // Get the active Scene view
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                // Focus on the selected object in the Scene view
                sceneView.FrameSelected();
                //sceneView.Repaint();
                Selection.activeGameObject = null;
            }
            else
            {
                Debug.Log("No active Scene view found.");
            }
    }
    else
    {
        Debug.LogWarning($"GameObject with tag '{objectTag}' not found. Unable to frame object.");
    }
}

    // Camera Enable/Disable functions using Transform to find child camera and child listener
    // Added by VB with help from UAI
    private GameObject mainCamera;
    private GameObject robotCamera;
    private GameObject robotListener;
    public void UseMainCamera(bool useMain)
    {
        // Find the cameras by their names within the scene if they haven't been found already
        if (mainCamera == null)
        {
            mainCamera = FindGameObjectByName("Main Camera");
        }

        if (robotCamera == null)
        {
            robotCamera = FindGameObjectByName("RobotCamera");
            robotListener = FindGameObjectByName("RobotListener");
        }

        if (mainCamera == null && useMain)
        {
            Debug.LogError("Main Camera not found, and can't be enabled.");
            return;
        }

        // Enable the target camera and disable the other one.
        // If there is no robotCamera or robotListener, use Main Camera (with its listener) so that there is always a camera
        // No error or warning if robotCamera or robotListener is null -- sometimes the robot isn't in the scene


    }



    // Functions to find a GameObject by name in the Hierarchy (even if a child)
    // Added by VB with help from UAI
    public static GameObject FindGameObjectByName(string name)
    {
        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (GameObject rootObject in rootObjects)
        {
            GameObject foundObject = FindGameObjectInChildren(rootObject.transform, name);
            if (foundObject != null)
            {
                return foundObject;
            }
        }
        return null; // if not found
    }
    // Recursive helper method to search through the children of a given Transform
    private static GameObject FindGameObjectInChildren(Transform parent, string name)
    {
        if (parent.name == name)
        {
            return parent.gameObject;
        }
        foreach (Transform child in parent)
        {
            GameObject foundObject = FindGameObjectInChildren(child, name);
            if (foundObject != null)
            {
                return foundObject;
            }
        }
        return null;
    }

    // ****************************
    // Added by VB with help from ClaudeAI
    // Used to set up the layout of the Project window to match the tutorial.
    // ClaudeAI warned about Internal API calls.
    // NOT CALLED -- Layout appears to handle this now
    public void CustomizeProjectWindowLayout()
    {
        try
        {
            // Get the ProjectBrowser type
            var projectBrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
            if (projectBrowserType == null)
            {
                Debug.LogError("Failed to find ProjectBrowser type");
                return;
            }

            // Find all ProjectBrowser windows
            var projectBrowsers = Resources.FindObjectsOfTypeAll(projectBrowserType);
            if (projectBrowsers.Length == 0)
            {
                Debug.LogWarning("No ProjectBrowser windows found");
                return;
            }

            foreach (EditorWindow projectBrowser in projectBrowsers)
            {
                if (projectBrowser == null)
                {
                    Debug.LogWarning("Found a null ProjectBrowser window, skipping");
                    continue;
                }

                // Set to two column layout
                SetProjectBrowserToTwoColumnMode();

                // Set the slider value to show titles
                ShowTextAndHideIcons();

                // Force the window to repaint
                projectBrowser.Repaint();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in CustomizeProjectWindowLayout: {e.Message}\nStack Trace: {e.StackTrace}");
        }
    }
    
    public void SetProjectBrowserToTwoColumnMode()
    {
        System.Type projectBrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
        var projectBrowsers = Resources.FindObjectsOfTypeAll(projectBrowserType);
        foreach (var projectBrowser in projectBrowsers)
        {
            MethodInfo setTwoColumnsMethod = projectBrowserType.GetMethod("SetTwoColumns",
                BindingFlags.Instance | BindingFlags.NonPublic);
            setTwoColumnsMethod?.Invoke(projectBrowser, null);
        }
    }

    public void ShowTextAndHideIcons()
    {
        System.Type projectBrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
        var projectBrowsers = Resources.FindObjectsOfTypeAll(projectBrowserType);
        foreach (var projectBrowser in projectBrowsers)
        {
            FieldInfo listAreaField = projectBrowserType.GetField("m_ListArea",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var listAreaState = listAreaField?.GetValue(projectBrowser);
            if (listAreaState != null)
            {
                System.Type listAreaType = listAreaState.GetType();

                var gridSizeProperty =
                    listAreaType.GetProperty("gridSize", BindingFlags.Instance | BindingFlags.Public);

                gridSizeProperty.SetValue(listAreaState, 16);
            }
        }
    }

    // Added by VB with help from UAI
    // Closes the AI Navigation window if it opens by default
    // NOT CALLED -- Layout appears to handle this now
    public void CloseNavWindowOnStart()
    {
        // Find the Navigation window
        var navigationWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.NavMeshEditorWindow");
        if (navigationWindowType != null)
        {
            var navigationWindow = EditorWindow.GetWindow(navigationWindowType, false, "Navigation");
            if (navigationWindow != null)
            {
                // Close the Navigation window
                navigationWindow.Close();
            }
        }
    }


    // Configure Main Camera to look at the Environment.
    // Necessary because the user may create a new Scena and save over SampleScene in T1, using the Main Camer'as default config instead of the one we want, as set in SampleScene.
    // 10/30/2024 AI-Tag
    // This was created with assistance from Muse, a Unity Artificial Intelligence product
    // Edited to use the mainCamera GameObject defined above for UseMainCamera()
    public void ConfigureMainCamera()
    {
        if (mainCamera == null)
        {
            mainCamera = FindGameObjectByName("Main Camera");
        }

        if (mainCamera != null)
        {
            // Set the Transform Position and Rotation
            mainCamera.transform.position = new Vector3(-18.0f, 26.0f, 0.0f);
            mainCamera.transform.rotation = Quaternion.Euler(56f, 90f, 0f);

            // Access the Camera component
            Camera mainCameraCam = mainCamera.GetComponent<Camera>();

            if (mainCamera != null)
            {
                // Basic Camera Settings
                mainCameraCam.clearFlags = CameraClearFlags.Skybox;
                mainCameraCam.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0.000f);
                mainCameraCam.orthographic = false;
                mainCameraCam.fieldOfView = 60f;
                mainCameraCam.nearClipPlane = 0.3f;
                mainCameraCam.farClipPlane = 1000f;
                mainCameraCam.depth = -1;
                mainCameraCam.allowHDR = true;
                mainCameraCam.allowMSAA = true;
                mainCameraCam.allowDynamicResolution = false;
                mainCameraCam.useOcclusionCulling = true;
                mainCameraCam.stereoConvergence = 10f;
                mainCameraCam.stereoSeparation = 0.022f;
            }
        }
        else
        {
            Debug.LogError("Main Camera GameObject not found!");
        }
    }
}
