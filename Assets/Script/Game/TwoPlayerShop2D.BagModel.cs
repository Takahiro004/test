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
//  - 破れ方: 底が真ん中から割れて開き(bagBottomOpenTime 秒で)、真ん中から中身が落ちる。
//    当たり判定は、底を左右2枚の扉(左は左のふち・右は右のふちを軸)にして bagBottomOpenAngle 度まで下へ回す。
//    見た目は、袋の形は崩さずに底の真ん中に四角い穴が開き、左右のふちが脚のように垂れる(「冂」の形)。モデルならシェイプキー bagOpenShape を 0→100 にする
//  - モデルが無いときは、袋の絵(または仮の袋の背景)を網目の形(メッシュ)にして曲げる: 底はそのまま(中の商品が乗っている)、
//    持ち手のほうが上へ伸びて細くなり(bagStretchAmount・bagHandleThin)、下のほうは丸くふくらむ(bagBottomBulge・bagBottomRound)。
//    破れたら、下のほう(bagBottomFlapHeight)の真ん中を穴にする
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
        public bool snapping;          // 破れてゴムパッチンのように跳ね戻っている最中(伸びは SnapBackBag が動かす)
        public float snapOffset;       // 跳ね戻りで、持ち上げた高さから上下にずれている分
        public Transform flapLeft, flapRight;   // 破れたあとの底の当たり判定(左右2枚。ふちを軸に回る)
        public float stretch;          // 今の伸び(0〜1)
        public bool tearStarted;       // 裂けるアニメーションを始めた
        public PlayableGraph graph;    // 裂けるアニメーションを再生する仕組み
        public Mesh mesh;              // モデルが無いとき、袋の絵を曲げる網目(初めて伸びたときに作る)
        public Mesh frontMesh;         // 手前の袋の絵の網目(bagFrontSprite があるとき。奥と同じ点を使う)
        public Transform meshTransform, frontMeshTransform;   // 網目を出しているもの(画像の大きさ bagSpriteScale / bagFrontScale をかける)
        public Vector3[] vertices;
    }

    const int BagMeshColumns = 20, BagMeshRows = 24;   // 袋の絵を曲げる網目の細かさ(列は偶数。左右半分ずつに分ける)
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

        // 袋の絵(奥・手前)と仮の袋の四角は隠す(当たり判定はそのまま)
        foreach (var part in new[] { bag.back, bag.front, bag.left, bag.right, bag.bottom })
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
        if (look == null || look.snapping) return;   // 跳ね戻り中は SnapBackBag が形を動かす

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

    // 網目の行の高さ(v: 0 底 〜 1 持ち手の先)。伸びは上のほう(持ち手)ほど大きい
    float RowY(float v, float extra)
        => floorY + bagHeight * v + extra * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, v));

    // 袋の絵(奥の絵と、あれば手前の絵)と同じ絵・同じ描く順番の網目を作り、絵のほうは隠す。
    // 奥と手前は同じ点(look.vertices)を使うので、伸びる・破れる形がそろう
    void BuildBagMesh(Player p, Bag bag)
    {
        var look = bag.look;
        look.vertices = new Vector3[2 * (BagHalfColumns + 1) * (BagMeshRows + 1)];
        look.mesh = MakeBagMeshLayer(p, bag, bag.back.GetComponent<SpriteRenderer>(), "BagMesh", out look.meshTransform);
        var frontSr = bag.front.GetComponent<SpriteRenderer>();
        if (frontSr.sprite != null && bagFrontSprite != null)
            look.frontMesh = MakeBagMeshLayer(p, bag, frontSr, "BagFrontMesh", out look.frontMeshTransform);
    }

    // 1枚の絵(SpriteRenderer)の網目を作る
    Mesh MakeBagMeshLayer(Player p, Bag bag, SpriteRenderer sr, string objName, out Transform meshTransform)
    {
        var look = bag.look;
        var sprite = sr.sprite;
        // 絵の中の範囲(画像をまとめた1枚の中にあっても、その部分だけ使う)
        Vector2 uvMin = Vector2.one, uvMax = Vector2.zero;
        foreach (var uv in sprite.uv) { uvMin = Vector2.Min(uvMin, uv); uvMax = Vector2.Max(uvMax, uv); }

        // 左の半分と右の半分を別々の網目にする(真ん中の点は2つ。破れたら真ん中で割れる)
        int n = look.vertices.Length;
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

        var mesh = new Mesh { name = objName };
        mesh.MarkDynamic();
        mesh.vertices = look.vertices;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;

        var go = new GameObject(objName);
        go.layer = p.layer;
        go.transform.SetParent(bag.root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = sr.sharedMaterial;   // 絵と同じマテリアル
        var block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", sprite.texture);
        mr.SetPropertyBlock(block);
        mr.sortingLayerID = sr.sortingLayerID;
        mr.sortingOrder = sr.sortingOrder;
        sr.enabled = false;
        meshTransform = go.transform;
        return mesh;
    }

    // 網目の点を動かす。s は伸び(0〜1)。底(floorY)は動かさない。
    // 破れたら(look.open)、袋の形は崩さずに、底の真ん中に四角い穴が開く(「冂」の形)。
    // 下のほう(bagBottomFlapHeight)の真ん中の点を穴の上のふちまで引き上げ、左右のふち(bagLegWidth)だけ脚のように少し下へ垂らす(bagLegDrop)
    void DeformBagMesh(Bag bag, float s, Vector3 shake)
    {
        var look = bag.look;
        float extra = bagHeight * Mathf.Max(0f, bagStretchAmount) * s;   // 持ち手が上へ伸びる分
        float holeTop = Mathf.Clamp(bagBottomFlapHeight, 0.01f, 1f);     // 穴の上のふち(下からの割合)
        float leg = Mathf.Clamp(bagLegWidth, 0.01f, 0.45f);               // 脚の幅(袋の幅の割合)
        float holeY = RowY(holeTop, extra);
        for (int r = 0; r <= BagMeshRows; r++)
        {
            float v = (float)r / BagMeshRows;   // 0: 底 〜 1: 持ち手の先
            float y = RowY(v, extra);
            // 幅: 上のほうは細く、下のほうはふくらむ(一番ふくらむのは下から3割くらい)
            float thin = Mathf.Clamp01(bagHandleThin) * s * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, v));
            float bulge = Mathf.Max(0f, bagBottomBulge) * s * Mathf.Sin(Mathf.PI * Mathf.Clamp01(v / 0.65f));
            float width = bagWidth * (1f - thin + bulge);
            for (int h = 0; h < 2; h++)
                for (int c = 0; c <= BagHalfColumns; c++)
                {
                    float u = (float)(h * BagHalfColumns + c) / BagMeshColumns - 0.5f;   // -0.5: 左 〜 0.5: 右
                    // 底の角を持ち上げて、底を丸くする(真ん中の底は動かさない)
                    float round = Mathf.Max(0f, bagBottomRound) * s * bagHeight * (4f * u * u) * Mathf.Pow(1f - Mathf.Clamp01(v / 0.5f), 2f);
                    Vector2 point = new Vector2(bagX + u * width, y + round);
                    if (look.open > 0f && v < holeTop)
                    {
                        // inner: 真ん中は 1(穴になる)、脚は 0(残る)。間はなめらかにして、穴の角を丸くする
                        float inner = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f - leg, 0.5f - leg - 0.12f, Mathf.Abs(u)));
                        float below = 1f - v / holeTop;   // 穴の上のふちで 0、底で 1
                        point.y = Mathf.Lerp(point.y, holeY, look.open * inner);
                        point.y -= Mathf.Max(0f, bagLegDrop) * bagHeight * look.open * (1f - inner) * below;   // 脚が垂れる
                    }
                    look.vertices[BagMeshIndex(h, r, c)] = (Vector3)point + shake;
                }
        }
        look.mesh.vertices = look.vertices;
        look.mesh.RecalculateBounds();
        if (look.frontMesh != null)
        {
            look.frontMesh.vertices = look.vertices;
            look.frontMesh.RecalculateBounds();
        }
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

    // 破れたとき(.Weight.cs から): ゴムパッチンのように、伸びていた袋が上へ跳ね戻る。
    // 持ち上げた高さから bagSnapHeight だけ上へ飛び出し、上下にびよんびよんしながら bagSnapTime 秒で止まる(bagSnapBounces 回)。
    // 伸びていた形も一緒に戻り、戻りすぎて一瞬縮む(伸びがマイナス = 持ち手が短く太くなる)
    IEnumerator SnapBackBag(Player p)
    {
        var bag = p.bag;
        var look = bag.look;
        look.snapping = true;
        float startStretch = Mathf.Max(look.stretch, 0.5f);   // 伸びが小さくても、しっかり跳ねる
        float duration = Mathf.Max(0.1f, bagSnapTime);
        float omega = 2f * Mathf.PI * Mathf.Max(0.5f, bagSnapBounces) / duration;   // 揺れの速さ
        float damping = 4f / duration;                                                // だんだん小さくなる速さ
        for (float time = 0f; time < duration; time += Time.fixedDeltaTime)
        {
            float decay = Mathf.Exp(-damping * time);
            // 時間の進み方を最初だけ速くして、パチンと一気に飛び出す(あとの揺れは今までどおり)
            float phase = omega * duration * Mathf.Pow(time / duration, 0.6f);
            look.snapOffset = bagSnapHeight * decay * Mathf.Sin(phase);       // 最初に上へ飛び出す
            look.stretch = Mathf.Max(-0.6f, startStretch * decay * Mathf.Cos(phase));
            if (!liftMoving) bag.body.MovePosition(new Vector2(0f, liftHeight + look.snapOffset));   // 持ち上げの動きが終わったあとは、ここで動かす
            foreach (var (smr, index) in look.stretchShapes)
                if (smr != null) smr.SetBlendShapeWeight(index, Mathf.Max(0f, look.stretch) * 100f);
            if (look.mesh != null) DeformBagMesh(bag, look.stretch, Vector3.zero);
            yield return new WaitForFixedUpdate();
        }
        look.snapOffset = 0f;
        look.stretch = 0f;
        if (look.mesh != null) DeformBagMesh(bag, 0f, Vector3.zero);
        if (!liftMoving) bag.body.MovePosition(new Vector2(0f, liftHeight));
        look.snapping = false;
    }

    // 跳ね戻りで、持ち上げた高さからずれている分(.Lift.cs が袋を動かすときに足す)
    float BagSnapOffset(Player p) => p.bag?.look != null ? p.bag.look.snapOffset : 0f;

    // 破れたとき(.Weight.cs から): 底を左右2枚の扉にして、bagBottomOpenTime 秒で下へ開く。
    // 当たり判定(左右2枚)と見た目(網目 / モデルのシェイプキー)を一緒に動かす
    IEnumerator OpenBagBottom(Player p)
    {
        var bag = p.bag;
        var look = bag.look;
        // 袋の中身(このあと .Weight.cs で袋の中の一覧は空になる)は、底から出たら左右へ流す
        StartCoroutine(FlowSpilledItems(p, new List<GameObject>(p.inBag)));
        float t = Mathf.Max(0.05f, bagWallThickness);
        float half = bagWidth / 2f;
        // 左の扉は左下の角、右の扉は右下の角を軸にする(底の上の面の高さ)。扉の厚さは底と同じ
        look.flapLeft = MakeBottomFlap(p, "FlapLeft", bagX - half, half, t);
        look.flapRight = MakeBottomFlap(p, "FlapRight", bagX + half, -half, t);
        if (bagSnapBack)
        {
            // 袋が上へ跳ね戻るときは、扉の当たり判定は使わない(中身を上へはじき飛ばさないように。中身はそのまま下へ落ちる)
            look.flapLeft.GetComponent<Collider2D>().enabled = false;
            look.flapRight.GetComponent<Collider2D>().enabled = false;
        }
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
        // 開ききったら、扉の当たり判定は消す(下へ垂れた扉が壁になって、中身が左右へ流れられなくならないように。見た目は残る)
        look.flapLeft.GetComponent<Collider2D>().enabled = false;
        look.flapRight.GetComponent<Collider2D>().enabled = false;
    }

    // 破れた袋から出た中身を、左右へ流す。袋の底より下へ出たところで、袋の真ん中から外向きに押す(速さはばらばら)。
    // 机の上は滑りやすくして流れるようにする。画面の外へ流れたものは消える(遅いものは机に残る)
    IEnumerator FlowSpilledItems(Player p, List<GameObject> items)
    {
        if (spillMaterial == null) spillMaterial = new PhysicsMaterial2D("Spill") { friction = 0.05f, bounciness = 0.1f };
        var pushed = new HashSet<GameObject>();
        float edge = PlayerViewSize().x / 2f + 0.5f;   // 画面の端(ここより外へ出たら消す)
        for (float time = 0f; time < 8f && items.Count > 0; time += Time.fixedDeltaTime)
        {
            float bottom = floorY + liftHeight - 0.1f;   // 今の袋の底
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var go = items[i];
                if (go == null) { items.RemoveAt(i); continue; }
                var rb = go.GetComponent<Rigidbody2D>();
                if (rb == null || !rb.simulated) { items.RemoveAt(i); continue; }
                Vector2 pos = go.transform.position;
                if (Mathf.Abs(pos.x) > edge)
                {
                    items.RemoveAt(i);
                    RemoveProduct(p, go);   // 画面の外へ流れた
                    continue;
                }
                if (pushed.Contains(go) || pos.y > bottom) continue;
                // 袋の底から出た: 外向き(真ん中あたりならどちらか)へ押し、滑りやすくする
                pushed.Add(go);
                float dx = pos.x - bagX;
                float side = Mathf.Abs(dx) > 0.1f ? Mathf.Sign(dx) : (Random.value < 0.5f ? -1f : 1f);
                rb.linearVelocity = new Vector2(side * Random.Range(0.4f, 2.4f) * bagTearScatter, rb.linearVelocity.y);
                foreach (var col in go.GetComponentsInChildren<Collider2D>()) col.sharedMaterial = spillMaterial;
            }
            yield return new WaitForFixedUpdate();
        }
    }

    PhysicsMaterial2D spillMaterial;   // こぼれた中身を机の上で滑らせる

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
