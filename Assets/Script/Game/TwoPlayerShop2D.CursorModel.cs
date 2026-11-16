using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// カーソルを3Dモデル(手など)にする(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「カーソル」)
// p1CursorModel / p2CursorModel にプレハブを入れると、画像のカーソルの代わりに出る。
//  - 大きさ: 縦横の長いほうが cursorScale x cursorModelSize(画像のカーソルと同じくらい)
//  - 掴む点(カーソルの位置)は、モデルの真ん中 + cursorModelOffset(カーソルの大きさを1として)
//  - 描く順番は画像のカーソルと同じ CursorOrder(2900。商品・画面の枠(2800)より手前、大きな文字・結果発表(3000〜)より奥)。3Dモデルの商品と同じく、描く順のグループを透明にして sortingOrder で並べる
//  - 手は全部 z=0 より手前に置く(3Dモデルの商品・棚は z=0 より奥。手がめり込んで後ろに隠れないように)
//  - 当たり判定は付けない(掴む判定の邪魔になるため)
//  - モーション: 商品を掴んだら cursorGrabClip(つかむ)、離したら cursorReleaseClip(はなす)を、そのプレイヤーの手(棚・自分の画面の両方)で再生する。
//    最後のコマで止まる(クリップの Loop Time は OFF のまま)。クリップは手のモデル(FBX)の ▶ を開いた中にある
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
            foreach (var r in renderers) r.sortingOrder = CursorOrder;
        }
        if (mirror) holder.localScale = new Vector3(-1f, 1f, 1f);

        SetLayer(go, layer);
        return go;
    }

    // カーソルの手のモーションを再生している仕組み(手ごとに1つ。新しいモーションを始めたら前のは片付ける)
    readonly Dictionary<Transform, PlayableGraph> cursorGraphs = new Dictionary<Transform, PlayableGraph>();

    // 掴んだ(grab = true)/ 離した(false)ときに、そのプレイヤーの手のモーションを再生する
    void PlayCursorMotion(Player p, bool grab)
    {
        var clip = grab ? cursorGrabClip : cursorReleaseClip;
        if (clip == null || p == null) return;
        PlayCursorClip(p.cursorShop, clip);
        PlayCursorClip(p.cursorOwn, clip);
    }

    void PlayCursorClip(Transform cursor, AnimationClip clip)
    {
        if (cursor == null) return;
        var model = cursor.Find("Mirror/Model");   // 3Dモデルの手(MakeModelCursor)。画像のカーソルなら何もしない
        if (model == null) return;
        if (cursorGraphs.TryGetValue(cursor, out var old) && old.IsValid()) old.Destroy();
        var animator = model.GetComponentInChildren<Animator>();
        if (animator == null) animator = model.gameObject.AddComponent<Animator>();
        var playable = AnimationPlayableUtilities.PlayClip(animator, clip, out var graph);
        playable.SetSpeed(Mathf.Max(0.01f, cursorMotionSpeed));   // モーションの速さ(2 なら2倍速)
        cursorGraphs[cursor] = graph;
    }

    // OnDestroy から: モーションの再生の仕組みを片付ける
    void DestroyCursorGraphs()
    {
        foreach (var g in cursorGraphs.Values) if (g.IsValid()) g.Destroy();
        cursorGraphs.Clear();
    }
}
