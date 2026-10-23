using System.Collections;
using UnityEngine;

// タイムアップのあと、結果発表の前に 1P/2P のレジ袋を持ち上げる(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs)
// 詰め放題と同じで、持ち上げたときに袋からこぼれた商品は数えない(消えて、その金額を合計から引く)
//  1. bagLiftDelay 秒: 落ちている途中の商品が袋に入るのを待つ(いつもどおり CheckBag で数える)
//  2. bagLiftTime 秒: 袋(Kinematic の Rigidbody2D)を MovePosition で上へ動かす。中の商品は底に押されて一緒に上がる
//  3. bagLiftHoldTime 秒: 持ち上げたまま待つ
//  2〜3 の間、袋の底より下へ落ちた商品は「こぼれた」として消し、合計から引く(Subtract Spilled From Total が ON のとき)
//  袋の中が重すぎたら、bagTearAt まで上がったところで袋が破れて中身が散らばり、その人は0円(TwoPlayerShop2D.Weight.cs)
public partial class TwoPlayerShop2D
{
    bool liftStarted, liftDone;
    float liftHeight;   // 今持ち上げている高さ

    // 持ち上げている途中なら true(UpdateResult はそこで止まる)
    bool UpdateBagLift()
    {
        if (liftDone || !bagLiftOnTimeUp || !createBag || p1.bag == null || p2.bag == null) return false;
        if (!liftStarted)
        {
            liftStarted = true;
            StartCoroutine(BagLiftRoutine());
        }
        return true;
    }

    IEnumerator BagLiftRoutine()
    {
        // 途中の掴み合い・キックのゲージは消す
        if (clash != null) { Destroy(clash.root); clash = null; }
        EndKickGauge(p1);
        EndKickGauge(p2);

        // 1. 落ちている途中の商品を待つ
        for (float time = 0f; time < bagLiftDelay; time += Time.deltaTime)
        {
            CheckBag(p1);
            CheckBag(p2);
            yield return null;
        }

        // キックの揺れが残っていたら止めて、袋を元の位置へ
        foreach (var p in new[] { p1, p2 })
        {
            if (p.shakeRoutine != null) { StopCoroutine(p.shakeRoutine); p.shakeRoutine = null; }
            p.bag.body.MovePosition(Vector2.zero);
            foreach (var go in p.inBag)   // 袋の中で止まっている商品を起こす(止まったままだと底に押されない)
            {
                var rb = go != null ? go.GetComponent<Rigidbody2D>() : null;
                if (rb != null) rb.WakeUp();
            }
        }
        lastEvent = "タイムアップ！ 袋を持ち上げる";
        Debug.Log($"[Shop2D] {lastEvent}");

        // 持ち上げる高さ: 画面の高さの bagLiftRatio。袋の上の合計金額が画面の上からはみ出さないところまで
        float viewTop = PlayerViewSize().y / 2f;
        float labelTop = floorY + bagHeight + 0.2f + HudTextSize * 8f;
        float target = Mathf.Clamp(PlayerViewSize().y * bagLiftRatio, 0f, Mathf.Max(0f, viewTop - labelTop));

        // 2. 持ち上げる → 3. そのまま待つ
        float total = Mathf.Max(0.01f, bagLiftTime) + Mathf.Max(0f, bagLiftHoldTime);
        for (float time = 0f; time < total; time += Time.fixedDeltaTime)
        {
            float k = Mathf.Clamp01(time / Mathf.Max(0.01f, bagLiftTime));
            liftHeight = target * Mathf.SmoothStep(0f, 1f, k);
            // 重すぎる袋は、bagTearAt まで上がったところで破れる(中身が散らばり0円。TwoPlayerShop2D.Weight.cs)
            if (k >= bagTearAt) { TryTearBagOnLift(p1); TryTearBagOnLift(p2); }
            p1.bag.body.MovePosition(new Vector2(0f, liftHeight));
            p2.bag.body.MovePosition(new Vector2(0f, liftHeight));
            CheckSpill(p1);
            CheckSpill(p2);
            yield return new WaitForFixedUpdate();
        }
        CheckSpill(p1);
        CheckSpill(p2);

        lastEvent = $"袋を持ち上げた: 1P ¥{p1.total} / 2P ¥{p2.total}";
        Debug.Log($"[Shop2D] {lastEvent}");
        liftDone = true;   // 次の UpdateResult から結果発表へ(袋は持ち上げたまま)
    }

    // 持ち上げた袋の底より下へ落ちた商品は、こぼれたとして消し、合計から引く
    void CheckSpill(Player p)
    {
        float bottom = floorY + liftHeight - 0.2f;   // 今の袋の底(少しめり込んでいても袋の中)
        for (int i = p.inBag.Count - 1; i >= 0; i--)
        {
            var go = p.inBag[i];
            if (go == null) { p.inBag.RemoveAt(i); continue; }
            if (go.transform.position.y >= bottom) continue;

            p.inBag.RemoveAt(i);
            priceOf.TryGetValue(go, out int price);
            if (subtractSpilledFromTotal)
            {
                p.total -= price;
                p.bag.totalLabel.text = $"¥{p.total}";
                StartCoroutine(MinusPopup(p, price));
            }
            RemoveProduct(p, go);
            lastEvent = $"{p.name}の {go.name} が持ち上げた袋からこぼれた -¥{price}(合計 ¥{p.total})";
            Debug.Log($"[Shop2D] {lastEvent}");
        }
    }
}
