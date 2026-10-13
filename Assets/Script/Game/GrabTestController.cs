using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 「Display 2 で物を掴む → Tab で画面切替 → Display 1/3 でも掴んだままか」をテストする。
/// DisplayTestSwitcher と併用する。
/// </summary>
public class GrabTestController : MonoBehaviour
{
    [Header("参照")]
    public DisplayTestSwitcher switcher;

    [Header("掴む設定")]
    [Tooltip("掴み始められる画面(0始まり。1 = Display 2)")]
    public int grabScreenIndex = 1;
    [Tooltip("ON: クリックで掴む/もう一度クリックで離す。OFF: ボタンを押している間だけ掴む")]
    public bool toggleGrab = true;
    [Tooltip("ON: 他の画面でもマウスに追従して動かす。OFF: 他の画面では掴んだまま静止")]
    public bool followMouseOnAllScreens = false;

    [Header("テスト用シーン自動構築")]
    [Tooltip("掴めるオブジェクト。空なら自動生成する")]
    public GameObject[] grabbableObjects;
    public bool arrangeCameras = true;
    public bool setBackgroundColors = true;

    [Header("見た目")]
    public Color heldColor = Color.yellow;
    public bool showOverlayLabel = true;

    class GrabInfo { public Renderer renderer; public Color originalColor; }
    readonly Dictionary<Collider, GrabInfo> grabbables = new Dictionary<Collider, GrabInfo>();

    GameObject held;
    GrabInfo heldInfo;
    int heldFromScreen = -1;
    Plane dragPlane;
    Vector3 grabOffset;
    int lastScreen = -1;

    void Start()
    {
        if (switcher == null) switcher = FindFirstObjectByType<DisplayTestSwitcher>();

        if (grabbableObjects == null || grabbableObjects.Length == 0)
            CreateTestObjects();

        foreach (var go in grabbableObjects)
            RegisterGrabbable(go);

        if (arrangeCameras) ArrangeCameras();
    }

    // ---------- テスト用オブジェクト/カメラ ----------

    void CreateTestObjects()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "TestGround";
        ground.transform.localScale = new Vector3(1.5f, 1, 1.5f);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Grab_Cube";
        cube.transform.position = new Vector3(-2f, 0.5f, 0f);

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Grab_Sphere";
        sphere.transform.position = new Vector3(0f, 0.5f, 0f);

        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "Grab_Capsule";
        capsule.transform.position = new Vector3(2f, 1f, 0f);

        grabbableObjects = new[] { cube, sphere, capsule };
    }

    void RegisterGrabbable(GameObject go)
    {
        if (go == null) return;
        var r = go.GetComponentInChildren<Renderer>();
        foreach (var c in go.GetComponentsInChildren<Collider>())
            grabbables[c] = new GrabInfo { renderer = r, originalColor = r != null ? r.material.color : Color.white };
    }

    void ArrangeCameras()
    {
        if (switcher == null) return;
        Vector3 center = new Vector3(0f, 0.5f, 0f);
        Color[] bg = {
            new Color(0.45f, 0.12f, 0.12f),   // 画面1: 赤
            new Color(0.12f, 0.40f, 0.15f),   // 画面2: 緑
            new Color(0.12f, 0.20f, 0.50f)    // 画面3: 青
        };

        for (int i = 0; i < switcher.screens.Length; i++)
        {
            var cam = switcher.screens[i].camera;
            if (cam == null) continue;

            float angle = (i - 1) * 60f;   // 画面2が正面、画面1/3が左右60度
            cam.transform.position = center + Quaternion.Euler(0, angle, 0) * new Vector3(0, 0, -7f) + Vector3.up * 3.5f;
            cam.transform.LookAt(center);

            if (setBackgroundColors && i < bg.Length)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = bg[i];
            }
        }
    }

    // ---------- メインループ ----------

    void Update()
    {
        if (switcher == null) return;

        int cur = switcher.Current;
        if (cur != lastScreen)
        {
            if (lastScreen >= 0)
                Debug.Log($"[GrabTest] 画面切替 {lastScreen + 1} → {cur + 1} / 掴み状態: {(held != null ? held.name + " を保持中" : "なし")}");
            lastScreen = cur;
        }

        Camera cam = switcher.screens[cur].camera;
        if (cam == null) return;

        if (MousePressed())
        {
            if (held == null)
            {
                if (cur == grabScreenIndex) TryGrab(cam);
            }
            else if (toggleGrab)
            {
                Release();
            }
        }
        else if (!toggleGrab && held != null && MouseReleased())
        {
            Release();
        }

        if (held != null) FollowMouse(cam, cur);
    }

    void TryGrab(Camera cam)
    {
        Ray ray = cam.ScreenPointToRay(MousePos());
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;
        if (!grabbables.TryGetValue(hit.collider, out var info)) return;

        held = info.renderer != null ? info.renderer.gameObject : hit.collider.gameObject;
        heldInfo = info;
        heldFromScreen = switcher.Current;

        // 水平面上でドラッグする(どのカメラからでも破綻しにくい)
        dragPlane = new Plane(Vector3.up, held.transform.position);
        grabOffset = dragPlane.Raycast(ray, out float d)
            ? held.transform.position - ray.GetPoint(d)
            : Vector3.zero;

        if (info.renderer != null) info.renderer.material.color = heldColor;
        Debug.Log($"[GrabTest] 画面{heldFromScreen + 1}で {held.name} を掴んだ");
    }

    void FollowMouse(Camera cam, int cur)
    {
        if (!followMouseOnAllScreens && cur != heldFromScreen) return;

        Ray ray = cam.ScreenPointToRay(MousePos());
        if (dragPlane.Raycast(ray, out float d))
            held.transform.position = ray.GetPoint(d) + grabOffset;
    }

    void Release()
    {
        Debug.Log($"[GrabTest] 画面{switcher.Current + 1}で {held.name} を離した");
        if (heldInfo != null && heldInfo.renderer != null)
            heldInfo.renderer.material.color = heldInfo.originalColor;
        held = null;
        heldInfo = null;
        heldFromScreen = -1;
    }

    // ---------- 入力(新旧Input System両対応) ----------

    Vector2 MousePos()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    bool MousePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    bool MouseReleased()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(0);
#endif
    }

    // ---------- 画面上の表示 ----------

    void OnGUI()
    {
        if (!showOverlayLabel || switcher == null) return;

        string state = held != null
            ? $"掴み中: {held.name} (掴んだ画面: {heldFromScreen + 1})"
            : $"掴み: なし (画面{grabScreenIndex + 1}でオブジェクトをクリック)";
        GUI.Label(new Rect(10, 34, 700, 24), $"現在: 画面{switcher.Current + 1} / {state}");
    }
}