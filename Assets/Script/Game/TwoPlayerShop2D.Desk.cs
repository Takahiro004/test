using System.Collections.Generic;
using UnityEngine;

// 1P/2P の画面の机の見た目(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「机の見た目」)
//  - deskModel(3Dモデル)か deskSprite(画像)を入れると、仮の机(暗い灰色の四角)の代わりに出す。両方なら3Dモデル
//  - 机の上の面(床の高さ floorY)に、画面に見えている机の幅(wallX x 2)で合わせる。画像は縦横の比率を変えずに、上の面をそろえる
//  - 机は2人の共通の机という設定なので、袋持ち上げターンでカメラを引いても端が見えないように、横だけ引いたときの幅
//    (x bagLiftCameraZoom)の、さらに deskCoverMargin 倍まで伸ばし、端がいつも画面の外(見切れる)にする
//    (deskCoverZoom。高さ・奥行きは画面に見えている机の幅のときのまま)
//  - さらに deskVisualScale(X 横・Y 縦・Z 奥行き)と deskVisualOffset(X 右・Y 上・Z 奥)をかける
//  - 見た目だけ。当たり判定(商品が乗る床・左右の見えない壁)は今までどおり。
//    当たり判定だけを動かすのは deskHitSize / deskHitOffset(ApplyFloorY)、見えるようにするのは deskHitVisible / deskHitColor
//  - Update から毎フレーム並べるので、再生中に変えてもすぐ反映する
public partial class TwoPlayerShop2D
{
    // 1つの画面の机の見た目
    class DeskLook
    {
        public Transform floor;          // 仮の机(当たり判定もこれ)
        public SpriteRenderer image;     // 画像の机
        public Transform holder;         // 3Dモデルの机の入れ物(大きさを変える)
        public GameObject builtModel;    // どのモデル・向きで作ったか
        public Vector3 builtRotation;
        public Vector3 center, size;     // 入れ物の大きさ 1 のときの、モデルの真ん中と大きさ(机の画面の原点から)
    }
    readonly List<DeskLook> deskLooks = new List<DeskLook>();

    void UpdateDeskLook()
    {
        foreach (var floor in floors)
        {
            if (floor == null) continue;
            var d = deskLooks.Find(x => x.floor == floor);
            if (d == null) { d = new DeskLook { floor = floor }; deskLooks.Add(d); }
            int layer = floor.gameObject.layer;
            var root = floor.parent;

            bool useModel = deskModel != null;
            bool useImage = !useModel && deskSprite != null;
            // 仮の机(= 机の当たり判定の形)。差し替えたら隠す。deskHitVisible なら、見た目の机の上に色を付けて重ねて出す
            var floorSr = floor.GetComponent<SpriteRenderer>();
            floorSr.enabled = deskHitVisible || (!useModel && !useImage);
            floorSr.color = deskHitVisible ? deskHitColor : new Color(0.2f, 0.2f, 0.2f, 0.9f);
            floorSr.sortingOrder = deskHitVisible ? 951 : -10;
            float deskW = wallX * 2f;   // 画面に見えている机の幅
            // 横の長さ: 机の端がいつも画面の外になるように、カメラを引いたときに映る幅 x deskCoverMargin まで伸ばす
            float coverW = deskCoverZoom
                ? deskW * (bagLiftOnTimeUp ? Mathf.Max(1f, bagLiftCameraZoom) : 1f) * Mathf.Max(1f, deskCoverMargin)
                : deskW;

            // 画像の机
            if (useImage)
            {
                if (d.image == null) d.image = MakeBagPart("DeskImage", root, layer, Color.white, -10, false).GetComponent<SpriteRenderer>();
                d.image.enabled = true;
                d.image.sprite = deskSprite;
                Vector2 size = deskSprite.bounds.size;
                float w = coverW * Mathf.Max(0.01f, deskVisualScale.x);
                float h = deskW * size.y / Mathf.Max(0.01f, size.x) * Mathf.Max(0.01f, deskVisualScale.y);   // 縦横の比率はそのまま
                d.image.transform.localPosition = new Vector3(deskVisualOffset.x, floorY - h / 2f + deskVisualOffset.y, 0f);   // 上の面を机の高さに
                d.image.transform.localRotation = Quaternion.identity;
                d.image.transform.localScale = new Vector3(w / Mathf.Max(0.01f, size.x), h / Mathf.Max(0.01f, size.y), 1f);
            }
            else if (d.image != null) d.image.enabled = false;

            // 3Dモデルの机(モデルか向きが変わったら作り直す)
            if (d.holder != null && (d.builtModel != deskModel || d.builtRotation != deskModelRotation))
            {
                Destroy(d.holder.gameObject);
                d.holder = null;
            }
            if (useModel && d.holder == null) BuildDeskModel(d, root, layer);
            if (d.holder != null)
            {
                float k = deskW / Mathf.Max(0.01f, d.size.x);    // 机の幅に合わせる(縦・奥行きも同じ割合)
                float kx = coverW / Mathf.Max(0.01f, d.size.x);  // 横だけ、カメラを引いたときの幅まで
                Vector3 scale = new Vector3(kx * Mathf.Max(0.01f, deskVisualScale.x), k * Mathf.Max(0.01f, deskVisualScale.y), k * Mathf.Max(0.01f, deskVisualScale.z));
                d.holder.localScale = scale;
                // 上の面を机の高さ・横は真ん中・手前の面を z = 0 に合わせる(商品の3Dモデルは z = 0 より少し奥)
                Vector3 c = Vector3.Scale(d.center, scale), e = Vector3.Scale(d.size / 2f, scale);
                d.holder.localPosition = new Vector3(-c.x, floorY - (c.y + e.y), -(c.z - e.z)) + deskVisualOffset;
            }
        }
    }

    void BuildDeskModel(DeskLook d, Transform root, int layer)
    {
        var holder = new GameObject("DeskModel").transform;
        holder.gameObject.layer = layer;
        holder.SetParent(root, false);
        var model = Instantiate(deskModel, holder);
        model.name = "Model";
        SetLayer(model, layer);
        // プレハブで付けた向き・大きさは残し、その上に deskModelRotation を足す
        model.transform.localRotation = Quaternion.Euler(deskModelRotation) * deskModel.transform.localRotation;
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale = deskModel.transform.localScale;
        d.holder = holder;
        d.builtModel = deskModel;
        d.builtRotation = deskModelRotation;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[Shop2D] 机の3Dモデル {deskModel.name} に見た目(Renderer)がありません");
            d.center = Vector3.zero;
            d.size = Vector3.one;
            return;
        }
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        d.center = b.center - holder.position;
        d.size = b.size;
        // 仮の机と同じく、商品より奥に描く(絵と同じ描く順番の決め方。UseSpriteOrder は TwoPlayerShop2D.Products.cs)
        UseSpriteOrder(renderers);
        foreach (var r in renderers) r.sortingOrder = -10;
    }
}
