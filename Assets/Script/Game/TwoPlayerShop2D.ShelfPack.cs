using System.Collections.Generic;
using UnityEngine;

// 棚の商品を、マス(列)をやめて詰めて並べる(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の shelfPacked)
//  - 1段に試す商品の数は 列の数 x shelfPackCapacity。すき間 shelfPackGap でできるだけ詰めて、入るだけ並べる(入らない分は山札へ戻す)
//  - 1画面ぶんの棚(PackSection)の段ごとに、下の段から順に、その段の商品の幅を測って(向きは乱雑に置くときと同じ選び方。
//    どの向きでも1段に入らない大きな商品はまっすぐ)、左から詰めて並べる。余った幅は、商品のあいだに同じだけ配る
//  - 大きな商品は広い場所を使うので、空いた列ができない。入りきらない商品は山札へ戻し、次に棚が回ってくるまでその場所は空ける(packedOut)
//  - 背の高い商品が上の段まで届いているときは、上の段のその横の範囲を空けて並べる(かぶらない)
//  - 並べるのは、試合の始め(CreateProducts)・画面の外で棚が回ったとき(RecycleSection)・乱雑の ON/OFF を変えたとき。
//    取られた場所に補充するときは、その場所の幅に入る商品を引き直す(RefillLater)
//  - 場所は slot.allocX(棚の左のはしからの商品の真ん中)。ShelfSlotPosition がそれを使う
public partial class TwoPlayerShop2D
{
    void PackAllSections()
    {
        for (int k = 0; k < RingSections; k++) PackSection(k);
    }

    void PackSection(int sec)
    {
        Rect a = ShelfArea();
        int cols = ShelfColumns, rows = ShelfRows;
        float W = a.width;
        float cellW = W / cols, cellH = a.height / rows;
        float board = showShelf ? shelfBoardThickness : 0f;
        float roomH = (cellH - board) * 0.96f;
        float minGap = Mathf.Max(0.01f, shelfPackGap);   // 商品どうしのすき間(できるだけ詰める)
        float nominalW = W / BaseColumns;                 // 向きを選ぶときの1マスの幅(Inspector の列の数で決まる)
        var forbidden = new List<Vector2>[rows];   // 段ごとの、下の段の背の高い商品が届いている横の範囲(棚の左のはしから)
        for (int r = 0; r < rows; r++) forbidden[r] = new List<Vector2>();

        for (int row = rows - 1; row >= 0; row--)   // 下の段から
        {
            // この段の商品を測る
            var items = new List<ShelfSlot>();
            var angles = new List<float>();
            var widths = new List<float>();
            var centers = new List<float>();
            var overs = new List<float>();
            for (int c = 0; c < cols; c++)
            {
                int idx = sec * ShelfCount + row * cols + c;
                if (idx >= slots.Count) continue;
                var s = slots[idx];
                s.hasAlloc = false;
                if (s.go == null) { s.packedOut = true; continue; }   // 山札が空などで、もともと空いている
                s.pos = ShelfSlotPosition(idx);
                MeasurePackPose(s.go, s, nominalW * 0.96f, roomH, out float ang, out float w, out float cx, out float over);
                items.Add(s); angles.Add(ang); widths.Add(w); centers.Add(cx); overs.Add(over);
            }

            // 入りきらなければ、右から減らす(下の段の背の高い商品の範囲も引く)
            float forbW = 0f;
            foreach (var f in forbidden[row]) forbW += f.y - f.x;
            float Sum() { float t = 0f; foreach (var w in widths) t += w; return t; }
            while (items.Count > 0 && Sum() + forbW + minGap * (items.Count + 1) > W)
            {
                int last = items.Count - 1;
                DropPacked(items[last]);
                items.RemoveAt(last); angles.RemoveAt(last); widths.RemoveAt(last); centers.RemoveAt(last); overs.RemoveAt(last);
            }

            // 余った幅を、商品のあいだに同じだけ配って、左から並べる
            float gap = Mathf.Max(minGap, (W - Sum() - forbW) / (items.Count + 1 + forbidden[row].Count));
            forbidden[row].Sort((p, q) => p.x.CompareTo(q.x));
            float cursor = 0f;
            for (int k = 0; k < items.Count; k++)
            {
                var s = items[k];
                float w = widths[k];
                cursor += gap;
                foreach (var f in forbidden[row])
                    if (cursor < f.y && cursor + w > f.x) cursor = f.y + gap;   // 下の段の背の高い商品の上はよける
                if (cursor + w > W + 0.01f) { DropPacked(s); continue; }
                float center = cursor + w / 2f;
                cursor += w;
                // 乱雑に置く: 横の位置を、あいだのすき間の中だけでばらつかせる(となりとかぶらない)
                float jitter = shelfMessyScatter ? (Hash01(s.go.GetInstanceID(), 2) * 2f - 1f) * gap * 0.45f * Mathf.Clamp01(shelfMessySpread) : 0f;
                s.hasAlloc = true;
                s.packedOut = false;
                s.allocX = center + jitter;
                s.allocW = w + gap;
                shelfPoses[s.go] = new ShelfPose { angle = angles[k], dx = -centers[k], cellW = cellW, messy = shelfMessy, scatter = shelfMessyScatter, over = overs[k] };
                int idx = slots.IndexOf(s);
                s.pos = ShelfSlotPosition(idx);
                PutOnShelf(s.go, s);

                // 上の段まで届いていたら、上の段のその横の範囲を空ける
                if (ShapeRect(s.go, out Rect r))
                {
                    float baseX = s.pos.x - s.allocX;   // この棚の左のはし(ゲームの位置)
                    for (int u = row - 1; u >= 0; u--)
                    {
                        int uIdx = sec * ShelfCount + u * cols;
                        if (uIdx >= slots.Count) break;
                        float upperBottom = ShelfSlotPosition(uIdx).y;   // 上の段の板の上
                        if (r.yMax <= upperBottom) break;
                        forbidden[u].Add(new Vector2(r.xMin - baseX - minGap, r.xMax - baseX + minGap));
                    }
                }
            }
        }
    }

    // 詰めたら入りきらなかった商品を山札へ戻し、その場所は次に棚が回ってくるまで空ける
    void DropPacked(ShelfSlot s)
    {
        if (UseDeck && s.product >= 0) { ReturnCard(s.product); s.product = -1; }
        if (s.go != null) ForgetShelfProduct(s.go);
        s.hasAlloc = false;
        s.packedOut = true;
        SetSlotTag(s);
    }

    // 商品の向きと幅を測る(乱雑に置くなら何回か向きを試して、幅 room・高さ roomH に入る向き。
    // どの向きでも入らない大きな商品はまっすぐ)。cx = 商品の見た目の真ん中が、ピボットからどれだけ横にずれているか
    void MeasurePackPose(GameObject go, ShelfSlot slot, float room, float roomH, out float angle, out float width, out float cx, out float over)
    {
        int id = go.GetInstanceID();
        angle = 0f; width = 0f; cx = 0f; over = 0f;
        float bestOver = float.MaxValue;
        int tries = shelfMessy ? 24 : 1;
        for (int t = 0; t < tries; t++)
        {
            float a = shelfMessy ? (Hash01(id, 10 + t) * 2f - 1f) * shelfMessyAngleMax : 0f;
            go.transform.rotation = Quaternion.Euler(0f, 0f, a);
            go.transform.position = slot.pos;
            if (!ShapeRect(go, out Rect r)) { angle = a; width = 0.5f; cx = 0f; over = 0f; return; }
            float o = Mathf.Max(r.width / Mathf.Max(0.01f, room), r.height / Mathf.Max(0.01f, roomH));
            if (o < bestOver) { bestOver = o; angle = a; width = r.width; cx = r.center.x - slot.pos.x; }
            if (o <= 1f) break;
        }
        over = bestOver;
        // どの向きでも1マスぶんに入らない大きな商品: 傾けずにまっすぐ(広い場所をもらう)
        if (bestOver > 1f)
        {
            go.transform.rotation = Quaternion.identity;
            go.transform.position = slot.pos;
            if (ShapeRect(go, out Rect r0)) { angle = 0f; width = r0.width; cx = r0.center.x - slot.pos.x; }
            over = 0f;   // 広い場所をもらうので、はみ出さない(補充の引き直しに使わない)
        }
    }
}
