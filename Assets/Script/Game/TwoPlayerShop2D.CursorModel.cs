using UnityEngine;

// カーソルを3Dモデル(手など)にする(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「カーソル」)
// p1CursorModel / p2CursorModel にプレハブを入れると、画像のカーソルの代わりに出る。
//  - 大きさ: 縦横の長いほうが cursorScale x cursorModelSize(画像のカーソルと同じくらい)
//  - 掴む点(カーソルの位置)は、モデルの真ん中 + cursorModelOffset(カーソルの大きさを1として)
//  - 描く順番は画像のカーソルと同じ 1000(商品より手前)。3Dモデルの商品と同じく、描く順のグループを透明にして sortingOrder で並べる
//  - 当たり判定は付けない(掴む判定の邪魔になるため)
public partial class TwoPlayerShop2D
{
    GameObject MakeModelCursor(string objName, int layer, GameObject prefab, bool mirror)
    {
        var go = new GameObject(objName);
        var sr = go.AddComponent<SpriteRenderer>();   // 絵は無し(ほかの処理と同じ形にしておく)
        sr.sortingOrder = 1000;

        var model = Instantiate(prefab, go.transform);
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
            model.transform.localScale = new Vector3(prefabScale.x * k * (mirror ? -1f : 1f), prefabScale.y * k, prefabScale.z * k);
            // 真ん中を掴む点に合わせて、cursorModelOffset だけずらす(反転したら左右も反転)
            Vector3 center = b.center * k;
            if (mirror) center.x = -center.x;
            Vector2 offset = cursorModelOffset * length;
            if (mirror) offset.x = -offset.x;
            model.transform.localPosition = new Vector3(offset.x - center.x, offset.y - center.y, -center.z);

            UseSpriteOrder(renderers);   // 絵と同じ sortingOrder の順で描く(TwoPlayerShop2D.Products.cs)
            foreach (var r in renderers) r.sortingOrder = 1000;
        }

        SetLayer(go, layer);
        return go;
    }
}
