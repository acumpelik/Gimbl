#if UNITY_EDITOR
﻿using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using Gimbl;
using PathCreation;

public class ActorWindow : EditorWindow
{
    #region Menu Variables.
    Vector2 scrollPosition = Vector2.zero;
    public delegate void CreateFunc<T>(MenuSettings<T> settings) where T : UnityEngine.Object;



    // Stores menu settings.
    public class MenuSettings<T> where T : UnityEngine.Object
    {
        public string typeName;
        public bool[] show = { false, false, false, false, false };
        public string name = "";
        public int selectedInstanceId;
        public Rect editRect = new Rect(); // stores location editing window.
        private T _selectedObj;
        public T selectedObj
        {
            get {return _selectedObj;}
            set
            {
                if (!UnityEngine.Object.ReferenceEquals(value,_selectedObj))
                {
                    _selectedObj = value;
                    if (value!=null) { selectedInstanceId = value.GetInstanceID(); }
                    else { selectedInstanceId = 0; }
                }
            }
        }
    }
    // Need to make non-generic inherited class for having serializble variables (otherwise menu changes on run)
    [System.Serializable] public class ActorMenuSettings : MenuSettings<ActorObject> { }
    [System.Serializable] public class ControllerMenuSettings : MenuSettings<ControllerOutput> { }
    [System.Serializable] public class PathMenuSettings : MenuSettings<PathCreator> { }
    [SerializeField] private ActorMenuSettings actSettings = new ActorMenuSettings() { typeName = "Actor" };
    [SerializeField] private ControllerMenuSettings contSettings = new ControllerMenuSettings() { typeName = "Controller" };
    [SerializeField] private PathMenuSettings pathSettings = new PathMenuSettings(){ typeName = "Path" };

    // Actor specific variables.
    private string[] actorModels;
    private int selectedModel = 0;
    private bool trackCam = true;

    // Controller specific Variables.
    private ControllerTypes contType = ControllerTypes.LinearTreadmill;

    #endregion

    #region Window Setup.
    private static EditorWindow currentWindow;
    public static void ShowWindow()
    {
        currentWindow = GetWindow<ActorWindow>("Actors",true, typeof(MainWindow));
        currentWindow.Show();
    }
    private void OnEnable()
    {
        // Get actor models.
        Resources.LoadAll<GameObject>("Actors/Mouse");
        UnityEngine.Object[] data = Resources.LoadAll<GameObject>("Actors/Prefabs");
        actorModels = data.Select(x => x.name).ToArray();
        actorModels = actorModels.Union(new string[] { "None" }).ToArray();

    }
    #endregion
    private void OnGUI()
    {


        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition,GUILayout.Height(position.height), GUILayout.Width(position.width));
        #region ActorMenu
        EditorGUILayout.BeginVertical(LayoutSettings.mainBox.style);
        EditorGUILayout.LabelField("Actors", LayoutSettings.sectionLabel);

        // Select and delete actor.
        EditorGUILayout.BeginHorizontal(LayoutSettings.editWidth);
            SelectMenu(actSettings);
            if (GUILayout.Button("Delete", LayoutSettings.buttonOp)) actSettings.selectedObj.DeleteActor();
        EditorGUILayout.EndHorizontal();


        // Edit Actor.
        if (actSettings.selectedObj != null)
        {
            actSettings.selectedObj.EditMenu();
        }

        // Create Actor.
        if (EditorApplication.isPlaying) GUI.enabled = false;
        actSettings.show[0] = EditorGUILayout.Foldout(actSettings.show[0], "Create");
        if (actSettings.show[0])
        {
            EditorGUILayout.BeginVertical(LayoutSettings.subBox.style);
            EditorGUILayout.LabelField("Create Actor", EditorStyles.boldLabel);
            actSettings.name = EditorGUILayout.TextField("Actor Name: ", actSettings.name, LayoutSettings.editFieldOp);
            selectedModel = EditorGUILayout.Popup("Model: ", selectedModel, actorModels, LayoutSettings.editFieldOp);
            trackCam = EditorGUILayout.Toggle("Add Tracking Cam: ",trackCam);
            CreateButton(actSettings);
            EditorGUILayout.EndVertical();
        }
        GUI.enabled = true;
        EditorGUILayout.EndVertical();
        #endregion

        #region Controller
        EditorGUILayout.BeginVertical(LayoutSettings.mainBox.style);
        EditorGUILayout.LabelField("Controllers", LayoutSettings.sectionLabel);

        // Select and delete.
        EditorGUILayout.BeginHorizontal(LayoutSettings.editWidth);
            SelectMenu(contSettings);
            if (GUILayout.Button("Delete", LayoutSettings.buttonOp)) contSettings.selectedObj.master.DeleteController();
        EditorGUILayout.EndHorizontal();

        // Edit.
        contSettings.show[0] = EditorGUILayout.Foldout(contSettings.show[0], "Edit");
        if (contSettings.show[0])
        {
            if (contSettings.selectedObj != null)
            {
                EditorGUILayout.BeginVertical(LayoutSettings.subBox.style);
                // Custom edit menu.
                contSettings.selectedObj.master.EditMenu();
                // Save and load options.
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal(LayoutSettings.editFieldOp); GUILayout.FlexibleSpace();
                if (GUILayout.Button("Save Controller Settings", GUILayout.Width(250))) contSettings.selectedObj.master.SaveController();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                if (EditorApplication.isPlaying) GUI.enabled = false;
                EditorGUILayout.BeginHorizontal(LayoutSettings.editFieldOp); GUILayout.FlexibleSpace();
                if (GUILayout.Button("Load Controller Settings", GUILayout.Width(250))) contSettings.selectedObj.master.LoadController();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUI.enabled = true;
                EditorGUILayout.EndVertical();

                
            }
        }
        // Create.
        if (EditorApplication.isPlaying) GUI.enabled = false;
        contSettings.show[1] = EditorGUILayout.Foldout(contSettings.show[1], "Create");
        if (contSettings.show[1])
        {
            EditorGUILayout.BeginVertical(LayoutSettings.subBox.style);
            EditorGUILayout.LabelField("Create Controller", EditorStyles.boldLabel);
            contSettings.name = EditorGUILayout.TextField("Controller Name: ", contSettings.name, LayoutSettings.editFieldOp);
            contType = (ControllerTypes)EditorGUILayout.EnumPopup("Type: ",contType, LayoutSettings.editFieldOp);
            CreateButton(contSettings);
            EditorGUILayout.EndVertical();
        }
        GUI.enabled = true;
        EditorGUILayout.EndVertical();
        #endregion

        #region Paths
        EditorGUILayout.BeginVertical(LayoutSettings.mainBox.style);
        EditorGUILayout.LabelField("Paths", LayoutSettings.sectionLabel);

        // Select and delete.
        EditorGUILayout.BeginHorizontal(LayoutSettings.editWidth);
            SelectMenu(pathSettings);
            if (GUILayout.Button("Delete", LayoutSettings.buttonOp)) DeletePath();
        EditorGUILayout.EndHorizontal();

        // Length readouts for the selected path.
        if (pathSettings.selectedObj != null) ShowPathLengths(pathSettings.selectedObj);

        // Create.
        if (EditorApplication.isPlaying) GUI.enabled = false;
        pathSettings.show[0] = EditorGUILayout.Foldout(pathSettings.show[0], "Create");
        if (pathSettings.show[0])
        {
            EditorGUILayout.BeginVertical(LayoutSettings.subBox.style);
            EditorGUILayout.LabelField("Create Path", EditorStyles.boldLabel);
            pathSettings.name = EditorGUILayout.TextField("Path Name: ", pathSettings.name, LayoutSettings.editFieldOp);
            CreateButton(pathSettings);
            EditorGUILayout.EndVertical();
        }
        GUI.enabled = true;
        EditorGUILayout.EndVertical();
        #endregion
        GUILayout.EndScrollView();
    }

    // Menu Functions.
    private void SelectMenu<T>(MenuSettings<T> settings) where T : UnityEngine.Object
    {
        // Object cant be found (possible serialization on run)
        if (settings.selectedObj == null)
        {
            T obj = null;
            // Check if instance ID is valid
            if (settings.selectedInstanceId != 0)
            {
                try { obj = (T)EditorUtility.InstanceIDToObject(settings.selectedInstanceId); }
                catch (System.InvalidCastException) { obj = null; } // catches changed instanceID on restart.
            }
            // Otherwise find first on list.
            if (obj==null){ obj = FindFirstObjectByType<T>();}
                if (obj != null) { settings.selectedObj = obj; }
        }
        settings.selectedObj = (T)EditorGUILayout.ObjectField(settings.selectedObj, typeof(T), true);
    }

    private void CreateButton<T>(MenuSettings<T> settings) where T:UnityEngine.Object
    {
        EditorGUILayout.BeginHorizontal();
            T[] objs = FindObjectsByType<T>(FindObjectsSortMode.None);
            string[] names = objs.Select(x => x.name).ToArray();
            string msg = "";
            if (ArrayUtility.Contains(names, settings.name)) { msg = "Duplicate name"; GUI.enabled = false; }
            if (settings.name == "") { msg = "Empty Name"; GUI.enabled = false; }
            EditorGUILayout.LabelField(msg, GUILayout.Width(197));
            if (GUILayout.Button("Create", LayoutSettings.buttonOp))
            {
                GameObject obj = new GameObject(settings.name);
                //Controller.
                if (typeof(T)==typeof(ControllerOutput))
                {
                    //Create Controller. 
                    ControllerObject cont = (ControllerObject)obj.AddComponent(System.Type.GetType(string.Format("Gimbl.{0}", contType.ToString())));
                    cont.InitiateController();
                    //Create general Output Object and link.
                    ControllerOutput contOut = obj.AddComponent<ControllerOutput>();
                    contOut.master = cont;
                    // Select created.
                    settings.selectedObj = contOut as T;
                }
                //Actor.
                if (typeof(T) == typeof(ActorObject))
                {
                    ActorObject act = obj.AddComponent<ActorObject>();
                    act.InitiateActor(actorModels[selectedModel], trackCam);
                    settings.selectedObj = act as T;
                }
                //Path.
                if (typeof(T) == typeof(PathCreator))
                {
                    CreatePath(settings as MenuSettings<PathCreator>, obj);
                }
                settings.name = "";
        }
        EditorGUILayout.EndHorizontal();
    }

    // Path Manipulation Functions
    private void DeletePath()
    {
        GameObject obj = pathSettings.selectedObj.gameObject;
        bool accept = EditorUtility.DisplayDialog(string.Format("Remove Path {0}?", obj.name),
            string.Format("Are you sure you want to delete Path {0}?", obj.name), "Delete", "Cancel");
        if (accept)
        {
            // Not deleting scriptable object asset so delete it can be undone.
            Undo.DestroyObjectImmediate(obj);
        }
    }

    // Length Readout Functions.
    // Shows (1) the Bezier TunnelPath length via PathCreator's VertexPath, and
    // (2) the summed world-space Z extent of the PathSegment meshes that belong to the
    // same imported context as this path. Useful as a calibration cross-check between the
    // authored corridor geometry and the path the treadmill actually rides.
    private void ShowPathLengths(PathCreator path)
    {
        EditorGUILayout.BeginVertical(LayoutSettings.subBox.style);
        EditorGUILayout.LabelField("Length", EditorStyles.boldLabel);

        // (1) Bezier / VertexPath length (Unity units).
        float bezierLen = 0f;
        try { if (path.path != null) bezierLen = path.path.length; }
        catch (System.Exception) { /* path not yet built */ }
        EditorGUILayout.LabelField("Bezier path length:", string.Format("{0:F3} u", bezierLen));

        // (2) PathSegment coverage along Z (Unity units).
        int segCount;
        bool scopedToContext;
        float overlap;
        float segZCoverage = SumPathSegmentZ(path.transform, out segCount, out scopedToContext, out overlap);
        string scopeNote = segCount == 0 ? "none found"
                                         : (scopedToContext ? "this context" : "whole scene");
        EditorGUILayout.LabelField(string.Format("PathSegment Z coverage ({0}, {1}):", segCount, scopeNote),
                                   string.Format("{0:F3} u", segZCoverage));
        // Only surface overlap when it is non-trivial (segments stacking in Z).
        if (overlap > 0.001f)
            EditorGUILayout.LabelField("  ↳ overlap removed:", string.Format("{0:F3} u", overlap));

        EditorGUILayout.EndVertical();
    }

    // Measures how much of the Z axis the PathSegment renderers actually cover, as the
    // UNION of their world-space [min.z, max.z] intervals -- so segments that overlap in Z
    // are counted once, not twice (a naive per-segment sum would double-count the overlap).
    // Reports the removed overlap (naive sum - union) via <paramref name="overlap"/>.
    // PathSegments are tagged by the .vr importer with a VRObjectTag component
    // (type == "PathSegment"); that type lives in the consuming project's assembly, not this
    // package, so it is matched by reflection rather than a direct reference. Scoped to the
    // selected path's parent (the "VRContext_<id>" root) when possible, else the whole scene.
    private static float SumPathSegmentZ(Transform pathTransform, out int count,
                                         out bool scopedToContext, out float overlap)
    {
        List<GameObject> segs = FindPathSegments(pathTransform != null ? pathTransform.parent : null);
        scopedToContext = segs.Count > 0;
        if (segs.Count == 0) segs = FindPathSegments(null); // widen to whole scene.

        // Collect each segment's Z-interval from its world-space renderer bounds.
        var intervals = new List<Vector2>(); // (min.z, max.z)
        float naiveSum = 0f;
        count = 0;
        foreach (GameObject go in segs)
        {
            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r == null) continue;
            Bounds b = r.bounds;
            intervals.Add(new Vector2(b.min.z, b.max.z));
            naiveSum += b.size.z;
            count++;
        }

        // Union of intervals: sort by start, merge overlapping/touching, sum merged lengths.
        intervals.Sort((a, b) => a.x.CompareTo(b.x));
        float union = 0f;
        float curStart = 0f, curEnd = 0f;
        bool open = false;
        foreach (Vector2 iv in intervals)
        {
            if (!open) { curStart = iv.x; curEnd = iv.y; open = true; continue; }
            if (iv.x <= curEnd) { if (iv.y > curEnd) curEnd = iv.y; } // overlaps/touches -> extend.
            else { union += curEnd - curStart; curStart = iv.x; curEnd = iv.y; } // gap -> close run.
        }
        if (open) union += curEnd - curStart;

        overlap = naiveSum - union;
        return union;
    }

    private static List<GameObject> FindPathSegments(Transform scope)
    {
        var result = new List<GameObject>();
        MonoBehaviour[] behaviours = scope != null
            ? scope.GetComponentsInChildren<MonoBehaviour>(true)
            : FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (MonoBehaviour mb in behaviours)
        {
            if (mb == null) continue;
            System.Type t = mb.GetType();
            if (t.Name != "VRObjectTag") continue;
            System.Reflection.FieldInfo field = t.GetField("type");
            if (field == null) continue;
            if ((field.GetValue(mb) as string) == "PathSegment") result.Add(mb.gameObject);
        }
        return result;
    }

    private void CreatePath(MenuSettings<PathCreator> settings,GameObject obj)
    {
        obj.transform.SetParent(GameObject.Find("Paths").transform);
        PathCreator path = obj.AddComponent<PathCreator>();
        path.bezierPath.Space = PathSpace.xz;
        path.bezierPath.ControlPointMode = BezierPath.ControlMode.Automatic;
        Undo.RegisterCreatedObjectUndo(obj, "Create Path");
        settings.selectedObj = path;
    }

}
#endif // UNITY_EDITOR
