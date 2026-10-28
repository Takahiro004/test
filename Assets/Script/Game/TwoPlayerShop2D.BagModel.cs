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
//    破れる時間(.Lift.cs の bagTearDelay)ちょうどに穴が開き、底の当たり判定が消えて中身が落ちる(.Weight.cs)。ビニールの破片も飛ぶ
//  - モデルが無いときは、袋の絵(または仮の袋の背景)を縦に伸ばして試せる
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
        public float stretch;          // 今の伸び(0〜1)
        public bool tearStarted;       // 裂けるアニメーションを始めた
        public PlayableGraph graph;    // 裂けるアニメーションを再生する仕組み
    }

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

        // 伸びのシェイプキーを探す。アニメーションで形が大きく変わっても消えないように、映っていなくても形を計算する
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.updateWhenOffscreen = true;
            int index = smr.sharedMesh != null && !string.IsNullOrEmpty(bagStretchShape) ? smr.sharedMesh.GetBlendShapeIndex(bagStretchShape) : -1;
            if (index >= 0) bag.look.stretchShapes.Add((smr, index));
        }
        if (!string.IsNullOrEmpty(bagStretchShape) && bag.look.stretchShapes.Count == 0)
            Debug.LogWarning($"[Shop2D] 袋の3Dモデル {bagModel.name} に、シェイプキー「{bagStretchShape}」がありません(伸びない)");

        // 袋の絵と仮の袋の四角は隠す(当たり判定はそのまま)
        foreach (var part in new[] { bag.back, bag.left, bag.right, bag.bottom })
            part.GetComponent<SpriteRenderer>().enabled = false;

        bag.look.holder = holder;
        bag.look.model = model;
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

        // モデルが無いとき: 袋の絵(仮の袋の背景)を縦に伸ばす(上のふちはそのまま、底が垂れる)。幅は少し細くなる
        var back = bag.back;
        Vector2 size = back.GetComponent<SpriteRenderer>().sprite.bounds.size;
        float sag = bagHeight * 0.15f * look.stretch;
        back.localScale = new Vector3(bagWidth * (1f - 0.08f * look.stretch) / Mathf.Max(size.x, 0.01f), (bagHeight + sag) / Mathf.Max(size.y, 0.01f), 1f);
        back.localPosition = new Vector3(bagX, floorY + bagHeight / 2f - sag / 2f, 0f) + shake;
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

    // 穴が開いたとき(.Weight.cs から): ビニールの破片を、袋の底から飛ばす
    void SpawnTearPieces(Player p)
    {
        float bottom = floorY + liftHeight;
        for (int i = 0; i < bagTearPieces; i++)
        {
            var piece = MakeBagPart($"TearPiece_{p.name}", null, p.layer, bagTearPieceColor, 600, false);
            float w = Random.Range(0.06f, 0.18f), h = Random.Range(0.04f, 0.12f);
            piece.position = new Vector3(bagX + Random.Range(-0.4f, 0.4f) * bagWidth, bottom, 0f);
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
