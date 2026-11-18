using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

// カーソルを3Dモデル(手など)にする(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「カーソル」)
// p1CursorModel / p2CursorModel にプレハブを入れると、画像のカーソルの代わりに出る。
//  - 大きさ: 縦横の長いほうが cursorScale x cursorModelSize(画像のカーソルと同じくらい)
//  - 掴む点(カーソルの位置)は、モデルの真ん中 + cursorModelOffset(カーソルの大きさを1として)
//  - 描く順番は画像のカーソルと同じ CursorOrder(2900。商品・画面の枠(2800)より手前、大きな文字・結果発表(3000〜)より奥)。3Dモデルの商品と同じく、描く順のグループを透明にして sortingOrder で並べる
//  - 手は全部 z=0 より手前に置く(3Dモデルの商品・棚は z=0 より奥。手がめり込んで後ろに隠れないように)
//  - 当たり判定は付けない(掴む判定の邪魔になるため)
//  - どちらの手か分かるように: 手のモデルにプレイヤーの色をうっすら付け(cursorModelTintAmount)、手の縁を枠と同じプレイヤーの色で縁取り(cursorOutlineWidth)、
//    棚の画面の手のそばに「1P」「2P」を出す(cursorShowPlayerLabel)
//  - 手のモデル専用のライト(handLight): 手だけを照らすライトを1つ足す。ライトレイヤー(URP の Rendering Layers。UniversalRP.asset で ON)を使い、
//    手のモデルの renderingLayerMask に HandLightLayerBit を足して、そのライトはそのビットの物だけを照らす(商品・棚は照らさない)
//  - モーション: 商品を掴んだら cursorGrabClip(つかむ)、離したら cursorReleaseClip(はなす)を、そのプレイヤーの手(棚・自分の画面の両方)で再生する。
//    最後のコマで止まる(クリップの Loop Time は OFF のまま)。クリップは手のモデル(FBX)の ▶ を開いた中にある。
//    cursorMotionAnimate が OFF なら、再生せずに「つかむ」クリップの1コマで止めた形にする(掴んだ形 / 離した形。最初は離した形)
public partial class TwoPlayerShop2D
{
    const int CursorOrder = 2900;   // カーソル(画像・3Dモデル)の描く順番

    GameObject MakeModelCursor(string objName, int layer, GameObject prefab, bool mirror)
    {
        var go = new GameObject(objName);
        var sr = go.AddComponent<SpriteRenderer>();   // 絵は無し(ほかの処理と同じ形にしておく)
        sr.sortingOrder = CursorOrder;

        // 左右反転は入れ物(Mirror)で、画面の横向きにかける。
        // モデル自身の大きさでかけると、プレハブを回してあるとき(例: 手のモデル)に、横ではなく奥行きが反転してしまう
        var holder = new GameObject("Mirror").transform;
        holder.SetParent(go.transform, false);
        var model = Instantiate(prefab, holder);
        model.name = "Model";
        Quaternion prefabRotation = prefab.transform.localRotation;
        Vector3 prefabScale = prefab.transform.localScale;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(cursorModelRotation) * prefabRotation;
        model.transform.localScale = prefabScale;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[Shop2D] カーソルの3Dモデル {prefab.name} に見た目(Renderer)がありません");
        }
        else
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float length = cursorScale * Mathf.Max(0.01f, cursorModelSize);   // 縦横の長いほうの長さ
            float k = length / Mathf.Max(b.size.x, b.size.y, 0.01f);
            model.transform.localScale = prefabScale * k;
            // 真ん中を掴む点に合わせて、cursorModelOffset だけずらす(反転したら、入れ物ごと左右も反転する)
            Vector3 center = b.center * k;
            Vector2 offset = cursorModelOffset * length;
            // 奥の面を z=0 より少し手前にする(商品・棚の3Dモデルは z=0 より奥にあるので、手がめり込んで隠れない)
            float backZ = (b.center.z + b.extents.z) * k;
            model.transform.localPosition = new Vector3(offset.x - center.x, offset.y - center.y, -backZ - 0.01f);

            UseSpriteOrder(renderers);   // 絵と同じ sortingOrder の順で描く(TwoPlayerShop2D.Products.cs)
            foreach (var r in renderers)
            {
                r.sortingOrder = CursorOrder;
                r.renderingLayerMask |= 1u << HandLightLayerBit;   // 手のモデル専用のライトに照らされるようにする
            }
        }
        if (mirror) holder.localScale = new Vector3(-1f, 1f, 1f);

        SetLayer(go, layer);
        return go;
    }

    // 手の縁取り: 手のモデルをもう1つ、少し大きくして後ろに重ね、プレイヤーの色1色で塗る(はみ出したふちだけが色付きで見える)。
    // 色は枠と同じプレイヤーの色。太さ cursorOutlineWidth(0 なら付けない)。塗るのは文字と同じ描き方(形だけを1色で塗る)
    void AddCursorOutline(Transform cursor, GameObject prefab, Color color)
    {
        if (cursor == null || prefab == null || cursorOutlineWidth <= 0f) return;
        var holder = cursor.Find("Mirror");
        var model = holder != null ? holder.Find("Model") : null;
        if (model == null) return;
        var modelRenderers = model.GetComponentsInChildren<Renderer>();
        if (modelRenderers.Length == 0) return;
        Bounds b = modelRenderers[0].bounds;
        foreach (var r in modelRenderers) b.Encapsulate(r.bounds);
        Vector3 center = holder.InverseTransformPoint(b.center);   // 手の真ん中(入れ物の中で)

        var outline = Instantiate(prefab, holder);
        outline.name = "Outline";
        float s = 1f + cursorOutlineWidth;
        outline.transform.localRotation = model.localRotation;
        outline.transform.localScale = model.localScale * s;
        outline.transform.localPosition = center + (model.localPosition - center) * s + new Vector3(0f, 0f, 0.02f);   // 手の真ん中を中心に大きく・少し奥

        if (labelFont == null) labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var mat = new Material(labelFont.material.shader) { name = "CursorOutline", color = new Color(color.r, color.g, color.b, 1f) };
        foreach (var r in outline.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.sortingOrder = CursorOrder - 1;   // 手より先に描く(手が上に重なる)
        }
        SetLayer(outline, cursor.gameObject.layer);
    }

    // カーソルの手のモーションを再生している仕組み(手ごとに1つ。新しいモーションを始めたら前のは片付ける)
    readonly Dictionary<Transform, PlayableGraph> cursorGraphs = new Dictionary<Transform, PlayableGraph>();

    // 掴んだ(grab = true)/ 離した(false)ときに、そのプレイヤーの手のモーションを再生する
    // cursorMotionAnimate が OFF のときは、モーションは再生せず、「つかむ」クリップの1コマで止めた形にする
    // (掴んだら cursorGrabPoseTime のコマ・離したら cursorReleasePoseTime のコマ。0 = 始め 〜 1 = 終わり)
    void PlayCursorMotion(Player p, bool grab)
    {
        if (p == null) return;
        if (!cursorMotionAnimate)
        {
            if (cursorGrabClip == null) return;
            float t = grab ? cursorGrabPoseTime : cursorReleasePoseTime;
            PlayCursorClip(p.cursorShop, cursorGrabClip, t);
            PlayCursorClip(p.cursorOwn, cursorGrabClip, t);
            return;
        }
        var clip = grab ? cursorGrabClip : cursorReleaseClip;
        if (clip == null) return;
        PlayCursorClip(p.cursorShop, clip);
        PlayCursorClip(p.cursorOwn, clip);
    }

    // pose が 0〜1 なら、その位置のコマで止める(-1 ならモーションを再生する)
    void PlayCursorClip(Transform cursor, AnimationClip clip, float pose = -1f)
    {
        if (cursor == null) return;
        // 3Dモデルの手(MakeModelCursor)と、その縁取り(AddCursorOutline)の両方を同じ形にする。画像のカーソルなら何もしない
        foreach (var path in new[] { "Mirror/Model", "Mirror/Outline" })
        {
            var t = cursor.Find(path);
            if (t != null) PlayModelClip(t, clip, pose);
        }
    }

    void PlayModelClip(Transform model, AnimationClip clip, float pose)
    {
        if (cursorGraphs.TryGetValue(model, out var old) && old.IsValid()) old.Destroy();
        var animator = model.GetComponentInChildren<Animator>();
        if (animator == null) animator = model.gameObject.AddComponent<Animator>();
        var playable = AnimationPlayableUtilities.PlayClip(animator, clip, out var graph);
        if (pose >= 0f)
        {
            playable.SetTime(clip.length * Mathf.Clamp01(pose));   // そのコマで止める
            playable.SetSpeed(0f);
        }
        else playable.SetSpeed(Mathf.Max(0.01f, cursorMotionSpeed));   // モーションの速さ(2 なら2倍速)
        cursorGraphs[model] = graph;
    }

    // OnDestroy から: モーションの再生の仕組みを片付ける
    void DestroyCursorGraphs()
    {
        foreach (var g in cursorGraphs.Values) if (g.IsValid()) g.Destroy();
        cursorGraphs.Clear();
    }

    // 手のモデルに、プレイヤーの色をうっすら付ける(マテリアルの色 _BaseColor / _Color に混ぜる。マテリアルはこの手だけのもの)
    void TintCursorModel(Transform cursor, Color color)
    {
        if (cursor == null || cursorModelTintAmount <= 0f) return;
        foreach (var r in cursor.GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials)
            {
                foreach (var prop in new[] { "_BaseColor", "_Color" })
                    if (m.HasProperty(prop))
                    {
                        Color c = m.GetColor(prop);
                        m.SetColor(prop, new Color(Mathf.Lerp(c.r, color.r, cursorModelTintAmount), Mathf.Lerp(c.g, color.g, cursorModelTintAmount),
                                                   Mathf.Lerp(c.b, color.b, cursorModelTintAmount), c.a));
                    }
            }
    }

    // 棚の画面のカーソルのそばに「1P」「2P」(プレイヤーの色・影付き)。カーソルの子なので一緒に動く(左右反転はしない)
    readonly Dictionary<Player, TextMesh> cursorLabels = new Dictionary<Player, TextMesh>();

    void AddCursorLabel(Player p, Color color)
    {
        if (!cursorShowPlayerLabel || p.cursorShop == null) return;
        var label = MakeText($"CursorLabel_{p.name}", p.cursorShop, layerShop, p.name, new Color(color.r, color.g, color.b, 1f), CursorOrder + 1);
        AddTextShadow(label);
        cursorLabels[p] = label;
        UpdateCursorLabel(p);
    }

    // 「1P」「2P」の位置と大きさ(LateUpdate から毎フレーム。再生中に変えてもすぐ反映)
    void UpdateCursorLabel(Player p)
    {
        if (p == null || !cursorLabels.TryGetValue(p, out var label) || label == null) return;
        label.transform.localPosition = new Vector3(cursorLabelOffset.x * cursorScale, cursorLabelOffset.y * cursorScale, 0f);
        SetTextSize(label, HudTextSize * 0.6f * Mathf.Max(0.1f, cursorLabelSize));
    }

    // ---------- 手のモデル専用のライト ----------
    const int HandLightLayerBit = 1;   // ライトレイヤーの番号(Light Layer 1)。手のモデルだけがこのビットを持つ
    Light handLight;

    // LateUpdate から毎フレーム(再生中に向き・明るさ・色を変えてもすぐ反映)
    void UpdateHandLight()
    {
        if (!handLightEnabled)
        {
            if (handLight != null) handLight.enabled = false;
            return;
        }
        if (handLight == null)
        {
            var go = new GameObject("HandLight");
            handLight = go.AddComponent<Light>();
            handLight.type = LightType.Directional;
            handLight.shadows = LightShadows.None;
            handLight.GetUniversalAdditionalLightData().renderingLayers = 1u << HandLightLayerBit;   // 手のモデルだけを照らす
        }
        handLight.enabled = true;
        handLight.intensity = Mathf.Max(0f, handLightIntensity);
        handLight.color = handLightColor;
        handLight.transform.rotation = Quaternion.Euler(handLightRotation);
    }
}
