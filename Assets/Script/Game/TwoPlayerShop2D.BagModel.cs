using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// レジ袋の3Dモデルと、破れる演出(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「レジ袋の3Dモデル・破れる演出」)
//  - bagModel にプレハブを入れると、袋の絵(と仮の袋の四角)の代わりに出す。袋の幅・高さぴったりに引き伸ばす。
//    当たり判定は今までどおり見えない2Dの壁(左右・底)。モデルは見た目だけで、袋と一緒に持ち上がる・揺れる
//  - 伸びる: 持ち上げると、中の重さが破れる重さの bagStretchStart を超えた分だけ、
//    モデルのシェイプキー bagStretchShape を強くする(重いほど伸びる)。破れる直前は小刻みに震える(bagTearShake)
//  - 裂ける: 破れる bagTearOpenTime 秒前に、裂けるアニメーション bagTearClip を再生し始める。
//    破れる時間(.Lift.cs の bagTearDelay)ちょうどに破れる(.Weight.cs)。ビニールの破片も飛ぶ
//  - 破れ方: 底が真ん中から割れて、左右2枚の扉のように下へ開き(bagBottomOpenAngle 度まで bagBottomOpenTime 秒で)、真ん中から中身が落ちる。
//    当たり判定も、底を左右2枚(左は左のふち・右は右のふちを軸)にして一緒に回す。モデルならシェイプキー bagOpenShape を 0→100 にする
//  - モデルが無いときは、袋の絵(または仮の袋の背景)を網目の形(メッシュ)にして曲げる: 底はそのまま(中の商品が乗っている)、
//    持ち手のほうが上へ伸びて細くなり(bagStretchAmount・bagHandleThin)、下のほうは丸くふくらむ(bagBottomBulge・bagBottomRound)。
//    網目は左右の半分に分けて作り(真ん中の点は2つ)、破れたら下のほう(bagBottomFlapHeight)を左右へめくる
public partial class TwoPlayerShop2D
{
    // 袋の見た目(3Dモデル・伸び・裂けるアニメーション)
    class BagLook
    {
        public Transform holder;       // 袋の大きさに合わせる入れ物(モデルはこの中で向きだけ変える)
        public GameObject model;
        public Vector3 center, size;   // 入れ物の大きさ 1 のときの、モデルの真ん中と大きさ
        public Vector3 basePosition;   // 震えていないときの入れ物の位置
        public readonly List<(SkinnedMeshRenderer renderer, int index)> stretchShapes = new List<(SkinnedMeshRenderer, int)>();
        public readonly List<(SkinnedMeshRenderer renderer, int index)> openShapes = new List<(SkinnedMeshRenderer, int)>();   // 底が開いた形
        public float open;             // 底の開き具合(0: 閉じている 〜 1: 開ききった)
        public Transform flapLeft, flapRight;   // 破れたあとの底の当たり判定(左右2枚。ふちを軸に回る)
        public float stretch;          // 今の伸び(0〜1)
        public bool tearStarted;       // 裂けるアニメーションを始めた
        public PlayableGraph graph;    // 裂けるアニメーションを再生する仕組み
        public Mesh mesh;              // モデルが無いとき、袋の絵を曲げる網目(初めて伸びたときに作る)
        public Vector3[] vertices;
    }

    const int BagMeshColumns = 10, BagMeshRows = 16;   // 袋の絵を曲げる網目の細かさ(列は偶数。左右半分ずつに分ける)
    const int BagHalfColumns = BagMeshColumns / 2;

    // 網目の点の番号。h: 0 左の半分 / 1 右の半分、r: 下からの行、c: その半分の中での列
    static int BagMeshIndex(int h, int r, int c) => (h * (BagMeshRows + 1) + r) * (BagHalfColumns + 1) + c;

    // CreateBag から: モデルを作る(無ければ見た目の記録だけ)
    void BuildBagModel(Player p, Bag bag)
    {
        bag.look = new BagLook();
        if (bagModel == null) return;

        var holder = new GameObject("BagModel").transform;
        holder.gameObject.layer = p.layer;
        holder.SetParent(bag.root, false);
        var model = Instantiate(bagModel, holder);
        model.name = "Model";
        SetLayer(model, p.layer);
        // プレハブで付けた向き・大きさは残し、その上に bagModelRotation を足す
        model.transform.localRotation = Quaternion.Euler(bagModelRotation) * bagModel.transform.localRotation;
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale = bagModel.transform.localScale;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[Shop2D] 袋の3Dモデル {bagModel.name} に見た目(Renderer)がありません");
            Destroy(holder.gameObject);
            return;
        }
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        bag.look.center = b.center - holder.position;
        bag.look.size = b.size;

        // 袋の背景と同じく商品より奥に描く(UseSpriteOrder は TwoPlayerShop2D.Products.cs)。
        // 中の商品を見せたいときは、モデルのマテリアルを半透明にするか、手前の面を無くしておく
        UseSpriteOrder(renderers);
        foreach (var r in renderers) r.sortingOrder = -20;

        // 伸び・底が開いた形のシェイプキーを探す。形が大きく変わっても消えないように、映っていなくても形を計算する
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            smr.updateWhenOffscreen = true;
        FindBlendShapes(model, bagStretchShape, bag.look.stretchShapes, "伸びない");
        FindBlendShapes(model, bagOpenShape, bag.look.openShapes, "底の開き方は当たり判定だけ");

        // 袋の絵と仮の袋の四角は隠す(当たり判定はそのまま)
        foreach (var part in new[] { bag.back, bag.left, bag.right, bag.bottom })
            part.GetComponent<SpriteRenderer>().enabled = false;

        bag.look.holder = holder;
        bag.look.model = model;
    }

    // モデルの中から、名前が shape のシェイプキーを全部探す
    void FindBlendShapes(GameObject model, string shape, List<(SkinnedMeshRenderer, int)> found, string whenMissing)
    {
        if (string.IsNullOrEmpty(shape)) return;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            int index = smr.sharedMesh != null ? smr.sharedMesh.GetBlendShapeIndex(shape) : -1;
            if (index >= 0) found.Add((smr, index));
        }
        if (found.Count == 0)
            Debug.LogWarning($"[Shop2D] 袋の3Dモデル {bagModel.name} に、シェイプキー「{shape}」がありません({whenMissing})");
    }

    // LayoutBags から: モデルを袋の幅・高さぴったりにする(奥行きは幅と高さの倍率の間)。さらに bagModelScale をかける
    void LayoutBagModel(Bag bag)
    {
        var look = bag.look;
        if (look == null || look.holder == null) return;
        float sx = bagWidth / Mathf.Max(0.01f, look.size.x);
        float sy = bagHeight / Mathf.Max(0.01f, look.size.y);
        Vector3 scale = Vector3.Scale(new Vector3(sx, sy, (sx + sy) / 2f), bagModelScale);
        look.holder.localScale = scale;
        look.basePosition = new Vector3(bagX, floorY + bagHeight / 2f, 0f) - Vector3.Scale(look.center, scale) + bagModelOffset;
        look.holder.localPosition = look.basePosition;
    }

    // 持ち上げている間(.Lift.cs から FixedUpdate ごと): 重さで伸びる・破れる直前に震える。lift は持ち上げた割合(0〜1)
    void UpdateBagLook(Player p, float lift)
    {
        var bag = p.bag;
        var look = bag?.look;
        if (look == null) return;

        float ratio = BagWeight(p) / Mathf.Max(0.01f, bagMaxWeight);   // 破れる重さの何倍か
        bool willTear = bagTearEnabled && ratio > 1f;
        // 破れたら(穴が開いたら)縮む。それまでは、重いほど・高く持ち上げるほど伸びる
        float target = bag.torn ? 0f : Mathf.InverseLerp(Mathf.Clamp01(bagStretchStart), 1f, ratio) * lift;
        look.stretch = Mathf.MoveTowards(look.stretch, target, Time.fixedDeltaTime * (bag.torn ? 4f : 1.5f));

        // 破れる直前(上まで持ち上げて、まだ破れていない)は小刻みに震える
        Vector3 shake = Vector3.zero;
        if (willTear && !bag.torn && lift >= 1f)
            shake = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * bagTearShake;

        if (look.holder != null)
        {
            foreach (var (smr, index) in look.stretchShapes)
                if (smr != null) smr.SetBlendShapeWeight(index, look.stretch * 100f);
            look.holder.localPosition = look.basePosition + shake;
            return;
        }

        // モデルが無いとき: 袋の絵を曲げる
        if (look.mesh == null && look.stretch <= 0f) return;   // まだ伸びていなければ、今までの絵のまま
        if (look.mesh == null) BuildBagMesh(p, bag);
        DeformBagMesh(bag, look.stretch, shake);
    }

    // 袋の絵(SpriteRenderer)と同じ絵・同じ描く順番の網目を作り、絵のほうは隠す
    void BuildBagMesh(Player p, Bag bag)
    {
        var look = bag.look;
        var sr = bag.back.GetComponent<SpriteRenderer>();
        var sprite = sr.sprite;
        // 絵の中の範囲(画像をまとめた1枚の中にあっても、その部分だけ使う)
        Vector2 uvMin = Vector2.one, uvMax = Vector2.zero;
        foreach (var uv in sprite.uv) { uvMin = Vector2.Min(uvMin, uv); uvMax = Vector2.Max(uvMax, uv); }

        // 左の半分と右の半分を別々の網目にする(真ん中の点は2つ。破れたら真ん中で割れる)
        int n = 2 * (BagHalfColumns + 1) * (BagMeshRows + 1);
        var uvs = new Vector2[n];
        var colors = new Color[n];
        for (int h = 0; h < 2; h++)
            for (int r = 0; r <= BagMeshRows; r++)
                for (int c = 0; c <= BagHalfColumns; c++)
                {
                    int i = BagMeshIndex(h, r, c);
                    float u = (float)(h * BagHalfColumns + c) / BagMeshColumns;   // 0: 左 〜 1: 右
                    uvs[i] = new Vector2(Mathf.Lerp(uvMin.x, uvMax.x, u), Mathf.Lerp(uvMin.y, uvMax.y, (float)r / BagMeshRows));
                    colors[i] = sr.color;
                }
        var triangles = new int[BagMeshColumns * BagMeshRows * 6];
        int t = 0;
        for (int h = 0; h < 2; h++)
            for (int r = 0; r < BagMeshRows; r++)
                for (int c = 0; c < BagHalfColumns; c++)
                {
                    int i = BagMeshIndex(h, r, c), up = BagMeshIndex(h, r + 1, c);
                    triangles[t++] = i; triangles[t++] = up; triangles[t++] = i + 1;
                    triangles[t++] = i + 1; triangles[t++] = up; triangles[t++] = up + 1;
                }

        look.vertices = new Vector3[n];
        look.mesh = new Mesh { name = "BagMesh" };
        look.mesh.MarkDynamic();
        look.mesh.vertices = look.vertices;
        look.mesh.uv = uvs;
        look.mesh.colors = colors;
        look.mesh.triangles = triangles;

        var go = new GameObject("BagMesh");
        go.layer = p.layer;
        go.transform.SetParent(bag.root, false);
        go.AddComponent<MeshFilter>().sharedMesh = look.mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = sr.sharedMaterial;   // 絵と同じマテリアル
        var block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", sprite.texture);
        mr.SetPropertyBlock(block);
        mr.sortingLayerID = sr.sortingLayerID;
        mr.sortingOrder = sr.sortingOrder;
        sr.enabled = false;
    }

    // 網目の点を動かす。s は伸び(0〜1)。底(floorY)は動かさない。
    // 破れたら(look.open)、下のほうを、左の半分は左下の角・右の半分は右下の角を軸に下へめくる(真ん中ほど大きく開く)
    void DeformBagMesh(Bag bag, float s, Vector3 shake)
    {
        var look = bag.look;
        float extra = bagHeight * Mathf.Max(0f, bagStretchAmount) * s;   // 持ち手が上へ伸びる分
        float bottomWidth = bagWidth;   // 底の幅(v = 0 ではふくらまない)
        float flapHeight = Mathf.Clamp(bagBottomFlapHeight, 0.01f, 1f);
        for (int r = 0; r <= BagMeshRows; r++)
        {
            float v = (float)r / BagMeshRows;   // 0: 底 〜 1: 持ち手の先
            // 伸びは上のほう(持ち手)ほど大きい
            float y = floorY + bagHeight * v + extra * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, v));
            // 幅: 上のほうは細く、下のほうはふくらむ(一番ふくらむのは下から3割くらい)
            float thin = Mathf.Clamp01(bagHandleThin) * s * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, v));
            float bulge = Mathf.Max(0f, bagBottomBulge) * s * Mathf.Sin(Mathf.PI * Mathf.Clamp01(v / 0.65f));
            float width = bagWidth * (1f - thin + bulge);
            // めくれる角度(下ほど大きい。flapHeight より上は動かない)
            float flap = bagBottomOpenAngle * look.open * Mathf.Pow(1f - Mathf.Clamp01(v / flapHeight), 1.5f);
            for (int h = 0; h < 2; h++)
            {
                float side = h == 0 ? -1f : 1f;
                Vector2 pivot = new Vector2(bagX + side * bottomWidth / 2f, floorY);   // 左下 / 右下の角
                Quaternion turn = Quaternion.Euler(0f, 0f, side * flap);           // 左は右回り・右は左回りで、真ん中が下がる
                for (int c = 0; c <= BagHalfColumns; c++)
                {
                    float u = (float)(h * BagHalfColumns + c) / BagMeshColumns - 0.5f;   // -0.5: 左 〜 0.5: 右
                    // 底の角を持ち上げて、底を丸くする(真ん中の底は動かさない)
                    float round = Mathf.Max(0f, bagBottomRound) * s * bagHeight * (4f * u * u) * Mathf.Pow(1f - Mathf.Clamp01(v / 0.5f), 2f);
                    Vector2 point = new Vector2(bagX + u * width, y + round);
                    if (flap != 0f) point = pivot + (Vector2)(turn * (point - pivot));
                    look.vertices[BagMeshIndex(h, r, c)] = (Vector3)point + shake;
                }
            }
        }
        look.mesh.vertices = look.vertices;
        look.mesh.RecalculateBounds();
        // 合計金額の表示も、伸びた持ち手の上へ
        bag.totalLabel.transform.localPosition = new Vector3(bagX, floorY + bagHeight + extra + 0.2f + HudTextSize * 4f, 0f);
    }

    // 破れる bagTearOpenTime 秒前(.Lift.cs から): 裂けるアニメーションを始める。1回だけ
    void StartBagTear(Player p)
    {
        var look = p.bag?.look;
        if (look == null || look.tearStarted) return;
        look.tearStarted = true;
        if (look.model == null || bagTearClip == null) return;

        var animator = look.model.GetComponentInChildren<Animator>();
        if (animator == null) animator = look.model.AddComponent<Animator>();
        AnimationPlayableUtilities.PlayClip(animator, bagTearClip, out look.graph);   // 最後のコマで止まる(ループしない設定のとき)
    }

    // 破れたとき(.Weight.cs から): 底を左右2枚の扉にして、bagBottomOpenTime 秒で下へ開く。
    // 当たり判定(左右2枚)と見た目(網目 / モデルのシェイプキー)を一緒に動かす
    IEnumerator OpenBagBottom(Player p)
    {
        var bag = p.bag;
        var look = bag.look;
        float t = Mathf.Max(0.05f, bagWallThickness);
        float half = bagWidth / 2f;
        // 左の扉は左下の角、右の扉は右下の角を軸にする(底の上の面の高さ)。扉の厚さは底と同じ
        look.flapLeft = MakeBottomFlap(p, "FlapLeft", bagX - half, half, t);
        look.flapRight = MakeBottomFlap(p, "FlapRight", bagX + half, -half, t);
        if (look.holder == null && look.mesh == null) BuildBagMesh(p, bag);

        float duration = Mathf.Max(0.05f, bagBottomOpenTime);
        for (float time = 0f; time <= duration; time += Time.fixedDeltaTime)
        {
            look.open = Mathf.SmoothStep(0f, 1f, time / duration);   // ゆっくり裂け始めて、だんだん速く開く…ように見える
            float angle = bagBottomOpenAngle * look.open;
            look.flapLeft.localRotation = Quaternion.Euler(0f, 0f, -angle);
            look.flapRight.localRotation = Quaternion.Euler(0f, 0f, angle);
            foreach (var (smr, index) in look.openShapes)
                if (smr != null) smr.SetBlendShapeWeight(index, look.open * 100f);
            if (look.mesh != null) DeformBagMesh(bag, look.stretch, Vector3.zero);
            yield return new WaitForFixedUpdate();
        }
    }

    // 底の扉(当たり判定だけ)。pivotX が軸、length の向き(+ なら右へ / - なら左へ)に伸びる
    Transform MakeBottomFlap(Player p, string objName, float pivotX, float length, float thickness)
    {
        var go = new GameObject(objName);
        go.layer = p.layer;
        go.transform.SetParent(p.bag.root, false);
        go.transform.localPosition = new Vector3(pivotX, floorY + thickness, 0f);
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(Mathf.Abs(length), thickness);
        col.offset = new Vector2(length / 2f, -thickness / 2f);
        return go.transform;
    }

    // 穴が開いたとき(.Weight.cs から): ビニールの破片を、袋の底から飛ばす
    void SpawnTearPieces(Player p)
    {
        float bottom = floorY + liftHeight;
        for (int i = 0; i < bagTearPieces; i++)
        {
            var piece = MakeBagPart($"TearPiece_{p.name}", null, p.layer, bagTearPieceColor, 600, false);
            float w = Random.Range(0.06f, 0.18f), h = Random.Range(0.04f, 0.12f);
            piece.position = new Vector3(bagX + Random.Range(-0.15f, 0.15f) * bagWidth, bottom, 0f);   // 底の真ん中の裂け目から
            piece.localScale = new Vector3(w, h, 1f);
            piece.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            Vector2 velocity = new Vector2(Random.Range(-2.5f, 2.5f), Random.Range(0.5f, 3f));
            StartCoroutine(FlyTearPiece(piece, velocity, Random.Range(-540f, 540f)));
        }
    }

    // 破片: 少し舞い上がってから落ち、ひらひら回りながら消える
    IEnumerator FlyTearPiece(Transform piece, Vector2 velocity, float spin)
    {
        var sr = piece.GetComponent<SpriteRenderer>();
        Color color = sr.color;
        const float life = 1.2f;
        for (float time = 0f; time < life && piece != null; time += Time.deltaTime)
        {
            velocity.y -= 6f * Time.deltaTime;   // ビニールなので、ふつうより軽く落ちる
            velocity.x *= 1f - 1.5f * Time.deltaTime;
            piece.position += (Vector3)(velocity * Time.deltaTime);
            piece.Rotate(0f, 0f, spin * Time.deltaTime);
            sr.color = new Color(color.r, color.g, color.b, color.a * (1f - time / life));
            yield return null;
        }
        if (piece != null) Destroy(piece.gameObject);
    }

    void OnDestroy()
    {
        // 裂けるアニメーションの再生の仕組みを片付ける
        foreach (var p in new[] { p1, p2 })
        {
            var look = p?.bag?.look;
            if (look != null && look.graph.IsValid()) look.graph.Destroy();
        }
    }
}
