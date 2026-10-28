using UnityEngine;

// カーソルを3Dモデル(手など)にする(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「カーソル」)
// p1CursorModel / p2CursorModel にプレハブを入れると、画像のカーソルの代わりに出る。
//  - 大きさ: 縦横の長いほうが cursorScale x cursorModelSize(画像のカーソルと同じくらい)
//  - 掴む点(カーソルの位置)は、モデルの真ん中 + cursorModelOffset(カーソルの大きさを1として)
//  - 描く順番は画像のカーソルと同じ 1000(商品より手前)。3Dモデルの商品と同じく、描く順のグループを透明にして sortingOrder で並べる
//  - 手は全部 z=0 より手前に置く(3Dモデルの商品・棚は z=0 より奥。手がめり込んで後ろに隠れないように)
//  - 当たり判定は付けない(掴む判定の邪魔になるため)
public partial class TwoPlayerShop2D
{
    GameObject MakeModelCursor(string objName, int layer, GameObject prefab, bool mirror)
    {
        var go = new GameObject(objName);
        var sr = go.AddComponent<SpriteRenderer>();   // 絵は無し(ほかの処理と同じ形にしておく)
        sr.sortingOrder = 1000;

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
            foreach (var r in renderers) r.sortingOrder = 1000;
        }
        if (mirror) holder.localScale = new Vector3(-1f, 1f, 1f);

        SetLayer(go, layer);
        return go;
    }
}
